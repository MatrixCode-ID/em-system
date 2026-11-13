using System.Collections.Immutable;

namespace Em.Grid.Menus;

/// <summary>The grid area a context menu was opened on.</summary>
public enum MenuRegion
{
    /// <summary>A column header.</summary>
    ColumnHeader,
    /// <summary>The group panel above the column headers.</summary>
    GroupPanel,
    /// <summary>A data cell.</summary>
    RowCell,
    /// <summary>A group header row.</summary>
    GroupRow,
    /// <summary>The total summary footer.</summary>
    TotalFooter,
    /// <summary>A group summary footer.</summary>
    GroupFooter,
    /// <summary>The filter panel below the rows.</summary>
    FilterPanel,
    /// <summary>An in-place editor.</summary>
    Editor
}

/// <summary>Describes where a context menu was opened.</summary>
/// <param name="Region">The grid area.</param>
/// <param name="ColumnKey">The column under the pointer, if any.</param>
/// <param name="RowKey">The key of the row under the pointer, if any.</param>
/// <param name="GroupLevel">The nesting level of the group row, if any.</param>
/// <param name="SummaryKey">The summary under the pointer, if any.</param>
public sealed record MenuHit(
    MenuRegion Region,
    string? ColumnKey = null,
    string? RowKey = null,
    int? GroupLevel = null,
    string? SummaryKey = null);

/// <summary>Base of the renderer-neutral context menu model.</summary>
public abstract record MenuEntry;

/// <summary>A separator line between menu groups.</summary>
public sealed record MenuSeparator : MenuEntry;

/// <summary>A non-clickable caption that starts a group of menu items.</summary>
/// <param name="Label">The caption text.</param>
public sealed record MenuSection(string Label) : MenuEntry
{
    /// <summary>Creates an empty section, for XAML and serializers.</summary>
    public MenuSection() : this("") { }
}

/// <summary>A clickable menu item bound to a grid command.</summary>
/// <param name="CommandId">The identifier of the command to run.</param>
/// <param name="Label">The item caption.</param>
/// <param name="Icon">An optional icon glyph.</param>
/// <param name="Shortcut">An optional shortcut text shown next to the caption.</param>
/// <param name="IsEnabled">Whether the item can be clicked.</param>
/// <param name="IsChecked">Whether the item shows a check mark.</param>
public sealed record MenuCommand(
    string CommandId,
    string Label,
    string? Icon = null,
    string? Shortcut = null,
    bool IsEnabled = true,
    bool IsChecked = false) : MenuEntry
{
    /// <summary>Creates an empty command, for XAML and serializers.</summary>
    public MenuCommand() : this("", "") { }
}

/// <summary>A menu item that opens nested items.</summary>
/// <param name="Label">The item caption.</param>
/// <param name="Items">The nested entries.</param>
/// <param name="Icon">An optional icon glyph.</param>
/// <param name="Id">An optional identifier for finding the submenu when customizing menus.</param>
public sealed record MenuSubmenu(string Label, ImmutableArray<MenuEntry> Items, string? Icon = null, string? Id = null) : MenuEntry
{
    /// <summary>Creates an empty submenu, for XAML and serializers.</summary>
    public MenuSubmenu() : this("", []) { }
}

/// <summary>Event data for a context menu that is about to open; handlers can change or cancel it.</summary>
/// <param name="hit">Where the menu was opened.</param>
/// <param name="items">The default entries.</param>
public sealed class MenuOpeningEventArgs(MenuHit hit, ImmutableArray<MenuEntry> items) : EventArgs
{
    /// <summary>Gets where the menu was opened.</summary>
    public MenuHit Hit { get; } = hit;
    /// <summary>Gets or sets the entries to show. Handlers may replace the list.</summary>
    public ImmutableArray<MenuEntry> Items { get; set; } = items;
    /// <summary>Gets or sets whether the menu is suppressed.</summary>
    public bool Cancel { get; set; }
}
