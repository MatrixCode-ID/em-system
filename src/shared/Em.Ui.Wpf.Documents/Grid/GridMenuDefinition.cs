using System.Collections.ObjectModel;
using System.Windows.Markup;
using Em.Grid.Menus;

namespace Em.Ui.Wpf.Documents.Grid;

/// <summary>How a <see cref="GridMenuDefinition"/> combines with the built-in menu of its region.</summary>
public enum GridMenuMode
{
    /// <summary>The definition replaces the built-in entries. When several replace definitions target one region, the last wins.</summary>
    Replace,

    /// <summary>The definition entries are added after the built-in entries, separated by a divider.</summary>
    Append,

    /// <summary>The definition entries are added before the built-in entries, separated by a divider.</summary>
    Prepend,

    /// <summary>
    /// The definition entries are inserted at <see cref="GridMenuDefinition.Index"/> of the menu built so far.
    /// No divider is added automatically; add a <see cref="MenuSeparator"/> where one is needed.
    /// </summary>
    Insert
}

/// <summary>Declares context menu entries for one grid region, in XAML or code.</summary>
[ContentProperty(nameof(Items))]
public sealed class GridMenuDefinition
{
    /// <summary>Gets or sets the region whose menu the definition changes.</summary>
    public MenuRegion Region { get; set; }
    /// <summary>Gets or sets how the entries combine with the built-in menu. The default replaces it.</summary>
    public GridMenuMode Mode { get; set; } = GridMenuMode.Replace;

    /// <summary>Zero-based position used by <see cref="GridMenuMode.Insert"/>. Values outside the menu are clamped to its start or end.</summary>
    public int Index { get; set; }

    /// <summary>
    /// For <see cref="GridMenuMode.Insert"/>: insert before the top-level entry with this id (a command id or a submenu id).
    /// Takes precedence over <see cref="Index"/>. When no entry has the id, <see cref="Index"/> is used.
    /// </summary>
    public string? InsertBefore { get; set; }

    /// <summary>For <see cref="GridMenuMode.Insert"/>: insert after the top-level entry with this id. <see cref="InsertBefore"/> wins when both are set.</summary>
    public string? InsertAfter { get; set; }

    /// <summary>Gets the entries to add.</summary>
    public ObservableCollection<MenuEntry> Items { get; } = [];
}

/// <summary>A submenu that can be declared in XAML. It becomes a <see cref="MenuSubmenu"/> when the menu opens.</summary>
[ContentProperty(nameof(Items))]
public sealed record GridMenuSubmenu : MenuEntry
{
    /// <summary>Gets or sets the submenu caption.</summary>
    public string Label { get; set; } = "";
    /// <summary>Gets or sets the optional icon key.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional id that <see cref="GridMenuDefinition.InsertBefore"/> and <see cref="GridMenuDefinition.InsertAfter"/> can target.</summary>
    public string? Id { get; set; }

    /// <summary>Gets the nested entries.</summary>
    public ObservableCollection<MenuEntry> Items { get; } = [];
}
