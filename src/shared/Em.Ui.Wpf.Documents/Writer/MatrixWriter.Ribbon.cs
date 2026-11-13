using System.Globalization;
using System.Windows.Media;
using Em.Ui.Wpf.Documents.Editing;
using Em.Write.Editing;
using Em.Write.Model;

namespace Em.Ui.Wpf.Documents.Writer;

public sealed partial class MatrixWriter
{
    // Arrangement from FigCraft page 90 MatrixWriter (frames "MatrixWriter · Full · Ribbon" and "Ribbon · Viewer") with the
    // groups of WordCraft's ribbon (ui-egui/src/ribbon/home.rs, insert.rs, layout.rs, table.rs, view.rs). A button whose
    // command has children in the catalog shows them in its drop-down.
    private static readonly RibbonGroupLayout ZoomGroup = new("Zoom", null,
        new RibbonLarge("zoom-out"), new RibbonRows([new RibbonField("zoom", 84)], [new RibbonSmall("zoom-100")]), new RibbonLarge("zoom-in"),
        new RibbonRows([new RibbonSmall("one-page")], [new RibbonSmall("page-width")]));

    private static readonly RibbonGroupLayout PagesGroup = new("Pages", null,
        new RibbonRows([new RibbonIcon("previous-page"), new RibbonField("page", 64), new RibbonText("page-total"),
            new RibbonIcon("next-page"), new RibbonIcon("go-page")]));

    private static readonly RibbonTabLayout[] Ribbon =
    [
        new("Home",
            new("Clipboard", null,
                new RibbonLarge("paste"),
                new RibbonRows([new RibbonSmall("cut")], [new RibbonSmall("copy")])),
            new("Font", "font",
                new RibbonRows(
                    [new RibbonField("font", 152), new RibbonField("size", 64), new RibbonIcon("grow-font"), new RibbonIcon("shrink-font"),
                        new RibbonIcon("change-case"), new RibbonIcon("clear-formatting")],
                    [new RibbonIcon("bold"), new RibbonIcon("italic"), new RibbonIcon("underline"), new RibbonIcon("strike"),
                        new RibbonIcon("subscript"), new RibbonIcon("superscript"), new RibbonIcon("highlight"), new RibbonIcon("color")])),
            new("Paragraph", "paragraph",
                new RibbonRows(
                    [new RibbonIcon("bullets"), new RibbonIcon("numbering"), new RibbonIcon("indent-out"), new RibbonIcon("indent-in")],
                    [new RibbonIcon("left"), new RibbonIcon("center"), new RibbonIcon("right"), new RibbonIcon("justify"),
                        new RibbonIcon("line-spacing")])),
            new("Styles", "styles",
                new RibbonGallery(["style-normal", "style-title", "style-heading", "style-heading2", "style-heading3"], "styles")),
            new("Editing", null,
                new RibbonRows([new RibbonSmall("find")], [new RibbonSmall("replace")], [new RibbonSmall("select-all", "Select")]))),
        new("Insert",
            new("Pages", null, new RibbonLarge("page-break", "Page\nBreak")),
            new("Tables", null, new RibbonLarge("insert-table")),
            new("Illustrations", null, new RibbonLarge("insert-image"), new RibbonLarge("resize-image", "Picture\nSize")),
            new("Header & Footer", null,
                new RibbonLarge("header"), new RibbonLarge("footer"),
                new RibbonRows([new RibbonSmall("page-number")], [new RibbonSmall("page-count")])),
            new("Text", null, new RibbonLarge("insert-date", "Date &\nTime"))),
        new("Layout",
            new("Page Setup", "page-setup",
                new RibbonLarge("margins"), new RibbonLarge("orientation"), new RibbonLarge("size-menu"),
                new RibbonRows([new RibbonSmall("breaks")], [new RibbonSmall("section-break", "Section")])),
            new("Paragraph", "paragraph",
                new RibbonRows(
                    [new RibbonText("label-before"), new RibbonField("space-before", 76), new RibbonText("label-after"),
                        new RibbonField("space-after", 76)],
                    [new RibbonText("label-indent"), new RibbonIcon("indent-out"), new RibbonIcon("indent-in")]))),
        new("View",
            ZoomGroup,
            new("Show", null, new RibbonLarge("ruler")),
            new("Find", null, new RibbonLarge("find"), new RibbonRows([new RibbonSmall("select-all")], [new RibbonSmall("select-paragraph")])),
            PagesGroup),
        new(WriteCommands.TableDesignTab,
            new("Table Styles", null, new RibbonLarge("table-shading")),
            new("Borders", "cell-format",
                new RibbonLarge("table-borders"),
                new RibbonRows([new RibbonSmall("borders-all")], [new RibbonSmall("borders-none")], [new RibbonSmall("borders-cell")])),
            new("Table Data", null,
                new RibbonRows([new RibbonSmall("repeat-header")], [new RibbonSmall("table-to-text")]))),
        new(WriteCommands.TableLayoutTab,
            new("Rows & Columns", null,
                new RibbonLarge("table-delete-menu"),
                new RibbonLarge("row-above", "Insert\nAbove"),
                new RibbonRows([new RibbonSmall("row-below")], [new RibbonSmall("col-left")], [new RibbonSmall("col-right")])),
            new("Merge", null,
                new RibbonRows([new RibbonSmall("merge")], [new RibbonSmall("split")], [new RibbonSmall("split-table")])),
            new("Cell Size", null,
                new RibbonLarge("autofit"),
                new RibbonRows([new RibbonText("label-width"), new RibbonField("column-width", 84)], [new RibbonSmall("distribute-columns")])),
            new("Alignment", null,
                new RibbonRows([new RibbonIcon("left"), new RibbonIcon("center")], [new RibbonIcon("right"), new RibbonIcon("justify")])),
            new("Data", null,
                new RibbonRows([new RibbonSmall("repeat-header")], [new RibbonSmall("table-to-text")]))),
        new(WriteCommands.HeaderFooterTab,
            new("Header & Footer", null, new RibbonLarge("header"), new RibbonLarge("footer")),
            new("Insert", null, new RibbonRows([new RibbonSmall("page-number")], [new RibbonSmall("page-count")])),
            new("Options", null, new RibbonRows([new RibbonSmall("first-page")], [new RibbonSmall("even-pages")])),
            new("Close", null, new RibbonLarge("close-story", "Close Header\nand Footer"))),
        new("Viewer",
            ZoomGroup,
            new("Find", null, new RibbonLarge("find")),
            new("Clipboard", null, new RibbonLarge("copy"), new RibbonLarge("select-all")),
            new("Output", null, new RibbonLarge("print", "Print"), new RibbonLarge("export")),
            PagesGroup)
    ];

    private static readonly double[] FontSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];
    private static readonly double[] ZoomLevels = [.25, .5, .75, 1, 1.25, 1.5, 2, 3, 4];
    private static IReadOnlyList<(string, string)>? fontFamilies;

    private protected override IReadOnlyList<RibbonTabLayout> RibbonTabs => Ribbon;

    private (WriteParagraph? Paragraph, WriteCharacterStyle Characters, WriteParagraphStyle Format) CaretFormat()
    {
        var caret = Session.Selection.Caret;
        var paragraph = Session.GetParagraph(caret);
        if (paragraph is null)
            return (null, new(), new());
        int offset = caret.Offset;
        WriteRun? run = null;
        foreach (var item in paragraph.Runs)
        {
            if (offset <= item.Length)
            {
                run = item;
                break;
            }
            offset -= item.Length;
        }
        return (paragraph, Session.Document.Resolve(paragraph, run ?? new WriteRun("", paragraph.Mark)),
            Session.Document.Resolve(paragraph).Paragraph);
    }

    private protected override bool IsCommandToggled(string id)
    {
        if (session is null)
            return false;
        var (paragraph, characters, format) = CaretFormat();
        if (paragraph is null)
            return false;
        WriteListKind? list = paragraph.NumberingId is { } numbering && Session.Document.Numbering.TryGetValue(numbering, out var entry)
            ? entry.Kind : null;
        return id switch
        {
            "bold" => characters.Bold == true,
            "italic" => characters.Italic == true,
            "underline" => characters.Underline == true,
            "strike" => characters.Strike == true,
            "superscript" => characters.Baseline == WriteBaseline.Superscript,
            "subscript" => characters.Baseline == WriteBaseline.Subscript,
            "left" => format.Alignment is null or WriteAlignment.Left,
            "center" => format.Alignment == WriteAlignment.Center,
            "right" => format.Alignment == WriteAlignment.Right,
            "justify" => format.Alignment == WriteAlignment.Justify,
            "bullets" => list == WriteListKind.Bullet,
            "numbering" => list == WriteListKind.Decimal,
            "style-normal" => paragraph.StyleId == "Normal",
            "style-heading" => paragraph.StyleId == "Heading1",
            "style-heading2" => paragraph.StyleId == "Heading2",
            "style-heading3" => paragraph.StyleId == "Heading3",
            "style-title" => paragraph.StyleId == "Title",
            "repeat-header" => Session.CurrentTable is { } table && table.Rows[0].RepeatHeader,
            "first-page" => Session.Document.Sections[Session.Selection.Caret.Section].Page.DifferentFirstPage,
            "even-pages" => Session.Document.Sections[Session.Selection.Caret.Section].Page.DifferentEvenPages,
            "ruler" => showRuler,
            "spacing-1" => Math.Abs((format.LineSpacing ?? 1) - 1) < .01,
            "spacing-115" => Math.Abs((format.LineSpacing ?? 1) - 1.15) < .01,
            "spacing-15" => Math.Abs((format.LineSpacing ?? 1) - 1.5) < .01,
            "spacing-2" => Math.Abs((format.LineSpacing ?? 1) - 2) < .01,
            "spacing-25" => Math.Abs((format.LineSpacing ?? 1) - 2.5) < .01,
            "spacing-3" => Math.Abs((format.LineSpacing ?? 1) - 3) < .01,
            _ => false
        };
    }

    private protected override string? ToolbarValue(string id) => id switch
    {
        "font" => CaretFormat().Characters.FontFamily ?? "Noto Sans",
        "size" => Points(CaretFormat().Characters.FontSize ?? 14.667),
        "zoom" => Percent(zoom),
        "page" => page.ToString(CultureInfo.InvariantCulture),
        "page-total" => "of " + Math.Max(1, PageCount).ToString(CultureInfo.InvariantCulture),
        "label-indent" => "Indent",
        "label-before" => "Before",
        "label-after" => "After",
        "label-width" => "Width",
        "space-before" => Points(CaretFormat().Format.SpaceBefore ?? 0) + " pt",
        "space-after" => Points(CaretFormat().Format.SpaceAfter ?? 0) + " pt",
        "column-width" => Session.CurrentTable is { } table && Session.Selection.Caret.Cell < table.ColumnWidths.Length ?
            Inches(table.ColumnWidths[Session.Selection.Caret.Cell]) : null,
        _ => null
    };

    private static readonly double[] Spacings = [0, 3, 6, 8, 10, 12, 18, 24, 36];
    private static readonly double[] ColumnInches = [0.5, 0.75, 1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6];

    private static string Inches(double dip) => (dip / 96).ToString("0.##", CultureInfo.InvariantCulture) + "\"";

    private protected override IReadOnlyList<(string Label, string Value)> ToolbarChoices(string id) => id switch
    {
        "font" => fontFamilies ??= [.. Fonts.SystemFontFamilies.Select(family => family.Source).Order(StringComparer.CurrentCultureIgnoreCase)
            .Select(name => (name, name))],
        "size" => [.. FontSizes.Select(size => Points(size * 4 / 3)).Select(text => (text, text))],
        "zoom" => [.. ZoomLevels.Select(Percent).Select(text => (text, text))],
        "space-before" or "space-after" => [.. Spacings.Select(size => size.ToString("0.#", CultureInfo.InvariantCulture) + " pt")
            .Select(text => (text, text))],
        "column-width" => [.. ColumnInches.Select(size => Inches(size * 96)).Select(text => (text, text))],
        "page" => [.. Enumerable.Range(1, Math.Clamp(PageCount, 1, 2000))
            .Select(number => number.ToString(CultureInfo.InvariantCulture)).Select(text => (text, text))],
        _ => []
    };

    private protected override void ApplyToolbarValue(string id, string value)
    {
        switch (id)
        {
            case "font" when CanExecuteCommand("font"):
                Session.FormatCharacters(style => style with { FontFamily = value });
                break;
            case "size" when CanExecuteCommand("size") &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) && points is > 0 and <= 180:
                Session.FormatCharacters(style => style with { FontSize = points * 4 / 3 });
                break;
            case "zoom" when double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double percent):
                SetZoom(percent / 100);
                break;
            case "space-before" or "space-after" when CanExecuteCommand("paragraph") &&
                double.TryParse(value.Replace("pt", "", StringComparison.Ordinal).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double spacing) && spacing is >= 0 and <= 1584:
                Session.FormatParagraphs(style => id == "space-before" ? style with { SpaceBefore = spacing * 4 / 3 } :
                    style with { SpaceAfter = spacing * 4 / 3 });
                break;
            case "column-width" when CanExecuteCommand("distribute-columns") &&
                double.TryParse(value.TrimEnd('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out double inches) && inches is > 0 and <= 22:
                Session.ResizeColumn(Session.Selection.Caret.Cell, inches * 96);
                break;
            case "page" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int target):
                Navigate(target);
                break;
        }
    }

    // Character sizes are stored in DIP; the ribbon shows points like the file formats do.
    private static string Points(double dip) => (Math.Round(dip * 3 / 4 * 2) / 2).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
