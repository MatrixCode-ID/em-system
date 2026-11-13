using Em.Editing;

namespace Em.Controls.Write.Editing;

/// <summary>
/// The MatrixWriter command catalog. Tabs and groups follow WordCraft's ribbon (ui-egui/src/ribbon); commands with a
/// <see cref="EditorCommand.Parent"/> appear in that command's drop-down.
/// </summary>
public static class WriteCommands
{
    /// <summary>The contextual tabs shown while the caret is in a table.</summary>
    public const string TableDesignTab = "Table Design";
    /// <summary>The contextual tab shown while the caret is in a table, for row, column and cell commands.</summary>
    public const string TableLayoutTab = "Table Layout";
    /// <summary>The contextual tab shown while a header or footer is edited.</summary>
    public const string HeaderFooterTab = "Header & Footer";

    /// <summary>Gets the commands in ribbon order.</summary>
    public static IReadOnlyList<EditorCommand> Catalog => Commands.Value;
    private static readonly Lazy<IReadOnlyList<EditorCommand>> Commands = new(() => Array.AsReadOnly<EditorCommand>(
    [
        new("open", "Open", "FolderOpen", "File", "File", "Ctrl+O", false),
        new("save", "Save", "Save", "File", "File", "Ctrl+S", true),
        new("print", "Print preview", "Print", "File", "File", "Ctrl+P", false),
        new("export", "Export", "Export", "File", "File", null, false),

        new("cut", "Cut", "Cut", "Clipboard", "Home", "Ctrl+X", true),
        new("copy", "Copy", "Copy", "Clipboard", "Home", "Ctrl+C", false),
        new("paste", "Paste", "Paste", "Clipboard", "Home", "Ctrl+V", true),
        Child("paste-text", "Keep text only", "PasteText", "Clipboard", "Home", "paste"),
        new("undo", "Undo", "Undo", "Undo/Redo", "Home", "Ctrl+Z", true),
        new("redo", "Redo", "Redo", "Undo/Redo", "Home", "Ctrl+Y", true),

        new("font", "Font", "FontColor", "Font", "Home", null, true),
        new("size", "Font size", "FontSize", "Font", "Home", null, true),
        new("grow-font", "Increase font size", "FontGrow", "Font", "Home", null, true),
        new("shrink-font", "Decrease font size", "FontShrink", "Font", "Home", null, true),
        new("change-case", "Change case", "ChangeCase", "Font", "Home", null, true) { MenuOnly = true },
        Child("case-sentence", "Sentence case.", "", "Font", "Home", "change-case"),
        Child("case-lower", "lowercase", "", "Font", "Home", "change-case"),
        Child("case-upper", "UPPERCASE", "", "Font", "Home", "change-case"),
        Child("case-title", "Capitalize Each Word", "", "Font", "Home", "change-case"),
        Child("case-toggle", "tOGGLE cASE", "", "Font", "Home", "change-case"),
        new("clear-formatting", "Clear all formatting", "ClearFormatting", "Font", "Home", null, true),
        new("bold", "Bold", "Bold", "Font", "Home", "Ctrl+B", true),
        new("italic", "Italic", "Italic", "Font", "Home", "Ctrl+I", true),
        new("underline", "Underline", "Underline", "Font", "Home", "Ctrl+U", true),
        new("strike", "Strikethrough", "Strikethrough", "Font", "Home", null, true),
        new("subscript", "Subscript", "Subscript", "Font", "Home", null, true),
        new("superscript", "Superscript", "Superscript", "Font", "Home", null, true),
        new("highlight", "Text highlight color", "Highlight", "Font", "Home", null, true),
        Child("highlight-yellow", "Yellow", "", "Colors", "Home", "highlight"),
        Child("highlight-green", "Bright green", "", "Colors", "Home", "highlight"),
        Child("highlight-turquoise", "Turquoise", "", "Colors", "Home", "highlight"),
        Child("highlight-pink", "Pink", "", "Colors", "Home", "highlight"),
        Child("highlight-none", "No color", "", "None", "Home", "highlight"),
        new("color", "Font color", "FontColor", "Font", "Home", null, true),
        Child("color-auto", "Automatic", "", "Automatic", "Home", "color"),
        Child("color-red", "Red", "", "Colors", "Home", "color"),
        Child("color-blue", "Blue", "", "Colors", "Home", "color"),
        Child("color-green", "Green", "", "Colors", "Home", "color"),
        Child("color-custom", "More colors…", "", "More", "Home", "color"),

        new("bullets", "Bullets", "ListBullet", "Paragraph", "Home", null, true),
        new("numbering", "Numbering", "ListNumbered", "Paragraph", "Home", null, true),
        new("indent-out", "Decrease indent", "IndentDecrease", "Paragraph", "Home", null, true),
        new("indent-in", "Increase indent", "IndentIncrease", "Paragraph", "Home", null, true),
        new("left", "Align left", "AlignLeft", "Paragraph", "Home", "Ctrl+L", true),
        new("center", "Center", "AlignCenter", "Paragraph", "Home", "Ctrl+E", true),
        new("right", "Align right", "AlignRight", "Paragraph", "Home", "Ctrl+R", true),
        new("justify", "Justify", "AlignJustify", "Paragraph", "Home", "Ctrl+J", true),
        new("line-spacing", "Line and paragraph spacing", "LineSpacing", "Paragraph", "Home", null, true) { MenuOnly = true },
        Child("spacing-1", "1.0", "", "Line spacing", "Home", "line-spacing"),
        Child("spacing-115", "1.15", "", "Line spacing", "Home", "line-spacing"),
        Child("spacing-15", "1.5", "", "Line spacing", "Home", "line-spacing"),
        Child("spacing-2", "2.0", "", "Line spacing", "Home", "line-spacing"),
        Child("spacing-25", "2.5", "", "Line spacing", "Home", "line-spacing"),
        Child("spacing-3", "3.0", "", "Line spacing", "Home", "line-spacing"),
        Child("space-before", "Add space before paragraph", "", "Spacing", "Home", "line-spacing"),
        Child("space-after", "Remove space after paragraph", "", "Spacing", "Home", "line-spacing"),
        Child("paragraph", "Line spacing options…", "LineSpacing", "Options", "Home", "line-spacing"),

        new("style-normal", "Normal", "", "Styles", "Home", null, true),
        new("style-title", "Title", "", "Styles", "Home", null, true),
        new("style-heading", "Heading 1", "", "Styles", "Home", null, true),
        new("style-heading2", "Heading 2", "", "Styles", "Home", null, true),
        new("style-heading3", "Heading 3", "", "Styles", "Home", null, true),
        new("styles", "Styles", "Styles", "Styles", "Home", null, true),

        new("find", "Find", "Search", "Find", "View", "Ctrl+F", false),
        new("replace", "Replace", "Replace", "Editing", "Home", "Ctrl+H", true),
        new("select-all", "Select all", "SelectAll", "Clipboard", "View", "Ctrl+A", false),
        new("select-paragraph", "Select paragraph", "SelectAll", "Clipboard", "View", null, false),

        new("insert-table", "Table", "Table", "Tables", "Insert", null, true),
        Child("table-2x2", "2 × 2 table", "", "Quick tables", "Insert", "insert-table"),
        Child("table-3x3", "3 × 3 table", "", "Quick tables", "Insert", "insert-table"),
        Child("table-4x4", "4 × 4 table", "", "Quick tables", "Insert", "insert-table"),
        Child("table-custom", "Insert table…", "Table", "More", "Insert", "insert-table"),
        new("insert-image", "Pictures", "Image", "Illustrations", "Insert", null, true),
        new("resize-image", "Picture size", "Image", "Illustrations", "Insert", null, true),
        new("insert-date", "Date & time", "Calendar", "Text", "Insert", null, true),
        new("header", "Header", "HeaderFooter", "Header & Footer", "Insert", null, true),
        new("footer", "Footer", "HeaderFooter", "Header & Footer", "Insert", null, true),
        new("page-number", "Page number", "PageNumber", "Header & Footer", "Insert", null, true),
        new("page-count", "Page count", "PageNumber", "Header & Footer", "Insert", null, true),

        new("margins", "Margins", "Margins", "Page Setup", "Layout", null, true) { MenuOnly = true },
        Child("margins-normal", "Normal (1\" all sides)", "", "Page Setup", "Layout", "margins"),
        Child("margins-narrow", "Narrow (0.5\")", "", "Page Setup", "Layout", "margins"),
        Child("margins-moderate", "Moderate (1\" × 0.75\")", "", "Page Setup", "Layout", "margins"),
        Child("margins-wide", "Wide (1\" × 2\")", "", "Page Setup", "Layout", "margins"),
        Child("page-setup", "Custom margins…", "PageSetup", "More", "Layout", "margins"),
        new("orientation", "Orientation", "Orientation", "Page Setup", "Layout", null, true) { MenuOnly = true },
        Child("orientation-portrait", "Portrait", "", "Page Setup", "Layout", "orientation"),
        Child("orientation-landscape", "Landscape", "", "Page Setup", "Layout", "orientation"),
        new("size-menu", "Size", "PaperSize", "Page Setup", "Layout", null, true) { MenuOnly = true },
        Child("size-letter", "Letter (8.5\" × 11\")", "", "Page Setup", "Layout", "size-menu"),
        Child("size-legal", "Legal (8.5\" × 14\")", "", "Page Setup", "Layout", "size-menu"),
        Child("size-a4", "A4 (21 cm × 29.7 cm)", "", "Page Setup", "Layout", "size-menu"),
        Child("size-a5", "A5 (14.8 cm × 21 cm)", "", "Page Setup", "Layout", "size-menu"),
        Child("size-executive", "Executive (7.25\" × 10.5\")", "", "Page Setup", "Layout", "size-menu"),
        new("breaks", "Breaks", "PageBreak", "Page Setup", "Layout", null, true) { MenuOnly = true },
        Child("page-break", "Page break", "PageBreak", "Page breaks", "Layout", "breaks", "Ctrl+Enter"),
        Child("section-break", "Section break (next page)", "PageBreak", "Section breaks", "Layout", "breaks"),

        new("first-page", "Different first page", "PageSetup", "Options", HeaderFooterTab, null, true),
        new("even-pages", "Different odd and even pages", "PageSetup", "Options", HeaderFooterTab, null, true),
        new("close-story", "Close header and footer", "Close", "Close", HeaderFooterTab, null, true),

        new("table-shading", "Shading", "FillColor", "Table Styles", TableDesignTab, null, true),
        Child("shading-none", "No color", "", "None", TableDesignTab, "table-shading"),
        Child("shading-gray", "Gray", "", "Colors", TableDesignTab, "table-shading"),
        Child("shading-blue", "Blue", "", "Colors", TableDesignTab, "table-shading"),
        Child("shading-green", "Green", "", "Colors", TableDesignTab, "table-shading"),
        Child("shading-gold", "Gold", "", "Colors", TableDesignTab, "table-shading"),
        Child("shading-table", "Shade the whole table", "", "Scope", TableDesignTab, "table-shading"),
        new("table-borders", "Borders", "Borders", "Borders", TableDesignTab, null, true),
        Child("borders-all", "All borders", "Borders", "Table", TableDesignTab, "table-borders"),
        Child("borders-none", "No border", "BorderNone", "Table", TableDesignTab, "table-borders"),
        Child("borders-cell", "Border this cell", "BorderOutside", "Cell", TableDesignTab, "table-borders"),
        Child("borders-cell-none", "Remove this cell's border", "BorderNone", "Cell", TableDesignTab, "table-borders"),
        new("cell-format", "Shading and border…", "FormatCells", "Borders", TableDesignTab, null, true),

        new("table-delete-menu", "Delete", "Trash", "Rows & Columns", TableLayoutTab, null, true) { MenuOnly = true },
        Child("col-delete", "Delete columns", "DeleteColumn", "Rows & Columns", TableLayoutTab, "table-delete-menu"),
        Child("row-delete", "Delete rows", "DeleteRow", "Rows & Columns", TableLayoutTab, "table-delete-menu"),
        Child("table-delete", "Delete table", "Trash", "Table", TableLayoutTab, "table-delete-menu"),
        new("row-above", "Insert above", "InsertRowAbove", "Rows & Columns", TableLayoutTab, null, true),
        new("row-below", "Insert below", "InsertRowBelow", "Rows & Columns", TableLayoutTab, null, true),
        new("col-left", "Insert left", "InsertColumnLeft", "Rows & Columns", TableLayoutTab, null, true),
        new("col-right", "Insert right", "InsertColumnRight", "Rows & Columns", TableLayoutTab, null, true),
        new("merge", "Merge cells", "Merge", "Merge", TableLayoutTab, null, true),
        new("split", "Split cells", "SplitCells", "Merge", TableLayoutTab, null, true),
        new("split-table", "Split table", "SplitTable", "Merge", TableLayoutTab, null, true),
        new("autofit", "AutoFit", "ColumnWidth", "Cell Size", TableLayoutTab, null, true) { MenuOnly = true },
        Child("autofit-window", "AutoFit window", "", "Cell Size", TableLayoutTab, "autofit"),
        Child("autofit-fixed", "Fixed column width", "", "Cell Size", TableLayoutTab, "autofit"),
        new("distribute-columns", "Distribute columns", "DistributeColumns", "Cell Size", TableLayoutTab, null, true),
        new("repeat-header", "Repeat header rows", "TableRows", "Data", TableLayoutTab, null, true),
        new("table-to-text", "Convert to text", "TableToText", "Data", TableLayoutTab, null, true),

        new("previous-page", "Previous page", "ChevronLeft", "Pages", "View", null, false),
        new("next-page", "Next page", "ChevronRight", "Pages", "View", null, false),
        new("go-page", "Go to page", "GoToCell", "Pages", "View", "Ctrl+G", false),
        new("zoom-out", "Zoom out", "ZoomOut", "Zoom", "View", null, false),
        new("zoom-in", "Zoom in", "ZoomIn", "Zoom", "View", null, false),
        new("zoom-100", "100%", "ZoomReset", "Zoom", "View", null, false),
        new("one-page", "One page", "PageSetup", "Zoom", "View", null, false),
        new("page-width", "Page width", "ZoomFit", "Zoom", "View", null, false),
        new("ruler", "Ruler", "Ruler", "Show", "View", null, false)
    ]));

    private static EditorCommand Child(string id, string label, string icon, string group, string tab, string parent, string? shortcut = null) =>
        new(id, label, icon, group, tab, shortcut, true) { Parent = parent };
}
