using Em.Editing;

namespace Em.Controls.Sheet.Commands;

/// <summary>
/// The MatrixSheet command catalog. Tabs and groups follow GridCraft's ribbon (ui-egui/src/ribbon); commands with a
/// <see cref="EditorCommand.Parent"/> appear in that command's drop-down.
/// </summary>
public static class SheetCommands
{
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
        Child("paste-values", "Paste values", "PasteValues", "Clipboard", "Home", "paste"),
        Child("paste-formulas", "Paste formulas", "Function", "Clipboard", "Home", "paste"),
        Child("paste-formats", "Paste formatting", "PasteFormats", "Clipboard", "Home", "paste"),
        Child("paste-transpose", "Transpose", "Transpose", "Paste options", "Home", "paste"),
        new("undo", "Undo", "Undo", "Undo/Redo", "Home", "Ctrl+Z", true),
        new("redo", "Redo", "Redo", "Undo/Redo", "Home", "Ctrl+Y", true),

        new("font", "Font", "FontColor", "Font", "Home", null, true),
        new("font-size", "Font size", "FontSize", "Font", "Home", null, true),
        new("grow-font", "Increase font size", "FontGrow", "Font", "Home", null, true),
        new("shrink-font", "Decrease font size", "FontShrink", "Font", "Home", null, true),
        new("bold", "Bold", "Bold", "Font", "Home", "Ctrl+B", true),
        new("italic", "Italic", "Italic", "Font", "Home", "Ctrl+I", true),
        new("underline", "Underline", "Underline", "Font", "Home", "Ctrl+U", true),
        new("strike", "Strikethrough", "Strikethrough", "Font", "Home", "Ctrl+5", true),
        new("borders", "Borders", "Borders", "Font", "Home", null, true),
        Child("border-bottom", "Bottom border", "BorderBottom", "Edges", "Home", "borders"),
        Child("border-top", "Top border", "BorderTop", "Edges", "Home", "borders"),
        Child("border-left", "Left border", "BorderLeft", "Edges", "Home", "borders"),
        Child("border-right", "Right border", "BorderRight", "Edges", "Home", "borders"),
        Child("border-none", "No border", "BorderNone", "Sets", "Home", "borders"),
        Child("border-all", "All borders", "Borders", "Sets", "Home", "borders"),
        Child("border-outside", "Outside borders", "BorderOutside", "Sets", "Home", "borders"),
        Child("border-inside", "Inside borders", "BorderInside", "Sets", "Home", "borders"),
        new("fill", "Fill color", "FillColor", "Font", "Home", null, true),
        new("font-color", "Font color", "FontColor", "Font", "Home", null, true),

        new("align-top", "Top align", "AlignTop", "Alignment", "Home", null, true),
        new("align-middle", "Middle align", "AlignMiddle", "Alignment", "Home", null, true),
        new("align-bottom", "Bottom align", "AlignBottom", "Alignment", "Home", null, true),
        new("left", "Align left", "AlignLeft", "Alignment", "Home", null, true),
        new("center", "Center", "AlignCenter", "Alignment", "Home", null, true),
        new("right", "Align right", "AlignRight", "Alignment", "Home", null, true),
        new("indent-out", "Decrease indent", "IndentDecrease", "Alignment", "Home", null, true),
        new("indent-in", "Increase indent", "IndentIncrease", "Alignment", "Home", null, true),
        new("wrap", "Wrap text", "WrapText", "Alignment", "Home", null, true),
        new("merge", "Merge & center", "Merge", "Alignment", "Home", null, true),
        Child("merge-across", "Merge across", "Merge", "Alignment", "Home", "merge"),
        Child("merge-cells", "Merge cells", "Merge", "Alignment", "Home", "merge"),
        Child("unmerge", "Unmerge cells", "SplitCells", "Alignment", "Home", "merge"),

        new("number-format", "Number format", "NumberFormat", "Number", "Home", null, true),
        new("accounting", "Accounting number format", "Accounting", "Number", "Home", null, true),
        new("percent", "Percent style", "Percent", "Number", "Home", "Ctrl+Shift+5", true),
        new("thousands", "Comma style", "Thousands", "Number", "Home", null, true),
        new("decimal-add", "Increase decimal", "DecimalAdd", "Number", "Home", null, true),
        new("decimal-remove", "Decrease decimal", "DecimalRemove", "Number", "Home", null, true),
        new("format", "Format cells", "FormatCells", "Number", "Home", "Ctrl+1", true),

        new("insert-row", "Insert sheet rows", "InsertRow", "Cells", "Home", null, true),
        new("insert-column", "Insert sheet columns", "InsertColumn", "Cells", "Home", null, true),
        new("insert-cells-right", "Insert cells, shift right", "ShiftRight", "Cells", "Home", null, true),
        new("insert-cells-down", "Insert cells, shift down", "ShiftDown", "Cells", "Home", null, true),
        new("add-sheet", "Insert sheet", "Plus", "Cells", "Home", "Shift+F11", true),
        new("delete-row", "Delete sheet rows", "DeleteRow", "Cells", "Home", null, true),
        new("delete-column", "Delete sheet columns", "DeleteColumn", "Cells", "Home", null, true),
        new("delete-cells-left", "Delete cells, shift left", "ShiftLeft", "Cells", "Home", null, true),
        new("delete-cells-up", "Delete cells, shift up", "ShiftUp", "Cells", "Home", null, true),
        new("delete-sheet", "Delete sheet", "Trash", "Cells", "Home", null, true),
        new("format-menu", "Format", "FormatCells", "Cells", "Home", null, true) { MenuOnly = true },
        Child("row-height", "Row height…", "RowHeight", "Cell size", "Home", "format-menu"),
        Child("column-width", "Column width…", "ColumnWidth", "Cell size", "Home", "format-menu"),
        Child("autofit-column", "AutoFit column width", "BestFit", "Cell size", "Home", "format-menu"),
        Child("rename-sheet", "Rename sheet…", "Edit", "Organize sheets", "Home", "format-menu"),
        Child("hide-sheet", "Hide sheet", "Hide", "Organize sheets", "Home", "format-menu"),
        Child("unhide-sheet", "Unhide sheet…", "Show", "Organize sheets", "Home", "format-menu"),

        new("autosum", "AutoSum", "Sum", "Editing", "Home", "Alt+=", true),
        Child("sum", "Sum", "Sum", "Editing", "Home", "autosum"),
        Child("average", "Average", "Average", "Editing", "Home", "autosum"),
        Child("count", "Count numbers", "Count", "Editing", "Home", "autosum"),
        Child("max", "Max", "Max", "Editing", "Home", "autosum"),
        Child("min", "Min", "Min", "Editing", "Home", "autosum"),
        Child("function", "More functions…", "Function", "More", "Formulas", "autosum"),
        new("fill-menu", "Fill", "FillDown", "Editing", "Home", null, true) { MenuOnly = true },
        Child("fill-down", "Down", "FillDown", "Editing", "Home", "fill-menu", "Ctrl+D"),
        Child("fill-right", "Right", "FillRight", "Editing", "Home", "fill-menu", "Ctrl+R"),
        Child("fill-up", "Up", "FillUp", "Editing", "Home", "fill-menu"),
        Child("fill-left", "Left", "FillLeft", "Editing", "Home", "fill-menu"),
        new("clear-menu", "Clear", "ClearContents", "Editing", "Home", null, true) { MenuOnly = true },
        Child("clear-all", "Clear all", "Trash", "Editing", "Home", "clear-menu"),
        Child("clear-formats", "Clear formats", "ClearFormatting", "Editing", "Home", "clear-menu"),
        Child("clear", "Clear contents", "ClearContents", "Editing", "Home", "clear-menu", "Delete"),
        new("sort-menu", "Sort", "SortAscending", "Editing", "Home", null, true) { MenuOnly = true },
        Child("sort-ascending", "Sort A to Z", "SortAscending", "Editing", "Home", "sort-menu"),
        Child("sort-descending", "Sort Z to A", "SortDescending", "Editing", "Home", "sort-menu"),

        new("define-name", "Define name", "DefineName", "Defined Names", "Formulas", null, true),
        new("pick-list", "Pick from drop-down list", "DropDownList", "Data Tools", "Data", null, true),

        new("freeze", "Freeze panes", "Freeze", "Window", "View", null, true),
        Child("freeze-top-row", "Freeze top row", "Freeze", "Window", "View", "freeze"),
        Child("freeze-first-column", "Freeze first column", "Freeze", "Window", "View", "freeze"),
        Child("unfreeze", "Unfreeze panes", "Freeze", "Window", "View", "freeze"),

        new("find", "Find", "Search", "Find", "View", "Ctrl+F", false),
        new("replace", "Replace", "Replace", "Find", "Home", "Ctrl+H", true),
        new("select-all", "Select all", "SelectAll", "Clipboard", "View", "Ctrl+A", false),
        new("go", "Go to cell", "GoToCell", "Navigation", "View", "Ctrl+G", false),
        new("zoom-out", "Zoom out", "ZoomOut", "Zoom", "View", null, false),
        new("zoom-in", "Zoom in", "ZoomIn", "Zoom", "View", null, false),
        new("zoom-100", "100%", "ZoomReset", "Zoom", "View", null, false),
        new("zoom-selection", "Zoom to selection", "ZoomFit", "Zoom", "View", null, false)
    ]));

    private static EditorCommand Child(string id, string label, string icon, string group, string tab, string parent, string? shortcut = null) =>
        new(id, label, icon, group, tab, shortcut, true) { Parent = parent };
}
