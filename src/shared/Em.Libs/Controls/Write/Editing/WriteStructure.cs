// Ported from WordCraft (doc/src/table.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using Em.Editing;
using Em.Controls.Write.Model;

namespace Em.Controls.Write.Editing;

public sealed partial class WriteSession
{
    /// <summary>Inserts a table with empty cells after the current body paragraph.</summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="columns">The number of columns.</param>
    /// <returns>The command result.</returns>
    public CommandResult InsertTable(int rows, int columns)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (rows is < 1 or > 32767 || columns is < 1 or > 63 || (long)rows * columns > 100_000)
            return Invalid("Invalid table size.");
        var pos = Selection.Caret;
        if (pos.Row >= 0 || pos.Story != WriteStory.Body || GetParagraph(pos) is not { } paragraph)
            return Invalid("Choose a body paragraph outside a table.");

        var width = Document.Sections[pos.Section].Page;
        var table = new WriteTable([.. Enumerable.Repeat((width.Width - width.Left - width.Right) / columns, columns)],
            [.. Enumerable.Range(0, rows).Select(_ => EmptyRow(columns))]);
        var first = paragraph with { Runs = Normalize(Slice(paragraph.Runs, 0, pos.Offset)) };
        var last = paragraph with { Runs = Normalize(Slice(paragraph.Runs, pos.Offset, paragraph.Length)) };
        var replacement = Container(Document, pos).RemoveAt(pos.Block).InsertRange(pos.Block, ImmutableArray.Create<WriteBlock>(first, table, last));
        var next = new WritePosition(pos.Section, pos.Block + 1, 0, pos.Story, 0, 0);
        return Commit(SetContainer(Document, pos, replacement), new(next, next));
    }

    /// <summary>Inserts or deletes a row of the table at the caret. Tables with merged rows are rejected.</summary>
    /// <param name="row">The zero-based row index; when inserting, the row count appends a row.</param>
    /// <param name="insert">True to insert, false to delete.</param>
    /// <returns>The command result.</returns>
    public CommandResult ChangeRows(int row, bool insert)
    {
        return EditTable((table, position) =>
        {
            if (row < 0 || row > table.Rows.Length || !insert && (row == table.Rows.Length || table.Rows.Length == 1) ||
                table.Rows.Any(item => item.Cells.Any(cell => cell.RowSpan != 1 || cell.ContinuesMerge)))
                return null;
            var rows = insert ? table.Rows.Insert(row, EmptyRow(table.ColumnWidths.Length)) : table.Rows.RemoveAt(row);
            return (table with { Rows = rows }, position with { Row = Math.Min(row, rows.Length - 1), Cell = 0, CellBlock = 0, Offset = 0 });
        });
    }

    /// <summary>Inserts or deletes a column of the table at the caret. Tables with merged columns are rejected.</summary>
    /// <param name="column">The zero-based column index; when inserting, the column count appends a column.</param>
    /// <param name="insert">True to insert, false to delete.</param>
    /// <returns>The command result.</returns>
    public CommandResult ChangeColumns(int column, bool insert)
    {
        return EditTable((table, position) =>
        {
            int count = table.ColumnWidths.Length;
            if (column < 0 || column > count || !insert && (column == count || count == 1) || insert && count >= 63 ||
                table.Rows.Any(row => row.Cells.Length != count || row.Cells.Any(cell => cell.ColumnSpan != 1)))
                return null;
            var widths = insert ? table.ColumnWidths.Insert(column, 96) : table.ColumnWidths.RemoveAt(column);
            var rows = table.Rows.Select(row => row with
            {
                Cells = insert ? row.Cells.Insert(column, new WriteTableCell()) : row.Cells.RemoveAt(column)
            }).ToImmutableArray();
            return (table with { Rows = rows, ColumnWidths = widths },
                position with { Cell = Math.Min(column, widths.Length - 1), CellBlock = 0, Offset = 0 });
        });
    }

    /// <summary>Sets the width of a table column.</summary>
    /// <param name="column">The zero-based column index.</param>
    /// <param name="width">The new width in device-independent pixels.</param>
    /// <returns>The command result.</returns>
    public CommandResult ResizeColumn(int column, double width) => EditTable((table, position) =>
        (uint)column < table.ColumnWidths.Length && double.IsFinite(width) && width > 0 ?
            (table with { ColumnWidths = table.ColumnWidths.SetItem(column, width) }, position) : null);

    /// <summary>Sets whether the first row repeats on every page.</summary>
    /// <param name="repeat">True to repeat the header row.</param>
    /// <returns>The command result.</returns>
    public CommandResult SetHeaderRepeat(bool repeat) => EditTable((table, position) =>
        (table with { Rows = table.Rows.SetItem(0, table.Rows[0] with { RepeatHeader = repeat }) }, position));

    /// <summary>Sets the fill and border colors of the current cell.</summary>
    /// <param name="fill">The fill color as 0xRRGGBB, or null.</param>
    /// <param name="border">The border color as 0xRRGGBB, or null.</param>
    /// <returns>The command result.</returns>
    public CommandResult FormatCell(uint? fill, uint? border) => EditTable((table, position) =>
    {
        var row = table.Rows[position.Row];
        var cell = row.Cells[position.Cell] with { Fill = fill, Border = border };
        return (table with { Rows = table.Rows.SetItem(position.Row, row with { Cells = row.Cells.SetItem(position.Cell, cell) }) }, position);
    });

    /// <summary>Merges the current cell with the following cells of its row.</summary>
    /// <param name="count">The number of cells to merge, including the current cell.</param>
    /// <returns>The command result.</returns>
    public CommandResult MergeCells(int count) => EditTable((table, position) =>
    {
        var row = table.Rows[position.Row];
        if (count < 2 || count > row.Cells.Length - position.Cell)
            return null;
        var group = row.Cells.Skip(position.Cell).Take(count).ToArray();
        if (group.Any(cell => cell.ContinuesMerge || cell.RowSpan > 1))
            return null;
        var merged = group[0] with { ColumnSpan = group.Sum(cell => cell.ColumnSpan),
            Blocks = group.SelectMany(cell => cell.Blocks).ToImmutableArray() };
        var cells = row.Cells.RemoveRange(position.Cell, count).Insert(position.Cell, merged);
        return (table with { Rows = table.Rows.SetItem(position.Row, row with { Cells = cells }) }, position);
    });

    /// <summary>Splits a horizontally merged cell back into single cells.</summary>
    /// <returns>The command result.</returns>
    public CommandResult SplitCell() => EditTable((table, position) =>
    {
        var row = table.Rows[position.Row];
        var cell = row.Cells[position.Cell];
        if (cell.ColumnSpan <= 1 || cell.RowSpan > 1 || cell.ContinuesMerge)
            return null;
        var cells = row.Cells.SetItem(position.Cell, cell with { ColumnSpan = 1 })
            .InsertRange(position.Cell + 1, Enumerable.Range(1, cell.ColumnSpan - 1).Select(_ => new WriteTableCell()));
        return (table with { Rows = table.Rows.SetItem(position.Row, row with { Cells = cells }) }, position);
    });

    /// <summary>Changes the page settings of the current section.</summary>
    /// <param name="settings">The new page settings.</param>
    /// <returns>The command result.</returns>
    public CommandResult SetPage(WritePageSettings settings)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (new[] { settings.Width, settings.Height, settings.Left, settings.Right, settings.Top, settings.Bottom,
            settings.HeaderDistance, settings.FooterDistance }.Any(value => !double.IsFinite(value) || value < 0) ||
            settings.Width - settings.Left - settings.Right < 24 || settings.Height - settings.Top - settings.Bottom < 24)
            return Invalid("Margins leave too little space.");
        int index = Selection.Caret.Section;
        return Commit(Document with { Sections = Document.Sections.SetItem(index, Document.Sections[index] with { Page = settings }) }, Selection);
    }

    /// <summary>Inserts a page break or a section break after the current body paragraph.</summary>
    /// <param name="section">True for a section break, false for a page break.</param>
    /// <returns>The command result.</returns>
    public CommandResult InsertBreak(bool section)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var position = Selection.Caret;
        if (position.Row >= 0 || position.Story != WriteStory.Body || GetParagraph(position) is not { } paragraph)
            return Invalid("Choose a body paragraph.");

        var current = Document.Sections[position.Section];
        var first = paragraph with { Runs = Normalize(Slice(paragraph.Runs, 0, position.Offset)) };
        var rest = paragraph with { Runs = Normalize(Slice(paragraph.Runs, position.Offset, paragraph.Length)) };
        if (!section)
        {
            var blocks = current.Blocks.SetItem(position.Block, first).InsertRange(position.Block + 1, ImmutableArray.Create<WriteBlock>(new WritePageBreak(), rest));
            var next = position with { Block = position.Block + 2, Offset = 0 };
            return Commit(SetContainer(Document, position, blocks), new(next, next));
        }
        var earlier = current with { Blocks = [.. current.Blocks.Take(position.Block), first] };
        var later = current with { Blocks = [rest, .. current.Blocks.Skip(position.Block + 1)] };
        var destination = new WritePosition(position.Section + 1, 0, 0);
        return Commit(Document with { Sections = Document.Sections.SetItem(position.Section, earlier).Insert(position.Section + 1, later) },
            new(destination, destination));
    }

    /// <summary>Moves the caret into a story of a section, creating an empty header or footer when needed.</summary>
    /// <param name="story">The story to edit.</param>
    /// <param name="section">The zero-based section index.</param>
    /// <returns>The command result.</returns>
    public CommandResult EditStory(WriteStory story, int section)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if ((uint)section >= Document.Sections.Length)
            return Invalid("Invalid section.");
        var start = Paragraphs(story).FirstOrDefault(position => position.Section == section);
        if (GetParagraph(start) is not null && start.Story == story && start.Section == section)
            return Select(new(start, start)) ? CommandResult.Success : Invalid("Could not select story.");
        if (story == WriteStory.Body)
            return Invalid("No body paragraph is available.");
        start = new(section, 0, 0, story);
        return Commit(SetContainer(Document, start, [new WriteParagraph()]), new(start, start));
    }

    /// <summary>Resizes the selected image.</summary>
    /// <param name="width">The new width in device-independent pixels.</param>
    /// <param name="height">The new height in device-independent pixels.</param>
    /// <returns>The command result.</returns>
    public CommandResult ResizeImage(double width, double height)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return Invalid("Invalid image size.");
        var position = Selection.Start;
        var paragraph = GetParagraph(position);
        if (paragraph is null)
            return Invalid("Choose an image.");
        int offset = 0;
        for (int i = 0; i < paragraph.Runs.Length; i++)
        {
            var run = paragraph.Runs[i];
            if (offset == position.Offset && run.Image is { } image)
                return Commit(SetParagraph(Document, position, paragraph with
                {
                    Runs = paragraph.Runs.SetItem(i, run with { Image = image with { Width = width, Height = height } })
                }), Selection);
            offset += run.Length;
        }
        return Invalid("Choose an image.");
    }

    private CommandResult EditTable(Func<WriteTable, WritePosition, (WriteTable Table, WritePosition Caret)?> edit)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var position = Selection.Caret;
        var blocks = Blocks(Document, position.Section, position.Story);
        if (position.Row < 0 || (uint)position.Block >= blocks.Length || blocks[position.Block] is not WriteTable table)
            return Invalid("Choose a table cell.");
        if (edit(table, position) is not { } change)
            return Invalid("The requested change is invalid for this table.");
        return Commit(SetContainer(Document, position with { Row = -1 }, blocks.SetItem(position.Block, change.Table)),
            new(change.Caret, change.Caret));
    }

    private static WriteTableRow EmptyRow(int columns) => new(
        [.. Enumerable.Range(0, columns).Select(_ => new WriteTableCell())]);
}
