// Behaviour follows WordCraft's Change Case and Table Layout commands (engine/src/cmd @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.Globalization;
using Em.Editing;
using Em.Write.Model;

namespace Em.Write.Editing;

/// <summary>The letter case applied by <see cref="WriteSession.ChangeCase(WriteCaseMode)"/>.</summary>
public enum WriteCaseMode
{
    /// <summary>Capitalizes the first letter of each sentence.</summary>
    Sentence,
    /// <summary>Makes all letters lowercase.</summary>
    Lower,
    /// <summary>Makes all letters uppercase.</summary>
    Upper,
    /// <summary>Capitalizes the first letter of each word.</summary>
    Title,
    /// <summary>Swaps the case of each letter.</summary>
    Toggle
}

/// <summary>Edits a word-processing document with undo and redo. Commands keep the document immutable and replace it on each change.</summary>
public sealed partial class WriteSession
{
    /// <summary>Changes the letter case of the selected text. Character formatting, fields and images are kept.</summary>
    public CommandResult ChangeCase(WriteCaseMode mode)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (Selection.IsEmpty)
            return Invalid("Select the text to change.");
        var culture = CultureInfo.CurrentCulture.TextInfo;
        return EditParagraphs((item, _) =>
        {
            var before = Slice(item.Paragraph.Runs, 0, item.Start);
            var selected = Slice(item.Paragraph.Runs, item.Start, item.End).ToArray();
            string text = string.Concat(selected.Select(run => run.PlainText));
            string changed = Transform(text, mode, culture);
            int offset = 0;
            var runs = new List<WriteRun>(selected.Length);
            foreach (var run in selected)
            {
                runs.Add(run.Field is null && run.Image is null && run.OpaqueXml is null ? run with { Text = changed.Substring(offset, run.Length) } : run);
                offset += run.Length;
            }
            return item.Paragraph with
            {
                Runs = Normalize(before.Concat(runs).Concat(Slice(item.Paragraph.Runs, item.End, item.Paragraph.Length)))
            };
        });
    }

    // Character by character, so every run keeps its length and formatting.
    private static string Transform(string text, WriteCaseMode mode, TextInfo culture)
    {
        var chars = text.ToCharArray();
        bool start = true;
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            chars[i] = mode switch
            {
                WriteCaseMode.Lower => culture.ToLower(c),
                WriteCaseMode.Upper => culture.ToUpper(c),
                WriteCaseMode.Toggle => char.IsUpper(c) ? culture.ToLower(c) : culture.ToUpper(c),
                WriteCaseMode.Title => start ? culture.ToUpper(c) : culture.ToLower(c),
                _ => start && char.IsLetter(c) ? culture.ToUpper(c) : culture.ToLower(c)
            };
            if (mode == WriteCaseMode.Title)
                start = !char.IsLetterOrDigit(c) && c != '\'';
            else if (char.IsLetter(c))
                start = false;
            else if (c is '.' or '!' or '?')
                start = true;
        }
        return new string(chars);
    }

    /// <summary>Removes the table that holds the caret and leaves the caret in the paragraph after it.</summary>
    public CommandResult DeleteTable()
    {
        if (!TableAtCaret(out var position, out var blocks, out _))
            return Invalid("Choose a table cell.");
        var replacement = blocks.RemoveAt(position.Block);
        int index = Math.Min(position.Block, replacement.Length - 1);
        if (replacement.Length == 0 || replacement[index] is not WriteParagraph)
        {
            replacement = replacement.Insert(position.Block, new WriteParagraph());
            index = position.Block;
        }
        var caret = new WritePosition(position.Section, index, 0, position.Story);
        return Commit(SetContainer(Document, position with { Row = -1 }, replacement), new(caret, caret));
    }

    /// <summary>Splits the table above the caret row, with an empty paragraph between the two tables.</summary>
    public CommandResult SplitTable()
    {
        if (!TableAtCaret(out var position, out var blocks, out var table))
            return Invalid("Choose a table cell.");
        if (position.Row == 0 || table.Rows.Any(row => row.Cells.Any(cell => cell.RowSpan != 1 || cell.ContinuesMerge)))
            return Invalid("Choose a row below the first row of a table without vertically merged cells.");
        var top = table with { Rows = [.. table.Rows.Take(position.Row)] };
        var bottom = table with { Rows = [.. table.Rows.Skip(position.Row).Select(row => row with { RepeatHeader = false })] };
        var replacement = blocks.RemoveAt(position.Block)
            .InsertRange(position.Block, ImmutableArray.Create<WriteBlock>(top, new WriteParagraph(), bottom));
        var caret = new WritePosition(position.Section, position.Block + 1, 0, position.Story);
        return Commit(SetContainer(Document, position with { Row = -1 }, replacement), new(caret, caret));
    }

    /// <summary>Gives every column the same width, keeping the table's total width; with <paramref name="width"/>, fits the table to it.</summary>
    public CommandResult DistributeColumns(double? width = null) => EditTable((table, position) =>
    {
        double total = width ?? table.ColumnWidths.Sum();
        if (!double.IsFinite(total) || total <= 0)
            return null;
        return (table with { ColumnWidths = [.. Enumerable.Repeat(total / table.ColumnWidths.Length, table.ColumnWidths.Length)] }, position);
    });

    /// <summary>Replaces the table with one paragraph per row, separating the cell texts with tabs.</summary>
    public CommandResult TableToText()
    {
        if (!TableAtCaret(out var position, out var blocks, out var table))
            return Invalid("Choose a table cell.");
        var paragraphs = table.Rows.Select(row =>
        {
            var runs = new List<WriteRun>();
            foreach (var (cell, index) in row.Cells.Where(cell => !cell.ContinuesMerge).Select((cell, index) => (cell, index)))
            {
                if (index > 0)
                    runs.Add(new WriteRun("\t"));
                var cellParagraphs = cell.Blocks.OfType<WriteParagraph>().ToArray();
                for (int i = 0; i < cellParagraphs.Length; i++)
                {
                    if (i > 0)
                        runs.Add(new WriteRun(" "));
                    runs.AddRange(cellParagraphs[i].Runs);
                }
            }
            return (WriteBlock)new WriteParagraph(Normalize(runs));
        }).ToImmutableArray();
        var caret = new WritePosition(position.Section, position.Block, 0, position.Story);
        return Commit(SetContainer(Document, position with { Row = -1 },
            blocks.RemoveAt(position.Block).InsertRange(position.Block, paragraphs)), new(caret, caret));
    }

    /// <summary>Changes the caret cell, or with <paramref name="wholeTable"/> every cell of the table.</summary>
    public CommandResult FormatCells(Func<WriteTableCell, WriteTableCell> format, bool wholeTable) => EditTable((table, position) =>
    {
        var rows = table.Rows.Select((row, r) => row with
        {
            Cells = [.. row.Cells.Select((cell, c) => wholeTable || r == position.Row && c == position.Cell ? format(cell) : cell)]
        }).ToImmutableArray();
        return (table with { Rows = rows }, position);
    });

    /// <summary>The table that holds the caret, or <see langword="null"/> outside a table.</summary>
    public WriteTable? CurrentTable => TableAtCaret(out _, out _, out var table) ? table : null;

    private bool TableAtCaret(out WritePosition position, out ImmutableArray<WriteBlock> blocks, out WriteTable table)
    {
        position = Selection.Caret;
        blocks = Blocks(Document, position.Section, position.Story);
        table = null!;
        if (position.Row < 0 || (uint)position.Block >= blocks.Length || blocks[position.Block] is not WriteTable found)
            return false;
        table = found;
        return true;
    }
}
