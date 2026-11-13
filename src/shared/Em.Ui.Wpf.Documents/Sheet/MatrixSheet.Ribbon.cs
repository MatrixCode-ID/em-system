using System.Globalization;
using System.Windows.Media;
using Em.Editing;
using Em.Sheet.Model;
using Em.Ui.Wpf.Documents.Editing;

namespace Em.Ui.Wpf.Documents.Sheet;

public sealed partial class MatrixSheet
{
    // Arrangement from FigCraft page 80 MatrixSheet (frame "MatrixSheet · Full · Ribbon") with the groups of GridCraft's
    // ribbon (ui-egui/src/ribbon/home.rs, insert.rs, formulas.rs, data.rs, view.rs). A button whose command has children in
    // the catalog shows them in its drop-down.
    private static readonly RibbonGroupLayout ZoomGroup = new("Zoom", null,
        new RibbonLarge("zoom-out"), new RibbonRows([new RibbonField("zoom", 84)], [new RibbonSmall("zoom-100")]), new RibbonLarge("zoom-in"),
        new RibbonLarge("zoom-selection", "Zoom to\nSelection"));

    private static readonly RibbonTabLayout[] Ribbon =
    [
        new("Home",
            new("Clipboard", null,
                new RibbonLarge("paste"),
                new RibbonRows([new RibbonSmall("cut")], [new RibbonSmall("copy")])),
            new("Font", "font",
                new RibbonRows(
                    [new RibbonField("font", 152), new RibbonField("font-size", 64), new RibbonIcon("grow-font"), new RibbonIcon("shrink-font")],
                    [new RibbonIcon("bold"), new RibbonIcon("italic"), new RibbonIcon("underline"), new RibbonIcon("strike"),
                        new RibbonIcon("borders"), new RibbonIcon("fill"), new RibbonIcon("font-color")])),
            new("Alignment", "format",
                new RibbonRows(
                    [new RibbonIcon("align-top"), new RibbonIcon("align-middle"), new RibbonIcon("align-bottom"), new RibbonIcon("wrap")],
                    [new RibbonIcon("left"), new RibbonIcon("center"), new RibbonIcon("right"), new RibbonIcon("indent-out"),
                        new RibbonIcon("indent-in")]),
                new RibbonLarge("merge", "Merge &\nCenter")),
            new("Number", "format",
                new RibbonRows(
                    [new RibbonField("number-format", 140)],
                    [new RibbonIcon("accounting"), new RibbonIcon("percent"), new RibbonIcon("thousands"), new RibbonIcon("decimal-add"),
                        new RibbonIcon("decimal-remove")])),
            new("Cells", null,
                new RibbonLarge("insert-row", "Insert",
                    ["insert-row", "insert-column", "insert-cells-right", "insert-cells-down", "add-sheet"]),
                new RibbonLarge("delete-row", "Delete",
                    ["delete-row", "delete-column", "delete-cells-left", "delete-cells-up", "delete-sheet"]),
                new RibbonLarge("format-menu")),
            new("Editing", null,
                new RibbonRows([new RibbonSmall("autosum")], [new RibbonSmall("fill-menu")], [new RibbonSmall("clear-menu")]),
                new RibbonLarge("sort-menu", "Sort &\nFilter"),
                new RibbonLarge("find", "Find &\nSelect", ["find", "replace", "go", "select-all"]))),
        new("Insert",
            new("Cells", null,
                new RibbonLarge("insert-row", "Rows"), new RibbonLarge("insert-column", "Columns"),
                new RibbonRows([new RibbonSmall("insert-cells-right")], [new RibbonSmall("insert-cells-down")])),
            new("Sheets", null, new RibbonLarge("add-sheet", "Sheet"))),
        new("Formulas",
            new("Function Library", null,
                new RibbonLarge("function", "Insert\nFunction"),
                new RibbonLarge("autosum")),
            new("Defined Names", null, new RibbonLarge("define-name", "Define\nName"))),
        new("Data",
            new("Sort", null, new RibbonLarge("sort-ascending", "Sort\nA to Z"), new RibbonLarge("sort-descending", "Sort\nZ to A")),
            new("Data Tools", null, new RibbonLarge("pick-list", "Pick from\nList"))),
        new("View",
            ZoomGroup,
            new("Window", null, new RibbonLarge("freeze")),
            new("Find", null, new RibbonLarge("find"), new RibbonRows([new RibbonSmall("replace")], [new RibbonSmall("go", "Go to")]))),
        new("Viewer",
            ZoomGroup,
            new("Find", null, new RibbonLarge("find")),
            new("Clipboard", null, new RibbonLarge("copy"), new RibbonLarge("select-all")),
            new("Output", null, new RibbonLarge("print", "Print"), new RibbonLarge("export")),
            new("Navigation", null, new RibbonLarge("go", "Go to")))
    ];

    private static readonly (string Label, string Value)[] NumberFormats =
    [
        ("General", "General"), ("Number", "0.00"), ("Thousands", "#,##0.00"), ("Percent", "0.00%"),
        ("Scientific", "0.00E+00"), ("Text", "@")
    ];

    private static readonly double[] FontSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];
    private static readonly double[] ZoomLevels = [.25, .5, .75, 1, 1.25, 1.5, 2, 3, 4];
    private static IReadOnlyList<(string, string)>? fontFamilies;

    private protected override IReadOnlyList<RibbonTabLayout> RibbonTabs => Ribbon;

    private SheetStyle CurrentStyle => Session.Workbook.Styles[Session.ActiveSheet[Session.ActiveCell].StyleIndex];

    private protected override bool IsCommandToggled(string id)
    {
        if (session is null)
            return false;
        var style = CurrentStyle;
        return id switch
        {
            "bold" => style.Bold,
            "italic" => style.Italic,
            "underline" => style.Underline,
            "strike" => style.Strike,
            "wrap" => style.Wrap,
            "left" => style.Alignment == CellAlignment.Start,
            "center" => style.Alignment == CellAlignment.Center,
            "right" => style.Alignment == CellAlignment.End,
            "borders" => style.Borders == CellBorders.All,
            "merge" => Session.ActiveSheet.Merges.Any(Session.Selection.Intersects),
            "freeze" => Session.ActiveSheet.FrozenRows > 0 || Session.ActiveSheet.FrozenColumns > 0,
            "align-top" => style.VerticalAlignment == CellVerticalAlignment.Top,
            "align-middle" => style.VerticalAlignment == CellVerticalAlignment.Center,
            "align-bottom" => style.VerticalAlignment == CellVerticalAlignment.Bottom,
            "percent" => style.NumberFormat.EndsWith('%'),
            _ => false
        };
    }

    private protected override string? ToolbarValue(string id) => id switch
    {
        "font" => CurrentStyle.FontFamily,
        "font-size" => Points(CurrentStyle.FontSize),
        "number-format" => CurrentStyle.NumberFormat,
        "zoom" => Percent(zoom),
        _ => null
    };

    private protected override IReadOnlyList<(string Label, string Value)> ToolbarChoices(string id) => id switch
    {
        "font" => fontFamilies ??= [.. Fonts.SystemFontFamilies.Select(family => family.Source).Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(name => (name, name))],
        "font-size" => [.. FontSizes.Select(size => Points(size * 4 / 3)).Select(text => (text, text))],
        "number-format" => NumberFormats,
        "zoom" => [.. ZoomLevels.Select(Percent).Select(text => (text, text))],
        _ => []
    };

    private protected override void ApplyToolbarValue(string id, string value)
    {
        var range = Session.Selection;
        switch (id)
        {
            case "font" when CanExecuteCommand("font"):
                Session.Format(range, style => style with { FontFamily = value });
                break;
            case "font-size" when CanExecuteCommand("font-size") &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) && points is > 0 and <= 409:
                Session.Format(range, style => style with { FontSize = points * 4 / 3 });
                break;
            case "number-format" when CanExecuteCommand("number-format"):
                Session.Format(range, style => style with { NumberFormat = value });
                break;
            case "zoom" when double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent):
                SetZoom(percent / 100);
                break;
        }
    }

    // Cell font sizes are stored in DIP; the ribbon shows points like the file formats do.
    private static string Points(double dip) => (Math.Round(dip * 3 / 4 * 2) / 2).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
