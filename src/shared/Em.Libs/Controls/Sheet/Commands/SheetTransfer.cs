// Ported from GridCraft (engine/src/clip_html.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
// Upstream sync: GridCraft crates/xlsx/src/csv.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926.
using System.IO;
using System.Net;
using System.Text;
using Em.Editing;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Formulas;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Commands;

/// <summary>Cells copied from a sheet, with text and HTML forms for other applications.</summary>
/// <param name="Rows">The number of copied rows.</param>
/// <param name="Columns">The number of copied columns.</param>
/// <param name="Cells">The cells in row-major order, with formula results as values.</param>
/// <param name="Text">Tab-separated text.</param>
/// <param name="Html">An HTML table.</param>
public sealed record SheetClipboard(int Rows, int Columns, IReadOnlyList<SheetCell> Cells, string Text, string Html);

public sealed partial class SheetSession
{
    /// <summary>Copies a range of the active sheet, up to one million cells.</summary>
    /// <param name="range">The range to copy.</param>
    /// <returns>The copied cells.</returns>
    public SheetClipboard Copy(CellRange range)
    {
        if (range.Count > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(range), "Clipboard ranges are limited to one million cells.");

        var cells = range.Cells().Select(address => ActiveSheet[address] with { Value = GetValue(ActiveSheet, address) }).ToArray();
        var text = new StringBuilder();
        var html = new StringBuilder("<table>");
        for (int row = 0; row < range.Height; row++)
        {
            if (row != 0)
                text.AppendLine();
            html.Append("<tr>");
            for (int column = 0; column < range.Width; column++)
            {
                string field = cells[row * range.Width + column].Value.ToString();
                if (column != 0)
                    text.Append('\t');
                AppendDelimited(text, field, '\t');
                html.Append("<td>").Append(WebUtility.HtmlEncode(field)).Append("</td>");
            }
            html.Append("</tr>");
        }
        return new(range.Height, range.Width, Array.AsReadOnly(cells), text.ToString(), html.Append("</table>").ToString());
    }

    /// <summary>Pastes tab-separated text starting at a cell; each field is parsed like typed input and target cells keep their styles.</summary>
    /// <param name="origin">The top-left target cell.</param>
    /// <param name="text">The tab-separated text.</param>
    /// <returns>The command result.</returns>
    public CommandResult Paste(CellAddress origin, string text) => Mode == EditorMode.Viewer ? ReadOnly :
        ChangeCells(ReadDelimited(new StringReader(text), '\t').SelectMany((row, rowIndex) =>
            row.Select((input, columnIndex) => (new CellAddress(origin.Row + rowIndex, origin.Column + columnIndex), input)))
            .Where(item => item.Item1.IsValid)
            .Select(item => (item.Item1, SheetCell.FromInput(item.input, ActiveSheet[item.Item1].StyleIndex))));

    /// <summary>Pastes copied cells starting at a cell; target cells keep their styles.</summary>
    /// <param name="origin">The top-left target cell.</param>
    /// <param name="clipboard">The copied cells.</param>
    /// <returns>The command result.</returns>
    public CommandResult Paste(CellAddress origin, SheetClipboard clipboard)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!origin.IsValid || clipboard.Rows <= 0 || clipboard.Columns <= 0 ||
            (long)clipboard.Rows * clipboard.Columns != clipboard.Cells.Count)
            return Invalid("Invalid clipboard dimensions.");

        return ChangeCells(clipboard.Cells.Select((cell, i) =>
                (new CellAddress(origin.Row + i / clipboard.Columns, origin.Column + i % clipboard.Columns), cell))
            .Where(item => item.Item1.IsValid)
            .Select(item => (item.Item1, item.cell with { StyleIndex = ActiveSheet[item.Item1].StyleIndex })));
    }

    /// <summary>
    /// Whether moving or copying <paramref name="source"/> to <paramref name="to"/> would replace cells that hold data,
    /// other than the moved cells themselves.
    /// </summary>
    public bool MoveWouldReplace(CellRange source, CellAddress to, bool copy = false)
    {
        var target = MoveTarget(source, to);
        return target is { } range && ActiveSheet.Cells.Any(pair => range.Contains(pair.Key) &&
            (copy || !source.Contains(pair.Key)) && (pair.Value.Formula is not null || pair.Value.Value.Kind != CellValueKind.Blank));
    }

    /// <summary>
    /// Drag and drop: moves <paramref name="source"/> so its top-left cell lands on <paramref name="to"/>, as Cut and Paste
    /// would. With <paramref name="copy"/> the source stays and formulas in the copy adjust their relative references.
    /// </summary>
    public CommandResult Move(CellRange source, CellAddress to, bool copy = false)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (source.Count > 1_000_000)
            return Invalid("Moving is limited to one million cells per command.");
        if (MoveTarget(source, to) is not { } target)
            return Invalid("The cells cannot be moved outside the sheet.");
        if (to == source.Start)
            return CommandResult.Success;

        var sheet = ActiveSheet;
        int rows = to.Row - source.Start.Row, columns = to.Column - source.Start.Column;
        var changes = new Dictionary<CellAddress, SheetCell>();
        var moved = sheet.Cells.Where(pair => source.Contains(pair.Key)).ToArray();
        if (!copy)
            foreach (var (address, _) in moved)
                changes[address] = default;
        foreach (var address in sheet.Cells.Keys.Where(target.Contains))
            changes[address] = default;
        foreach (var (address, cell) in moved)
            changes[new(address.Row + rows, address.Column + columns)] = copy && cell.Formula is { } formula
                ? cell with { Formula = ReferenceAdjustment.Copy(formula, rows, columns), Value = default }
                : cell;
        return ChangeCells(changes.Select(pair => (pair.Key, pair.Value)));
    }

    private static CellRange? MoveTarget(CellRange source, CellAddress to)
    {
        var end = new CellAddress(to.Row + source.Height - 1, to.Column + source.Width - 1);
        return to.IsValid && end.IsValid ? new CellRange(to, end) : null;
    }

    internal static void AppendDelimited(StringBuilder text, string value, char delimiter)
    {
        if (value.IndexOfAny([delimiter, '"', '\r', '\n']) < 0)
            text.Append(value);
        else
            text.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
    }

    internal static IEnumerable<string[]> ReadDelimited(TextReader reader, char delimiter, CancellationToken cancellation = default)
    {
        var field = new StringBuilder();
        var row = new List<string>();
        bool quoted = false, pending = false, overflow = false;
        void EndField()
        {
            if (row.Count < CellAddress.MaxColumns)
                row.Add(field.ToString());
            overflow = row.Count >= CellAddress.MaxColumns;
            field.Clear();
        }
        while (reader.Read() is int value && value >= 0)
        {
            char current = (char)value;
            pending = true;
            if (quoted)
            {
                if (current != '"')
                {
                    if (!overflow)
                        field.Append(current);
                }
                else if (reader.Peek() == '"')
                {
                    reader.Read();
                    if (!overflow) field.Append('"');
                }
                else
                    quoted = false;
            }
            else if (current == '"' && field.Length == 0)
                quoted = true;
            else if (current == delimiter)
            {
                EndField();
            }
            else if (current is '\n' or '\r')
            {
                cancellation.ThrowIfCancellationRequested();
                if (current == '\r' && reader.Peek() == '\n')
                    reader.Read();
                EndField();
                yield return row.ToArray();
                overflow = false;
                row.Clear();
                field.Clear();
                pending = false;
            }
            else if (!overflow)
                field.Append(current);
        }
        if (pending)
        {
            EndField();
            yield return row.ToArray();
        }
    }
}
