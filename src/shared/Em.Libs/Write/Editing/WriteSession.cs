// Ported from WordCraft (doc/src/edit.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.Globalization;
using Em.Editing;
using Em.Write.Model;

namespace Em.Write.Editing;

public sealed partial class WriteSession
{
    private readonly UndoHistory history = new();
    private EditorMode mode;
    private WriteCharacterStyle? typingStyle;
    private string? typingStyleId;

    /// <summary>Starts an editing session.</summary>
    /// <param name="document">The document to edit, or null for an empty document.</param>
    public WriteSession(WriteDocument? document = null)
    {
        Document = document ?? WriteDocument.Create();
        var start = Paragraphs().FirstOrDefault();
        Selection = new(start, start);
    }

    /// <summary>Gets the current document. Each change replaces it with a new immutable document.</summary>
    public WriteDocument Document { get; private set; }
    /// <summary>Gets the selection.</summary>
    public WriteSelection Selection { get; private set; }
    /// <summary>Gets whether a change can be undone; always false in viewer mode.</summary>
    public bool CanUndo => Mode == EditorMode.Full && history.CanUndo;
    /// <summary>Gets whether an undone change can be redone; always false in viewer mode.</summary>
    public bool CanRedo => Mode == EditorMode.Full && history.CanRedo;

    /// <summary>Gets or sets whether the session allows changes. Commands that change the document are rejected in viewer mode.</summary>
    public EditorMode Mode
    {
        get => mode;
        set
        {
            if (mode == value)
                return;
            mode = value;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Occurs after a command changes the document, including undo and redo.</summary>
    public event EventHandler? DocumentChanged;
    /// <summary>Occurs when <see cref="Mode"/> changes.</summary>
    public event EventHandler? ModeChanged;
    /// <summary>Occurs when the selection changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Selects a range; both ends must be valid and in the same story, and header and footer selections in the same section.</summary>
    /// <param name="selection">The selection.</param>
    /// <returns>False when the selection is not valid.</returns>
    public bool Select(WriteSelection selection)
    {
        if (!Valid(selection.Anchor) || !Valid(selection.Caret) ||
            selection.Anchor.Story != selection.Caret.Story ||
            selection.Anchor.Story != WriteStory.Body && selection.Anchor.Section != selection.Caret.Section)
            return false;

        typingStyle = null;
        typingStyleId = null;
        if (selection != Selection)
        {
            Selection = selection;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        return true;
    }

    /// <summary>Selects the whole body.</summary>
    /// <returns>False when the body has no paragraph.</returns>
    public bool SelectAll()
    {
        var paragraphs = Paragraphs().ToArray();
        if (paragraphs.Length == 0)
            return false;
        var end = paragraphs[^1];
        return Select(new(paragraphs[0], end with { Offset = GetParagraph(end)!.Length }));
    }

    /// <summary>Returns the paragraph at a position.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The paragraph, or null when the position does not point to one.</returns>
    public WriteParagraph? GetParagraph(WritePosition position) => GetParagraph(Document, position);

    /// <summary>Enumerates the paragraph positions of a story, including paragraphs inside table cells.</summary>
    /// <param name="story">The story.</param>
    /// <returns>The start position of each paragraph.</returns>
    public IEnumerable<WritePosition> Paragraphs(WriteStory story = WriteStory.Body)
    {
        for (int section = 0; section < Document.Sections.Length; section++)
        {
            var blocks = Blocks(Document, section, story);
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] is WriteParagraph)
                    yield return new(section, i, 0, story);
                else if (blocks[i] is WriteTable table)
                    for (int row = 0; row < table.Rows.Length; row++)
                    for (int cell = 0; cell < table.Rows[row].Cells.Length; cell++)
                    for (int block = 0; block < table.Rows[row].Cells[cell].Blocks.Length; block++)
                        if (table.Rows[row].Cells[cell].Blocks[block] is WriteParagraph)
                            yield return new(section, i, 0, story, row, cell, block);
            }
        }
    }

    /// <summary>Replaces the selection with text. Line breaks start new paragraphs that keep the current paragraph formatting.</summary>
    /// <param name="text">The text to insert.</param>
    /// <returns>The command result.</returns>
    public CommandResult InsertText(string text)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var start = Selection.Start;
        var paragraph = GetParagraph(start);
        if (paragraph is null)
            return Invalid("Choose a paragraph before typing.");
        var style = typingStyle ?? StyleAt(paragraph, start.Offset);
        var styleId = typingStyleId ?? RunAt(paragraph, start.Offset)?.StyleId;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return ReplaceSelection([.. lines.Select(line => new WriteParagraph(line.Length > 0 ? [new(line, style, styleId)] : [],
            paragraph.StyleId, paragraph.Format, paragraph.NumberingId, style))]);
    }

    /// <summary>Replaces the selection with a run, such as a field or an image.</summary>
    /// <param name="run">The run to insert.</param>
    /// <returns>The command result.</returns>
    public CommandResult InsertRun(WriteRun run)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var current = GetParagraph(Selection.Start);
        return current is null ? Invalid("Choose a paragraph before inserting content.") :
            ReplaceSelection([current with { Runs = [run] }]);
    }

    /// <summary>Deletes the selection, or one position before or after the caret when nothing is selected.</summary>
    /// <param name="backward">True to delete before the caret.</param>
    /// <returns>The command result.</returns>
    public CommandResult Delete(bool backward)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!Selection.IsEmpty)
            return ReplaceSelection([]);
        var start = Selection.Caret;
        var paragraph = GetParagraph(start);
        if (paragraph is null)
            return Invalid("Choose a paragraph before deleting.");
        string text = paragraph.Text;
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        int offset = backward ? boundaries.LastOrDefault(item => item < start.Offset, -1) :
            boundaries.FirstOrDefault(item => item > start.Offset, -1);
        if (offset >= 0)
            return ReplaceSelection([], new(start, start with { Offset = offset }));
        var positions = Paragraphs(start.Story).Where(position => SameContainer(start, position)).ToArray();
        int index = Array.FindIndex(positions, position => SameParagraph(start, position));
        int other = index + (backward ? -1 : 1);
        if ((uint)other >= positions.Length)
            return CommandResult.Success;
        var target = positions[other];
        if (Math.Abs(Index(start) - Index(target)) != 1)
            return Invalid("The selection crosses a structural block.");
        if (backward)
            target = target with { Offset = GetParagraph(target)!.Length };
        return ReplaceSelection([], new(start, target));
    }

    /// <summary>Copies the selection.</summary>
    /// <returns>The copied paragraphs and their plain text.</returns>
    public WriteClipboard Copy()
    {
        var segments = SelectionSegments(Selection).Select(item => item.Paragraph with
        {
            Runs = [.. Slice(item.Paragraph.Runs, item.Start, item.End)]
        }).ToImmutableArray();
        return new(segments, string.Join("\n", segments.Select(item => item.Text)));
    }

    /// <summary>Replaces the selection with copied paragraphs.</summary>
    /// <param name="clipboard">The copied content.</param>
    /// <returns>The command result.</returns>
    public CommandResult Paste(WriteClipboard clipboard) => ReplaceSelection(clipboard.Paragraphs);
    /// <summary>Deletes the selection; copy it first to move it to the clipboard.</summary>
    /// <returns>The command result.</returns>
    public CommandResult Cut() => ReplaceSelection([]);

    /// <summary>Changes the character formatting of the selection, or of the next typed text when nothing is selected.</summary>
    /// <param name="transform">Returns the new formatting for the current formatting.</param>
    /// <returns>The command result.</returns>
    public CommandResult FormatCharacters(Func<WriteCharacterStyle, WriteCharacterStyle> transform)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;

        if (Selection.IsEmpty)
        {
            var position = Selection.Caret;
            var paragraph = GetParagraph(position);
            if (paragraph is null)
                return Invalid("Choose a paragraph before formatting.");
            typingStyle = transform(typingStyle ?? Document.Resolve(paragraph,
                RunAt(paragraph, position.Offset) ?? new WriteRun("", paragraph.Mark)));
            return CommandResult.Success;
        }

        return EditParagraphs((item, doc) => item.Paragraph with
        {
            Runs = Normalize(Slice(item.Paragraph.Runs, 0, item.Start).Concat(
                Slice(item.Paragraph.Runs, item.Start, item.End).Select(run => run with
                {
                    Format = transform(doc.Resolve(item.Paragraph, run))
                })).Concat(Slice(item.Paragraph.Runs, item.End, item.Paragraph.Length)))
        });
    }

    /// <summary>Changes the formatting of the selected paragraphs.</summary>
    /// <param name="transform">Returns the new formatting for the current formatting.</param>
    /// <returns>The command result.</returns>
    public CommandResult FormatParagraphs(Func<WriteParagraphStyle, WriteParagraphStyle> transform) =>
        EditParagraphs((item, doc) => item.Paragraph with { Format = transform(doc.Resolve(item.Paragraph).Paragraph) });

    /// <summary>Applies a named style to the selected paragraphs or characters and clears their direct formatting.</summary>
    /// <param name="id">The style identifier, or null for the default style.</param>
    /// <param name="kind">Whether a paragraph or a character style is applied.</param>
    /// <returns>The command result.</returns>
    public CommandResult ApplyStyle(string? id, WriteStyleKind kind)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (id is not null && (!Document.Styles.TryGetValue(id, out var style) || style.Kind != kind))
            return Invalid("Unknown style.");
        if (kind == WriteStyleKind.Paragraph)
            return EditParagraphs((item, _) => item.Paragraph with { StyleId = id ?? "Normal", Format = null });
        if (Selection.IsEmpty)
        {
            typingStyleId = id;
            typingStyle = new();
            return CommandResult.Success;
        }
        return EditParagraphs((item, _) => item.Paragraph with
        {
            Runs = Normalize(Slice(item.Paragraph.Runs, 0, item.Start).Concat(
                Slice(item.Paragraph.Runs, item.Start, item.End).Select(run => run with { StyleId = id, Format = null }))
                .Concat(Slice(item.Paragraph.Runs, item.End, item.Paragraph.Length)))
        });
    }

    /// <summary>Turns the selected paragraphs into a list, or removes them from lists.</summary>
    /// <param name="kind">The list kind, or null to remove the list.</param>
    /// <returns>The command result.</returns>
    public CommandResult SetList(WriteListKind? kind)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var document = Document;
        int? id = null;
        if (kind is { } requested)
        {
            id = document.Numbering.Values.Where(item => item.Kind == requested && item.Label is null && item.Start == 1)
                .Select(item => (int?)item.Id).FirstOrDefault();
            if (id is null)
            {
                id = checked(document.Numbering.Keys.DefaultIfEmpty(0).Max() + 1);
                document = document with { Numbering = document.Numbering.Add(id.Value, new(id.Value, requested)) };
            }
        }
        return EditParagraphs((item, _) => item.Paragraph with { NumberingId = id }, document);
    }

    /// <summary>Replaces every occurrence of a text in the body, ignoring case.</summary>
    /// <param name="search">The text to find.</param>
    /// <param name="replacement">The replacement text.</param>
    /// <returns>The command result.</returns>
    public CommandResult ReplaceAll(string search, string replacement)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (search.Length == 0)
            return Invalid("Enter text to find.");

        var doc = Document;
        foreach (var position in Paragraphs())
        {
            var paragraph = GetParagraph(position)!;
            var hits = new List<int>();
            for (int offset = 0; offset <= paragraph.Length - search.Length;)
            {
                int found = paragraph.Text.IndexOf(search, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                hits.Add(found);
                offset = found + search.Length;
            }
            if (hits.Count == 0)
                continue;
            var runs = paragraph.Runs;
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                int start = hits[i];
                int length = runs.Sum(run => run.Length);
                runs = Normalize(Slice(runs, 0, start).Append(new(replacement, StyleAt(paragraph, start)))
                    .Concat(Slice(runs, start + search.Length, length)));
            }
            doc = SetParagraph(doc, position, paragraph with { Runs = runs });
        }

        var selected = Selection.Caret;
        int limit = GetParagraph(doc, selected)?.Length ?? 0;
        selected = selected with { Offset = Math.Min(selected.Offset, limit) };
        return Commit(doc, new(selected, selected));
    }

    /// <summary>Reverts the last change.</summary>
    /// <returns>The command result.</returns>
    public CommandResult Undo()
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        return history.Undo() ? CommandResult.Success : Invalid("There is no change to undo.");
    }

    /// <summary>Reapplies the last undone change.</summary>
    /// <returns>The command result.</returns>
    public CommandResult Redo()
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        return history.Redo() ? CommandResult.Success : Invalid("There is no change to redo.");
    }

    private CommandResult ReplaceSelection(ImmutableArray<WriteParagraph> inserted, WriteSelection? selection = null)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var range = selection ?? Selection;
        var start = range.Start;
        var end = range.End;
        if (!SameContainer(start, end))
            return Invalid("The edit crosses a table cell or section boundary.");
        if (GetParagraph(start) is not { } first || GetParagraph(end) is not { } last)
            return Invalid("Choose a paragraph before editing.");
        if (range.IsEmpty && inserted.Length == 0)
            return CommandResult.Success;

        var before = Slice(first.Runs, 0, start.Offset);
        var after = Slice(last.Runs, end.Offset, last.Length);
        int firstIndex = Index(start);
        int lastIndex = Index(end);
        var changed = inserted.IsDefaultOrEmpty ? ImmutableArray.Create(first with { Runs = [] }) : inserted;
        int offset = changed[^1].Length + (changed.Length == 1 ? start.Offset : 0);
        changed = changed.SetItem(0, first with { Runs = Normalize(before.Concat(changed[0].Runs)) });
        changed = changed.SetItem(changed.Length - 1, changed[^1] with { Runs = Normalize(changed[^1].Runs.Concat(after)) });
        var caret = At(start, firstIndex + changed.Length - 1) with { Offset = offset };
        return Commit(SetContainer(Document, start, Container(Document, start).RemoveRange(firstIndex, lastIndex - firstIndex + 1)
            .InsertRange(firstIndex, changed.Cast<WriteBlock>())), new(caret, caret));
    }

    private CommandResult EditParagraphs(Func<Segment, WriteDocument, WriteParagraph> transform, WriteDocument? document = null)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var result = document ?? Document;
        foreach (var item in SelectionSegments(Selection))
            result = SetParagraph(result, item.Position, transform(item, result));
        return Commit(result, Selection);
    }

    private IEnumerable<Segment> SelectionSegments(WriteSelection selection)
    {
        foreach (var position in Paragraphs(selection.Start.Story))
        {
            var item = GetParagraph(position)!;
            if (position.CompareTo(selection.End) > 0 ||
                (position with { Offset = item.Length }).CompareTo(selection.Start) < 0)
                continue;
            yield return new(position, item, SameParagraph(position, selection.Start) ? selection.Start.Offset : 0,
                SameParagraph(position, selection.End) ? selection.End.Offset : item.Length);
        }
    }

    private CommandResult Commit(WriteDocument document, WriteSelection selection)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (ReferenceEquals(Document, document))
            return CommandResult.Success;
        var previous = Document;
        var previousSelection = Selection;
        history.Record(() => Apply(previous, previousSelection), () => Apply(document, selection));
        Apply(document, selection);
        return CommandResult.Success;
    }

    private void Apply(WriteDocument document, WriteSelection selection)
    {
        Document = document;
        Selection = selection;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool Valid(WritePosition position)
    {
        if (GetParagraph(position) is not { } paragraph || position.Offset < 0 || position.Offset > paragraph.Length)
            return false;
        return position.Offset == paragraph.Length || position.Offset == 0 || !char.IsLowSurrogate(paragraph.Text[position.Offset]);
    }

    private static WriteParagraph? GetParagraph(WriteDocument document, WritePosition position)
    {
        var container = Container(document, position);
        int index = Index(position);
        return (uint)index < container.Length ? container[index] as WriteParagraph : null;
    }

    private static ImmutableArray<WriteBlock> Blocks(WriteDocument document, int section, WriteStory story)
    {
        if ((uint)section >= document.Sections.Length)
            return [];
        var item = document.Sections[section];
        return story == WriteStory.Body ? item.Blocks : item.Stories.GetValueOrDefault(story, []);
    }

    private static ImmutableArray<WriteBlock> Container(WriteDocument document, WritePosition position)
    {
        var blocks = Blocks(document, position.Section, position.Story);
        if (position.Row < 0)
            return blocks;
        if ((uint)position.Block >= blocks.Length || blocks[position.Block] is not WriteTable table ||
            (uint)position.Row >= table.Rows.Length || (uint)position.Cell >= table.Rows[position.Row].Cells.Length)
            return [];
        return table.Rows[position.Row].Cells[position.Cell].Blocks;
    }

    private static WriteDocument SetParagraph(WriteDocument doc, WritePosition position, WriteParagraph value) =>
        SetContainer(doc, position, Container(doc, position).SetItem(Index(position), value));

    private static WriteDocument SetContainer(WriteDocument document, WritePosition position, ImmutableArray<WriteBlock> replacement)
    {
        var section = document.Sections[position.Section];
        if (position.Row >= 0)
        {
            var story = Blocks(document, position.Section, position.Story);
            var table = (WriteTable)story[position.Block];
            var row = table.Rows[position.Row];
            var cell = row.Cells[position.Cell];
            row = row with { Cells = row.Cells.SetItem(position.Cell, cell with { Blocks = replacement }) };
            replacement = story.SetItem(position.Block, table with { Rows = table.Rows.SetItem(position.Row, row) });
        }
        section = position.Story == WriteStory.Body ? section with { Blocks = replacement } :
            section with { Stories = section.Stories.SetItem(position.Story, replacement) };
        return document with { Sections = document.Sections.SetItem(position.Section, section) };
    }

    private static int Index(WritePosition position) => position.Row < 0 ? position.Block : position.CellBlock;
    private static WritePosition At(WritePosition position, int index) => position.Row < 0 ?
        position with { Block = index } : position with { CellBlock = index };

    private static bool SameParagraph(WritePosition a, WritePosition b) => (a with { Offset = 0 }) == (b with { Offset = 0 });

    private static bool SameContainer(WritePosition a, WritePosition b) => a.Section == b.Section && a.Story == b.Story &&
        (a.Row < 0 && b.Row < 0 || a.Block == b.Block && a.Row == b.Row && a.Cell == b.Cell);

    private static WriteCharacterStyle? StyleAt(WriteParagraph paragraph, int offset) => RunAt(paragraph, offset)?.Format ?? paragraph.Mark;

    private static WriteRun? RunAt(WriteParagraph paragraph, int offset)
    {
        foreach (var run in paragraph.Runs)
        {
            if (offset <= run.Length)
                return run;
            offset -= run.Length;
        }
        return null;
    }

    private static IEnumerable<WriteRun> Slice(ImmutableArray<WriteRun> runs, int start, int end)
    {
        if (start >= end)
            yield break;
        int offset = 0;
        foreach (var run in runs)
        {
            int last = offset + run.Length;
            if (last > start && offset < end)
                yield return run.Field is not null || run.Image is not null || run.OpaqueXml is not null ? run :
                    run with { Text = run.Text[Math.Max(0, start - offset)..Math.Min(run.Length, end - offset)] };
            if (last >= end)
                yield break;
            offset = last;
        }
    }

    private static ImmutableArray<WriteRun> Normalize(IEnumerable<WriteRun> runs)
    {
        var result = ImmutableArray.CreateBuilder<WriteRun>();
        foreach (var run in runs)
        {
            if (run.Length == 0)
                continue;
            if (result.Count > 0 && IsText(result[^1]) && IsText(run) && result[^1].Format == run.Format && result[^1].StyleId == run.StyleId)
                result[^1] = run with { Text = result[^1].Text + run.Text };
            else
                result.Add(run);
        }
        return result.ToImmutable();

        static bool IsText(WriteRun item) => item.Field is null && item.Image is null && item.OpaqueXml is null;
    }

    private static CommandResult Invalid(string reason) => CommandResult.Rejected(reason);
    private static CommandResult ReadOnly => Invalid("The document is read-only.");
    private sealed record Segment(WritePosition Position, WriteParagraph Paragraph, int Start, int End);
}

/// <summary>Content copied from a document.</summary>
/// <param name="Paragraphs">The copied paragraph parts with their formatting.</param>
/// <param name="Text">The plain text, one line per paragraph.</param>
public sealed record WriteClipboard(ImmutableArray<WriteParagraph> Paragraphs, string Text);
