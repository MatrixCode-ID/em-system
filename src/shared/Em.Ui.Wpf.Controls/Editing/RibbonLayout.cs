namespace Em.Ui.Wpf.Controls;

/// Presentation of one ribbon tab. Commands still come from the editor catalog; the layout only arranges them.
internal sealed record RibbonTabLayout(string Tab, params RibbonGroupLayout[] Groups);

internal sealed record RibbonGroupLayout(string Caption, string? Launcher, params RibbonSlot[] Slots);

internal abstract record RibbonSlot;

/// A 32 DIP icon box over a label. With a menu, the button opens the listed commands.
internal sealed record RibbonLarge(string Id, string? Label = null, string[]? Menu = null) : RibbonSlot;

/// Stacked rows of small buttons, icon buttons, fields and text.
internal sealed record RibbonRows(params RibbonItem[][] Rows) : RibbonSlot;

/// Style tiles with the up, down and more column.
internal sealed record RibbonGallery(string[] Ids, string? More) : RibbonSlot;

internal abstract record RibbonItem(string Id);

internal sealed record RibbonIcon(string Id, bool Drop = false) : RibbonItem(Id);

internal sealed record RibbonSmall(string Id, string? Label = null) : RibbonItem(Id);

internal sealed record RibbonField(string Id, double Width) : RibbonItem(Id);

internal sealed record RibbonText(string Id) : RibbonItem(Id);

/// Editor state the toolbar shows: toggled commands, field values and their choices.
internal interface IToolbarState
{
    bool IsToggled(string id);
    string? Value(string id);
    IReadOnlyList<(string Label, string Value)> Choices(string id);
    void Apply(string id, string value);
    IReadOnlyList<RibbonTabLayout> RibbonTabs { get; }
}
