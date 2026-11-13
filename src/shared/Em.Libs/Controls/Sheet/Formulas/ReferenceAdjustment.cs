// Ported from GridCraft (formula/src/adjust.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Text.RegularExpressions;
using Em.Controls.Sheet.Cells;

namespace Em.Controls.Sheet.Formulas;

internal static partial class ReferenceAdjustment
{
    internal static string Copy(string formula, int rows, int columns) => Transform(formula, (_, address, rowFixed, columnFixed) =>
        new(address.Row + (rowFixed ? 0 : rows), address.Column + (columnFixed ? 0 : columns)));

    internal static string Shift(string formula, string context, string target, bool rows, int start, int count, bool insert) =>
        Transform(formula, (sheet, address, _, _) =>
        {
            if (!(sheet ?? context).Equals(target, StringComparison.OrdinalIgnoreCase))
                return address;

            int position = rows ? address.Row : address.Column;
            if (position < start)
                return address;
            if (!insert && position < start + count)
                return new(-1, -1);
            int shifted = position + (insert ? count : -count);
            return rows ? address with { Row = shifted } : address with { Column = shifted };
        });

    /// <summary>
    /// Like <see cref="Shift"/>, but only references inside the band of columns (or rows) between
    /// <paramref name="bandStart"/> and <paramref name="bandEnd"/> move: inserting or deleting cells shifts part of the sheet.
    /// </summary>
    internal static string ShiftBand(string formula, string context, string target, bool rows, int start, int count, bool insert,
        int bandStart, int bandEnd) =>
        Transform(formula, (sheet, address, _, _) =>
        {
            if (!(sheet ?? context).Equals(target, StringComparison.OrdinalIgnoreCase))
                return address;
            int across = rows ? address.Column : address.Row;
            int position = rows ? address.Row : address.Column;
            if (across < bandStart || across > bandEnd || position < start)
                return address;
            if (!insert && position < start + count)
                return new(-1, -1);
            int limit = rows ? CellAddress.MaxRows : CellAddress.MaxColumns;
            int shifted = position + (insert ? count : -count);
            if (shifted >= limit)
                return new(-1, -1);
            return rows ? address with { Row = shifted } : address with { Column = shifted };
        });

    private static string Transform(string formula, Func<string?, CellAddress, bool, bool, CellAddress> change) =>
        References().Replace(formula, match =>
        {
            var reference = match.Groups["cell"];
            if (!reference.Success || !CellAddress.TryParse(reference.ValueSpan, out var address))
                return match.Value;
            string? sheet = match.Groups["sheet"].Success ? match.Groups["sheet"].Value : null;
            if (sheet is { Length: > 1 } && sheet[0] == '\'')
                sheet = sheet[1..^1].Replace("''", "'");
            bool rowFixed = match.Groups["row"].Success, columnFixed = match.Groups["col"].Success;
            var result = change(sheet, address, rowFixed, columnFixed);
            if (!result.IsValid)
                return "#REF!";
            if (result == address)
                return match.Value;
            string prefix = match.Value[..(reference.Index - match.Index)];
            return prefix + (columnFixed ? "$" : "") + CellAddress.ColumnName(result.Column) +
                (rowFixed ? "$" : "") + (result.Row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        });

    [GeneratedRegex(""" "(?:""|[^"])*"|(?<![\p{L}\p{N}_.!])(?:(?<sheet>'(?:''|[^'])*'|[\p{L}_][\p{L}\p{N}_.]*)!)?(?<cell>(?<col>\$)?[A-Z]{1,3}(?<row>\$)?\d{1,7})(?![\p{L}\p{N}_.!(]) """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex References();
}
