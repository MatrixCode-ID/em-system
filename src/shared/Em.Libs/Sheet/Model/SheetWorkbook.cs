// Ported from GridCraft (model/src/workbook.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Collections.ObjectModel;
using Em.Sheet.Cells;

namespace Em.Sheet.Model;

/// <summary>A spreadsheet workbook: sheets, a shared style table, defined names and the date system.</summary>
public sealed class SheetWorkbook
{
    private readonly List<SheetTab> sheets;
    private readonly List<SheetStyle> styles = [new()];
    private readonly Dictionary<SheetStyle, int> styleIds = new();
    private readonly Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a workbook with one empty sheet named Sheet1.</summary>
    public SheetWorkbook() : this([new("Sheet1")]) { }

    internal SheetWorkbook(IEnumerable<SheetTab> tabs)
    {
        sheets = [.. tabs];
        Sheets = sheets.AsReadOnly();
        Styles = styles.AsReadOnly();
        DefinedNames = new ReadOnlyDictionary<string, string>(names);
        styleIds.Add(styles[0], 0);
    }

    /// <summary>Gets the sheets in tab order.</summary>
    public IReadOnlyList<SheetTab> Sheets { get; }
    /// <summary>Gets the distinct cell styles; index 0 is the default style.</summary>
    public IReadOnlyList<SheetStyle> Styles { get; }
    /// <summary>Gets the defined names and the references they stand for.</summary>
    public IReadOnlyDictionary<string, string> DefinedNames { get; }
    /// <summary>Gets the date system used for date serial numbers.</summary>
    public SheetDateSystem DateSystem { get; internal set; }

    internal List<SheetTab> MutableSheets => sheets;
    internal Dictionary<string, string> MutableNames => names;

    internal int Intern(SheetStyle style)
    {
        if (styleIds.TryGetValue(style, out int index))
            return index;

        index = styles.Count;
        styleIds.Add(style, index);
        styles.Add(style);
        return index;
    }

    internal bool IsValidName(string name, SheetTab? except = null) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 31 &&
        name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) < 0 && !name.Any(char.IsControl) &&
        name[0] != '\'' && name[^1] != '\'' &&
        sheets.All(sheet => ReferenceEquals(sheet, except) || !name.Equals(sheet.Name, StringComparison.OrdinalIgnoreCase));
}
