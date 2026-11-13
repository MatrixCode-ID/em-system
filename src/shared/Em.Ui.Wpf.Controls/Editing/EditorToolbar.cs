using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Em.Editing;
using Em.Controls.Grid.Menus;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

internal sealed class EditorToolbar : ContentControl
{
    private static readonly Lazy<IReadOnlyDictionary<string, Geometry>> Icons = new(LoadIcons);

    private static IReadOnlyDictionary<string, Geometry> LoadIcons()
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("/Em.Ui.Wpf.Controls;component/Editing/EditorIcons.xaml", UriKind.Relative)
        };
        var icons = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        foreach (string key in resources.Keys)
        {
            var geometry = (Geometry)resources[key];
            geometry.Freeze();
            icons.Add(key, geometry);
        }
        return icons;
    }

    internal static Geometry? Geometry(string key) => Icons.Value.GetValueOrDefault(key);

    private static readonly string[] QuickAccess = ["save", "undo", "redo"];

    // A contextual group shows these tabs while the editor reports it, like WordCraft's Table Design and Table Layout.
    private static readonly Dictionary<string, string[]> ContextTabs = new(StringComparer.Ordinal)
    {
        ["Table Tools"] = ["Table Design", "Table Layout"],
        ["Header & Footer"] = ["Header & Footer"]
    };
    private static readonly HashSet<string> ContextualNames = ContextTabs.Values.SelectMany(tabs => tabs).ToHashSet(StringComparer.Ordinal);
    private static readonly string[] ViewerGroups = ["Zoom", "Find", "Clipboard", "Output", "Navigation", "Pages", "File"];
    private readonly IReadOnlyList<EditorCommand> catalog;
    private readonly Action<string> execute;
    private readonly Func<string, bool> canExecute;
    private readonly IToolbarState? state;
    private readonly List<(Button Control, string[] Ids, bool Card)> buttons = [];
    private readonly List<(ComboBox Control, string Id)> fields = [];
    private readonly List<(TextBlock Control, string Id)> texts = [];
    private bool syncing;
    private EditorMode mode;
    private ToolbarStyle style;
    private int widthBand = 4;
    private bool show;
    private bool allowOpen;
    private string active = "Home";
    private string? context;
    private Popup? popup;

    internal EditorToolbar(IReadOnlyList<EditorCommand> catalog, Action<string> execute, Func<string, bool> canExecute,
        IToolbarState? state = null)
    {
        this.state = state;
        this.catalog = catalog;
        this.execute = execute;
        this.canExecute = canExecute;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Focusable = false;
        SizeChanged += (_, _) =>
        {
            int band = ActualWidth switch { <= 0 => 4, < 360 => 0, < 720 => 1, < 960 => 2, < 1280 => 3, _ => 4 };
            if (band != widthBand)
            {
                widthBand = band;
                Rebuild();
            }
        };
    }

    internal IReadOnlyList<EditorCommand> Commands => catalog.Where(command =>
        command.IsAvailable(mode) && (allowOpen || command.Id != "open") && InContext(command.RibbonTab)).ToArray();

    private bool InContext(string tab) => !ContextualNames.Contains(tab) ||
        context is not null && ContextTabs.TryGetValue(context, out var tabs) && tabs.Contains(tab);

    /// <summary>The commands listed in the drop-down of <paramref name="id"/>.</summary>
    private static EditorCommand[] Children(string id, IReadOnlyList<EditorCommand> commands) =>
        [.. commands.Where(command => command.Parent == id)];

    /// <summary>Top-level commands: a command in a drop-down stays out of the bar while its parent is shown.</summary>
    private static IEnumerable<EditorCommand> TopLevel(IReadOnlyList<EditorCommand> commands) =>
        commands.Where(command => command.Parent is not { } parent || commands.All(item => item.Id != parent));
    internal bool Collapsed { get; private set; }
    internal ToolbarStyle EffectiveStyle => widthBand == 0 ? ToolbarStyle.Compact : style;

    internal void Configure(EditorMode newMode, ToolbarStyle newStyle, bool visible, bool open)
    {
        if (mode == newMode && style == newStyle && show == visible && allowOpen == open)
            return;
        (mode, style, show, allowOpen) = (newMode, newStyle, visible, open);
        Rebuild();
    }

    internal void SetContext(string? value)
    {
        if (context == value)
            return;
        context = value;
        Rebuild();
    }

    internal void Refresh()
    {
        foreach (var (button, ids, card) in buttons)
        {
            button.IsEnabled = ids.Any(canExecute);
            bool toggled = state?.IsToggled(ids[0]) == true;
            if (toggled)
                Material.Paint(button, BackgroundProperty, "SecondaryContainer");
            else if (card)
                Material.Paint(button, BackgroundProperty, "SurfaceContainerLowest");
            else
                button.ClearValue(BackgroundProperty);
        }
        if (state is null)
            return;
        syncing = true;
        try
        {
            foreach (var (box, id) in fields)
            {
                string? value = state.Value(id);
                var choices = state.Choices(id).Select(choice => new ChoiceItem(choice.Label, choice.Value)).ToList();
                var selected = choices.FirstOrDefault(choice => choice.Value == value);
                if (selected is null && value is not null)
                {
                    selected = new ChoiceItem(value, value);
                    choices.Insert(0, selected);
                }
                if (box.ItemsSource is not List<ChoiceItem> existing || !existing.SequenceEqual(choices))
                    box.ItemsSource = choices;
                box.SelectedItem = selected;
                box.IsEnabled = value is not null && (!catalog.Any(command => command.Id == id) || canExecute(id));
            }
            foreach (var (block, id) in texts)
                block.Text = state.Value(id) ?? "";
        }
        finally
        {
            syncing = false;
        }
    }

    internal bool HandleKey(KeyEventArgs args)
    {
        if (args.Key == Key.Escape && popup?.IsOpen == true)
        {
            popup.IsOpen = false;
            return true;
        }
        if (args.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.Control && EffectiveStyle == ToolbarStyle.Ribbon && show)
        {
            ToggleCollapse();
            return true;
        }
        return false;
    }

    private void Rebuild()
    {
        ClosePopup();
        buttons.Clear();
        fields.Clear();
        texts.Clear();
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Content = !show ? null : EffectiveStyle == ToolbarStyle.Ribbon ? Ribbon() : Compact();
        Refresh();
    }

    private FrameworkElement Compact()
    {
        var commands = Commands;
        var groups = Groups(TopLevel(commands).Where(command => !ContextualNames.Contains(command.RibbonTab)));
        int keep = widthBand switch { 0 => 2, 1 => 3, 2 => 4, 3 => 6, _ => groups.Length };
        var bar = Bar(48, "SurfaceContainer");
        var line = Row();
        var contextual = TopLevel(commands).Where(command => ContextualNames.Contains(command.RibbonTab)).ToArray();
        var overflow = groups.Skip(keep).SelectMany(group => group)
            .Concat(widthBand < 3 ? contextual : []).ToArray();
        if (mode == EditorMode.Viewer)
            AttachChip(bar);
        if (overflow.Length > 0)
            Right(bar, GroupMenu("More", "MoreHoriz", overflow.SelectMany(command => command.MenuOnly ?
                Children(command.Id, commands) : [command, .. Children(command.Id, commands)])));
        foreach (var group in groups.Take(keep))
        {
            if (line.Children.Count > 0)
                line.Children.Add(Divider(24));
            foreach (var command in group)
                line.Children.Add(CompactButton(command, commands));
        }
        bar.Children.Add(Scroll(line));
        if (context is null || contextual.Length == 0 || widthBand < 3)
            return bar;
        var second = Bar(40, "SurfaceContainerLow");
        var items = Row();
        var label = new TextBlock { Text = context, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        Material.Paint(label, TextBlock.ForegroundProperty, "Primary");
        items.Children.Add(label);
        foreach (var command in contextual)
            items.Children.Add(CompactButton(command, commands));
        second.Children.Add(Scroll(items));
        var stack = new StackPanel();
        stack.Children.Add(bar);
        stack.Children.Add(second);
        return stack;
    }

    private FrameworkElement Ribbon()
    {
        var commands = Commands;
        string[] tabs = mode == EditorMode.Viewer ? ["View"] : commands.Select(command => command.RibbonTab)
            .Concat(context is not null && ContextTabs.TryGetValue(context, out var contextTabs) ? contextTabs : [])
            .Concat(state?.RibbonTabs.Select(layout => layout.Tab).Where(InContext).Where(tab => tab != "Viewer") ?? [])
            .Distinct()
            .OrderBy(tab => tab switch { "File" => 0, "Home" => 1, "Insert" => 2, "Formulas" or "Layout" => 3,
                "Data" or "References" => 4, "View" => 5, _ when ContextualNames.Contains(tab) => 7, _ => 6 }).ToArray();
        string current = mode == EditorMode.Viewer ? "View" : tabs.Contains(active) ? active : tabs.Contains("Home") ? "Home" : tabs[0];
        var stack = new StackPanel();
        var bar = Bar(40, "SurfaceContainer");
        var collapse = Button(Collapsed ? "Expand ribbon" : "Collapse ribbon", Collapsed ? "ChevronDown" : "ChevronUp");
        collapse.Width = collapse.Height = 28;
        collapse.Margin = new Thickness(0, 0, 8, 0);
        collapse.Click += (_, _) => ToggleCollapse();
        Right(bar, collapse);
        if (mode == EditorMode.Viewer)
            AttachChip(bar);
        var line = Row();
        line.Margin = new Thickness(8, 0, 0, 0);
        if (mode == EditorMode.Full)
        {
            foreach (var id in QuickAccess)
                if (commands.FirstOrDefault(command => command.Id == id) is { } command)
                {
                    var quick = CommandButton(command);
                    quick.Width = quick.Height = 28;
                    line.Children.Add(quick);
                }
            line.Children.Add(Divider(24));
        }
        foreach (string tab in tabs)
        {
            bool contextual = ContextualNames.Contains(tab);
            bool selected = tab == current && !Collapsed;
            Button button;
            if (contextual)
            {
                if (line.Children.Count > 0 && line.Children[^1] is not StackPanel { Tag: "contextual" })
                    line.Children.Add(Divider(24));
                button = Material.Button("", "ToolbarButton");
                button.Height = 32;
                button.Padding = new Thickness(12, 0, 14, 0);
                var chip = Row();
                chip.Children.Add(Icon(tab.StartsWith("Table", StringComparison.Ordinal) ? "Table" : "HeaderFooter"));
                chip.Children.Add(new TextBlock { Text = tab, FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                button.Content = chip;
                Material.Paint(button, BackgroundProperty, selected ? "SecondaryContainer" : "SurfaceContainerHigh");
                Material.Paint(button, ForegroundProperty, selected ? "OnSecondaryContainer" : "OnSurfaceVariant");
                button.Margin = new Thickness(2, 0, 2, 0);
            }
            else
            {
                button = Material.Button(tab, "RibbonTab");
                if (selected)
                    Material.Paint(button, ForegroundProperty, "Primary");
            }
            button.Focusable = false;
            var tabPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Tag = contextual ? "contextual" : null };
            tabPanel.Children.Add(button);
            // The indicator slot is always present so every tab keeps the same height.
            var indicator = new Border
            {
                Height = 3, CornerRadius = new CornerRadius(3, 3, 0, 0),
                Margin = contextual ? new Thickness(16, 2, 16, 0) : new Thickness(12, 0, 12, 0)
            };
            if (contextual)
                button.Height = 32;
            if (selected)
                Material.Paint(indicator, Border.BackgroundProperty, "Primary");
            tabPanel.Children.Add(indicator);
            button.Click += (_, _) =>
            {
                if (mode == EditorMode.Full)
                    active = tab;
                if (Collapsed)
                    OpenOverlay(bar, commands, tab);
                else
                    Rebuild();
            };
            line.Children.Add(tabPanel);
        }
        bar.Children.Add(Scroll(line));
        stack.Children.Add(bar);
        if (!Collapsed)
            stack.Children.Add(RibbonPanel(commands, current));
        return stack;
    }

    private FrameworkElement RibbonPanel(IReadOnlyList<EditorCommand> commands, string tab)
    {
        var groups = Layout(commands, tab);
        // Narrow widths shrink groups from the right into single group buttons that open the group in a popup.
        int full = widthBand switch { >= 4 => groups.Length, 3 => groups.Length - 2, 2 => groups.Length - 4, _ => 0 };
        var line = Row();
        line.VerticalAlignment = VerticalAlignment.Stretch;
        for (int index = 0; index < groups.Length; index++)
        {
            if (line.Children.Count > 0)
                line.Children.Add(Divider(80));
            line.Children.Add(index < full ? Group(groups[index], commands) : GroupButton(groups[index], commands));
        }
        var panel = new Border { Height = 96, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 4, 8, 4), Child = Scroll(line) };
        Material.Paint(panel, Border.BackgroundProperty, "SurfaceContainerLow");
        Material.Paint(panel, Border.BorderBrushProperty, "OutlineVariant");
        return panel;
    }

    private RibbonGroupLayout[] Layout(IReadOnlyList<EditorCommand> commands, string tab)
    {
        var available = commands.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);
        var known = catalog.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);
        bool Visible(string id) => available.Contains(id) || (!known.Contains(id) && state?.Value(id) is not null);
        var scope = (mode == EditorMode.Viewer ? TopLevel(commands) : TopLevel(commands).Where(command => command.RibbonTab == tab)).ToArray();
        var defined = state?.RibbonTabs.FirstOrDefault(layout => layout.Tab == (mode == EditorMode.Viewer ? "Viewer" : tab));
        var result = new List<RibbonGroupLayout>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in defined?.Groups ?? [])
        {
            var slots = new List<RibbonSlot>();
            foreach (var slot in group.Slots)
            {
                RibbonSlot? kept = slot switch
                {
                    RibbonLarge large when large.Menu?.Where(Visible).ToArray() is var menu &&
                        (Visible(large.Id) || menu is { Length: > 0 }) => large with { Menu = menu },
                    RibbonRows rows when rows.Rows.Select(row => row.Where(item => Visible(item.Id)).ToArray())
                        .Where(row => row.Length > 0).ToArray() is { Length: > 0 } items => new RibbonRows(items),
                    RibbonGallery gallery when gallery.Ids.Where(Visible).ToArray() is { Length: > 0 } tiles =>
                        new RibbonGallery(tiles, gallery.More is { } more && Visible(more) ? more : null),
                    _ => null
                };
                if (kept is not null)
                    slots.Add(kept);
            }
            foreach (var id in group.Slots.SelectMany(Ids))
                used.Add(id);
            if (slots.Count > 0)
                foreach (var id in group.Slots.SelectMany(Ids).ToArray())
                    foreach (var child in Children(id, commands))
                        used.Add(child.Id);
            if (group.Launcher is not null)
                used.Add(group.Launcher);
            if (slots.Count > 0)
                result.Add(new(group.Caption, group.Launcher is { } launcher && Visible(launcher) ? launcher : null, [.. slots]));
        }
        // Commands of the tab that the layout does not place still appear, so a layout never hides catalog entries.
        foreach (var group in Groups(scope.Where(command => !used.Contains(command.Id) &&
                     (mode == EditorMode.Viewer || !QuickAccess.Contains(command.Id)))))
            result.Add(new(group.Key, group.FirstOrDefault(command => command.Id is "format" or "paragraph")?.Id,
                [.. group.Select(command => (RibbonSlot)new RibbonLarge(command.Id))]));
        return [.. result];
    }

    private static IEnumerable<string> Ids(RibbonSlot slot) => slot switch
    {
        RibbonLarge large => [large.Id, .. large.Menu ?? []],
        RibbonRows rows => rows.Rows.SelectMany(row => row.Select(item => item.Id)),
        RibbonGallery gallery => gallery.More is null ? gallery.Ids : [.. gallery.Ids, gallery.More],
        _ => []
    };

    private FrameworkElement Group(RibbonGroupLayout group, IReadOnlyList<EditorCommand> commands)
    {
        var content = Row();
        content.Height = 68;
        foreach (var slot in group.Slots)
            content.Children.Add(slot switch
            {
                RibbonLarge large => LargeButton(large, commands),
                RibbonRows rows => Rows(rows, commands),
                RibbonGallery gallery => Gallery(gallery, commands),
                _ => new Border()
            });
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 20 };
        var label = new TextBlock { Text = group.Caption, FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        Material.Paint(label, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        caption.Children.Add(label);
        if (group.Launcher is { } dialog && commands.FirstOrDefault(command => command.Id == dialog) is { } launch)
        {
            var launcher = Button(launch.Label, "DialogLauncher");
            launcher.Width = launcher.Height = 16;
            launcher.Margin = new Thickness(6, 0, 0, 0);
            launcher.Click += (_, _) => Invoke(dialog);
            Track(launcher, [dialog]);
            caption.Children.Add(launcher);
        }
        var section = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        section.Children.Add(content);
        section.Children.Add(caption);
        return section;
    }

    private Button GroupButton(RibbonGroupLayout group, IReadOnlyList<EditorCommand> commands)
    {
        string icon = group.Slots.SelectMany(Ids).Select(id => commands.FirstOrDefault(command => command.Id == id)?.IconKey)
            .FirstOrDefault(key => key is { Length: > 0 }) ?? "MoreHoriz";
        var button = Material.Button("", "ToolbarButton");
        button.Focusable = false;
        button.Height = 68;
        button.MinWidth = 52;
        button.Padding = new Thickness(6);
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Content = LargeContent(group.Caption, icon, true);
        button.ToolTip = group.Caption;
        button.Click += (_, _) =>
        {
            ClosePopup();
            var panel = Material.Panel(Group(group, commands), "SurfaceContainer", 4, new Thickness(8, 4, 8, 4));
            popup = new() { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = panel };
            popup.PreviewKeyDown += (_, args) => args.Handled = HandleKey(args);
            popup.IsOpen = true;
            Refresh();
        };
        return button;
    }

    private FrameworkElement LargeButton(RibbonLarge large, IReadOnlyList<EditorCommand> commands)
    {
        var command = commands.FirstOrDefault(item => item.Id == large.Id);
        // An explicit menu replaces the command's own drop-down and the whole button opens it.
        bool explicitMenu = large.Menu is { Length: > 0 };
        var menu = explicitMenu
            ? large.Menu!.Select(id => commands.FirstOrDefault(item => item.Id == id)).OfType<EditorCommand>().ToArray()
            : Children(large.Id, commands);
        var first = command ?? menu[0];
        string label = large.Label ?? first.Label;
        bool opens = explicitMenu && menu.Length > 1 || first.MenuOnly;
        if (menu.Length > 0 && !opens)
        {
            // Split button: the icon runs the command, the caption opens its drop-down.
            bool twoLines = label.Contains('\n');
            var top = Material.Button("", "ToolbarButton");
            top.Focusable = false;
            top.Height = twoLines ? 30 : 38;
            top.Padding = new Thickness(6, twoLines ? 1 : 3, 6, 0);
            top.Content = IconBox(first.IconKey, twoLines ? 26 : 32);
            top.ToolTip = Tip(first);
            System.Windows.Automation.AutomationProperties.SetName(top, label.Replace('\n', ' '));
            top.Click += (_, _) => Invoke(first.Id);
            Track(top, [first.Id]);
            var bottom = Material.Button("", "ToolbarButton");
            bottom.Focusable = false;
            bottom.Height = twoLines ? 34 : 26;
            bottom.Padding = new Thickness(4, 0, 4, 0);
            bottom.Content = Caption(label, true);
            bottom.ToolTip = first.Label + " options";
            System.Windows.Automation.AutomationProperties.SetName(bottom, first.Label + " options");
            bottom.Click += (_, _) => OpenMenu(bottom, menu);
            Track(bottom, [.. menu.Select(item => item.Id)]);
            var split = new StackPanel { MinWidth = 52, VerticalAlignment = VerticalAlignment.Top };
            split.Children.Add(top);
            split.Children.Add(bottom);
            return split;
        }
        var button = Material.Button("", "ToolbarButton");
        button.Focusable = false;
        button.Height = 64;
        button.MinWidth = 52;
        button.Padding = new Thickness(6, 2, 6, 0);
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Content = LargeContent(label, first.IconKey, opens);
        button.ToolTip = Tip(first);
        System.Windows.Automation.AutomationProperties.SetName(button, label.Replace('\n', ' '));
        if (opens)
        {
            button.Click += (_, _) => OpenMenu(button, menu);
            Track(button, [.. menu.Select(item => item.Id)]);
        }
        else
        {
            button.Click += (_, _) => Invoke(first.Id);
            Track(button, [first.Id]);
        }
        return button;
    }

    private FrameworkElement Rows(RibbonRows rows, IReadOnlyList<EditorCommand> commands)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };
        double gap = rows.Rows.Length > 2 ? 2 : 6;
        foreach (var row in rows.Rows)
        {
            var line = Row();
            line.HorizontalAlignment = HorizontalAlignment.Left;
            if (stack.Children.Count > 0)
                line.Margin = new Thickness(0, gap, 0, 0);
            foreach (var item in row)
                line.Children.Add(Item(item, commands));
            stack.Children.Add(line);
        }
        return stack;
    }

    private FrameworkElement Item(RibbonItem item, IReadOnlyList<EditorCommand> commands)
    {
        var command = commands.FirstOrDefault(entry => entry.Id == item.Id);
        switch (item)
        {
            case RibbonField field:
                var box = new ComboBox
                {
                    Style = Material.Style("ComboBox"), Height = 28, Width = field.Width, Padding = new Thickness(10, 0, 28, 0),
                    Margin = new Thickness(0, 0, 4, 0), MaxDropDownHeight = 320, ToolTip = command?.Label
                };
                System.Windows.Automation.AutomationProperties.SetName(box, command?.Label ?? field.Id);
                box.SelectionChanged += (_, _) =>
                {
                    if (!syncing && box.SelectedItem is ChoiceItem choice && state is not null)
                        state.Apply(field.Id, choice.Value);
                };
                fields.Add((box, field.Id));
                return box;
            case RibbonText text:
                var block = new TextBlock { Margin = new Thickness(4, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                Material.Paint(block, TextBlock.ForegroundProperty, "OnSurfaceVariant");
                texts.Add((block, text.Id));
                return block;
            case RibbonSmall small when command is not null:
            {
                var children = Children(command.Id, commands);
                var smallButton = Material.Button("", "ToolbarButton");
                smallButton.Focusable = false;
                smallButton.Height = 22;
                smallButton.Padding = new Thickness(4, 0, 8, 0);
                var content = Row();
                content.Children.Add(Icon(command.IconKey));
                content.Children.Add(new TextBlock
                {
                    Text = small.Label ?? command.Label, FontWeight = FontWeights.Medium,
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
                });
                if (command.MenuOnly)
                    content.Children.Add(Chevron());
                smallButton.Content = content;
                smallButton.ToolTip = Tip(command);
                System.Windows.Automation.AutomationProperties.SetName(smallButton, small.Label ?? command.Label);
                return WithMenu(smallButton, command, children, 22);
            }
            case RibbonIcon when command is not null:
            {
                var children = Children(command.Id, commands);
                var iconButton = Material.Button("", "ToolbarButton");
                iconButton.Focusable = false;
                iconButton.Height = 28;
                iconButton.MinWidth = 28;
                iconButton.Margin = new Thickness(0, 0, children.Length > 0 && !command.MenuOnly ? 0 : 2, 0);
                iconButton.Padding = new Thickness(4, 0, command.MenuOnly ? 2 : 4, 0);
                var glyph = Row();
                glyph.Children.Add(Icon(command.IconKey));
                if (command.MenuOnly)
                    glyph.Children.Add(Chevron());
                iconButton.Content = glyph;
                iconButton.ToolTip = Tip(command);
                System.Windows.Automation.AutomationProperties.SetName(iconButton, command.Label);
                return WithMenu(iconButton, command, children, 28);
            }
            default:
                return new Border();
        }
    }

    /// <summary>
    /// Wires <paramref name="button"/> to its command. A menu-only command opens its drop-down; a command with children gets a
    /// separate chevron that opens them, as a split button.
    /// </summary>
    private FrameworkElement WithMenu(Button button, EditorCommand command, EditorCommand[] children, double height)
    {
        if (command.MenuOnly)
        {
            button.Click += (_, _) => OpenMenu(button, children);
            Track(button, [.. children.Select(child => child.Id)]);
            return button;
        }
        button.Click += (_, _) => Invoke(command.Id);
        Track(button, [command.Id]);
        if (children.Length == 0)
            return button;
        var arrow = Material.Button("", "ToolbarButton");
        arrow.Focusable = false;
        arrow.Height = height;
        arrow.Width = 14;
        arrow.Padding = new Thickness(0);
        arrow.Margin = new Thickness(0, 0, 2, 0);
        arrow.Content = Material.Icon("ChevronDown", 14);
        arrow.ToolTip = command.Label + " options";
        System.Windows.Automation.AutomationProperties.SetName(arrow, command.Label + " options");
        arrow.Click += (_, _) => OpenMenu(button, children);
        Track(arrow, [.. children.Select(child => child.Id)]);
        var split = Row();
        split.Children.Add(button);
        split.Children.Add(arrow);
        return split;
    }

    /// <summary>A compact bar button: icon only, with the same drop-down behaviour as the ribbon.</summary>
    private FrameworkElement CompactButton(EditorCommand command, IReadOnlyList<EditorCommand> commands)
    {
        var children = Children(command.Id, commands);
        if (children.Length == 0 && !command.MenuOnly)
            return CommandButton(command);
        var button = Button(command.Label, "");
        var glyph = Row();
        glyph.Children.Add(Icon(command.IconKey.Length > 0 ? command.IconKey : "MoreHoriz"));
        if (command.MenuOnly)
            glyph.Children.Add(Chevron());
        button.Content = glyph;
        button.Height = 32;
        button.MinWidth = 32;
        button.Padding = new Thickness(6, 0, command.MenuOnly ? 2 : 6, 0);
        button.ToolTip = Tip(command);
        System.Windows.Automation.AutomationProperties.SetName(button, command.Label);
        return WithMenu(button, command, children, 32);
    }

    /// <summary>Opens the Material drop-down below <paramref name="target"/>, with dividers between catalog groups.</summary>
    private void OpenMenu(FrameworkElement target, IReadOnlyList<EditorCommand> items)
    {
        ClosePopup();
        if (items.Count == 0)
            return;
        var entries = ImmutableArray.CreateBuilder<MenuEntry>();
        string? group = null;
        foreach (var item in items)
        {
            if (group is not null && item.Group != group)
                entries.Add(new MenuSeparator());
            group = item.Group;
            entries.Add(new MenuCommand(item.Id, item.Label, item.IconKey.Length == 0 ? null : item.IconKey, item.Shortcut,
                canExecute(item.Id), state?.IsToggled(item.Id) == true));
        }
        var built = entries.ToImmutable();
        var menu = MenuRenderer.Attach(target, () => new(new MenuHit(MenuRegion.RowCell), built), id => new ToolbarCommand(this, id));
        target.ContextMenu = null;
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private sealed class ToolbarCommand(EditorToolbar toolbar, string id) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => toolbar.canExecute(id);
        public void Execute(object? parameter) => toolbar.Invoke(id);
    }

    private static string Tip(EditorCommand command) => command.Shortcut is null ? command.Label : $"{command.Label} ({command.Shortcut})";

    private FrameworkElement Gallery(RibbonGallery gallery, IReadOnlyList<EditorCommand> commands)
    {
        var line = Row();
        foreach (var id in gallery.Ids)
            if (commands.FirstOrDefault(command => command.Id == id) is { } command)
                line.Children.Add(CommandButton(command, true));
        var scroll = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        foreach (var (name, icon) in new[] { ("Previous styles", "ChevronUp"), ("Next styles", "ChevronDown") })
        {
            var step = Button(name, icon);
            step.Width = 24;
            step.Height = 20;
            // Every tile is visible, so the gallery has nothing to scroll.
            step.IsEnabled = false;
            scroll.Children.Add(step);
        }
        if (gallery.More is { } more && commands.FirstOrDefault(command => command.Id == more) is { } all)
        {
            var open = Button(all.Label, "MoreHoriz");
            open.Width = 24;
            open.Height = 20;
            open.Click += (_, _) => Invoke(more);
            Track(open, [more]);
            scroll.Children.Add(open);
        }
        line.Children.Add(scroll);
        return line;
    }

    private static UIElement LargeContent(string label, string icon, bool drop)
    {
        var panel = new StackPanel();
        // Two-line captions take the room of a shorter icon box, so the button keeps its 64 DIP height.
        bool twoLines = label.Contains('\n');
        panel.Children.Add(IconBox(icon, twoLines ? 26 : 32));
        var caption = Caption(label, drop);
        caption.Margin = new Thickness(0, twoLines ? 0 : 2, 0, 0);
        panel.Children.Add(caption);
        return panel;
    }

    private static Border IconBox(string icon, double height = 32)
    {
        var box = new Border { Width = 32, Height = height, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon.Length > 0)
        {
            var glyph = Icon(icon);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            box.Child = glyph;
        }
        return box;
    }

    /// <summary>A large button caption: one or two lines (split at "\n"), with the drop-down chevron after the last line.</summary>
    private static FrameworkElement Caption(string label, bool drop)
    {
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        caption.Children.Add(new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center,
            LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, VerticalAlignment = VerticalAlignment.Center
        });
        if (drop)
        {
            var chevron = Chevron();
            chevron.VerticalAlignment = VerticalAlignment.Bottom;
            caption.Children.Add(chevron);
        }
        return caption;
    }

    private static FrameworkElement Chevron()
    {
        var chevron = Material.Icon("ChevronDown", 16);
        chevron.Margin = new Thickness(2, 0, 0, 0);
        return chevron;
    }

    private void Track(Button button, string[] ids, bool card = false) => buttons.Add((button, ids, card));

    private sealed record ChoiceItem(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private IGrouping<string, EditorCommand>[] Groups(IEnumerable<EditorCommand> commands) =>
        (mode == EditorMode.Viewer ? commands.OrderBy(command => Array.IndexOf(ViewerGroups, Group(command))) : commands)
        .GroupBy(Group).ToArray();

    private string Group(EditorCommand command) => mode == EditorMode.Viewer && command.Id is "print" or "export" ? "Output" : command.Group;

    private void OpenOverlay(FrameworkElement target, IReadOnlyList<EditorCommand> commands, string tab)
    {
        ClosePopup();
        var panel = Material.Panel(RibbonPanel(commands, tab), "SurfaceContainerLow", 4, new Thickness(0));
        panel.Width = target.ActualWidth;
        popup = new() { PlacementTarget = target, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = panel };
        popup.PreviewKeyDown += (_, args) => args.Handled = HandleKey(args);
        popup.IsOpen = true;
    }

    private void ClosePopup()
    {
        if (popup is not null)
            popup.IsOpen = false;
        popup = null;
    }

    private void ToggleCollapse()
    {
        Collapsed = !Collapsed;
        Rebuild();
    }

    private Button GroupMenu(string label, string icon, IEnumerable<EditorCommand> source)
    {
        var button = Button(label, icon);
        var commands = source.ToArray();
        button.Click += (_, _) =>
        {
            var menu = Menu(commands);
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }

    internal ContextMenu Menu(IEnumerable<EditorCommand> commands)
    {
        var menu = new ContextMenu();
        foreach (var command in commands)
        {
            var item = new MenuItem { Header = command.Label, InputGestureText = command.Shortcut,
                Icon = command.IconKey.Length == 0 ? null : Icon(command.IconKey), IsEnabled = canExecute(command.Id) };
            item.Click += (_, _) => Invoke(command.Id);
            menu.Items.Add(item);
        }
        return menu;
    }

    private Button CommandButton(EditorCommand command, bool large = false)
    {
        var button = Button(command.Label, large ? "" : command.IconKey);
        if (IsStyle(command.Id))
            StyleCard(button, command, large);
        else if (large)
        {
            button.Height = 64;
            button.MinWidth = 52;
            button.Padding = new Thickness(6);
            button.Content = IconAndLabel(command.Label, command.IconKey);
        }
        button.IsEnabled = canExecute(command.Id);
        button.ToolTip = command.Shortcut is null ? command.Label : $"{command.Label} ({command.Shortcut})";
        button.Click += (_, _) => Invoke(command.Id);
        Track(button, [command.Id], IsStyle(command.Id));
        return button;
    }

    private static bool IsStyle(string id) => id.StartsWith("style-", StringComparison.Ordinal);

    private static void StyleCard(Button button, EditorCommand command, bool large)
    {
        bool heading = command.Id != "style-normal";
        button.Width = large ? 76 : 88;
        button.Height = large ? 60 : 32;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.BorderThickness = new Thickness(1);
        Material.Paint(button, BackgroundProperty, "SurfaceContainerLowest");
        if (!large)
        {
            button.FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal;
            return;
        }
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        double sample = command.Id switch { "style-title" => 20, "style-heading" => 16, "style-heading2" => 15, _ => 14 };
        panel.Children.Add(new TextBlock
        {
            Text = command.Id == "style-title" ? "Aa" : "AaBb", FontSize = sample,
            FontWeight = heading && command.Id != "style-title" ? FontWeights.SemiBold : FontWeights.Normal
        });
        var name = new TextBlock { Text = command.Label, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
        Material.Paint(name, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        panel.Children.Add(name);
        button.Content = panel;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
    }

    private void Invoke(string id)
    {
        ClosePopup();
        if (canExecute(id))
            execute(id);
    }

    private static Button Button(string label, string icon)
    {
        var button = icon.Length == 0 ? Material.Button(label, "ToolbarButton") : Material.IconButton(icon, label);
        if (icon.Length > 0)
            button.Content = Icon(icon);
        button.Focusable = false;
        return button;
    }

    internal static FrameworkElement Icon(string key)
    {
        if (!Icons.Value.TryGetValue(key, out var geometry))
            return Material.Icon(key);
        var canvas = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false };
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };
        Material.Paint(path, System.Windows.Shapes.Shape.StrokeProperty, "OnSurfaceVariant");
        canvas.Children.Add(path);
        return canvas;
    }

    private static UIElement IconAndLabel(string label, string icon)
    {
        var panel = new StackPanel();
        if (icon.Length > 0)
        {
            var glyph = Icon(icon);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(glyph);
        }
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, TextAlignment = TextAlignment.Center });
        return panel;
    }

    private static void AttachChip(DockPanel bar)
    {
        var line = Row();
        line.Children.Add(Icon("Lock"));
        line.Children.Add(new TextBlock { Text = "Read only", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var chip = new Border { Child = line, Height = 32, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 8, 0) };
        Material.Paint(chip, Border.BackgroundProperty, "SecondaryContainer");
        Material.Paint(chip, System.Windows.Documents.TextElement.ForegroundProperty, "OnSecondaryContainer");
        Right(bar, chip);
    }

    private static void Right(DockPanel parent, UIElement child)
    {
        DockPanel.SetDock(child, Dock.Right);
        parent.Children.Add(child);
    }

    private static DockPanel Bar(double height, string role)
    {
        var bar = new DockPanel { Height = height };
        Material.Paint(bar, Panel.BackgroundProperty, role);
        return bar;
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
    };

    private static Border Divider(double height)
    {
        var divider = new Border { Width = 1, Height = height, Margin = new Thickness(6, 0, 6, 0) };
        Material.Paint(divider, Border.BackgroundProperty, "OutlineVariant");
        return divider;
    }
}
