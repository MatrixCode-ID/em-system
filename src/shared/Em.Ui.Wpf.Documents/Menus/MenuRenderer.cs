using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Em.Grid.Menus;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Menus;

/// <summary>Turns the renderer-neutral menu model into Material-styled WPF context menus.</summary>
public static class MenuRenderer
{
    [ThreadStatic] private static ResourceDictionary? _resources;

    /// <summary>Attaches a context menu that is rebuilt every time it opens.</summary>
    /// <param name="owner">The element that shows the menu.</param>
    /// <param name="createOpening">Creates the menu entries and lets handlers change them.</param>
    /// <param name="resolveCommand">Maps command identifiers to WPF commands.</param>
    /// <returns>The attached context menu.</returns>
    public static ContextMenu Attach(
        FrameworkElement owner,
        Func<MenuOpeningEventArgs> createOpening,
        Func<string, ICommand?> resolveCommand) => Attach(owner, createOpening, resolveCommand, finderOptions: false);

    internal static ContextMenu Attach(
        FrameworkElement owner,
        Func<MenuOpeningEventArgs> createOpening,
        Func<string, ICommand?> resolveCommand,
        bool finderOptions)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(createOpening);
        ArgumentNullException.ThrowIfNull(resolveCommand);
        var resources = _resources ??= Material.Load("Menu.xaml");
        var menu = new ContextMenu
        {
            PlacementTarget = owner, Style = (Style)resources[finderOptions ? "Material.FindOptionsMenu" : "Material.Menu"],
            MaxHeight = Math.Min(560, SystemParameters.WorkArea.Height - 20),
            FontFamily = (FontFamily)resources["MatrixGrid.FontFamily"]
        };
        owner.ContextMenu = menu;
        menu.Opened += (_, _) =>
        {
            var opening = createOpening();
            menu.Items.Clear();
            if (opening.Cancel || opening.Items.IsDefaultOrEmpty)
            {
                menu.IsOpen = false;
                return;
            }

            GridTheme.SetMode(menu, GridTheme.GetMode(owner));
            menu.Width = 16 + Populate(menu, opening.Items, opening.Hit, resolveCommand, owner,
                resources, new Typeface(menu.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), finderOptions);
        };
        return menu;
    }

    private static double Populate(ItemsControl parent, ImmutableArray<MenuEntry> entries, MenuHit hit,
        Func<string, ICommand?> resolveCommand, FrameworkElement owner, ResourceDictionary resources, Typeface font,
        bool finderOptions = false)
    {
        double neededWidth = 112;
        var rows = new List<FrameworkElement>();
        foreach (var entry in entries)
        {
            if (entry is MenuSeparator)
            {
                rows.Add(new Separator { Style = (Style)resources["Material.MenuDivider"] });
                continue;
            }

            if (entry is MenuSection section)
            {
                var heading = Label(section.Label, "OnSurfaceVariant");
                heading.FontSize = 12;
                heading.FontWeight = FontWeights.Medium;
                var sectionRow = new Separator { Style = (Style)resources["Material.MenuSection"], Tag = heading };
                neededWidth = Math.Max(neededWidth, 24 + Measure(section.Label, 12));
                rows.Add(sectionRow);
                continue;
            }

            var item = new MenuItem { Style = (Style)resources["Material.MenuItem"] };
            if (finderOptions)
                item.Height = 40;
            string label;
            string? icon;
            string? shortcut = null;
            bool isChecked = false;
            bool hasSubmenu = entry is MenuSubmenu;
            switch (entry)
            {
                case MenuCommand command:
                    label = command.Label;
                    icon = command.Icon;
                    shortcut = command.Shortcut;
                    isChecked = command.IsChecked;
                    item.InputGestureText = shortcut;
                    item.IsChecked = isChecked;
                    item.IsEnabled = command.IsEnabled;
                    item.Command = resolveCommand(command.CommandId);
                    item.CommandParameter = hit;
                    item.CommandTarget = owner;
                    AutomationProperties.SetAutomationId(item, command.CommandId);
                    break;
                case MenuSubmenu submenu:
                    label = submenu.Label;
                    icon = submenu.Icon;
                    Populate(item, submenu.Items, hit, resolveCommand, owner, resources, font);
                    break;
                default:
                    throw new ArgumentException("Unknown menu entry.", nameof(entries));
            }
            AutomationProperties.SetName(item, label);

            var row = new DockPanel();
            var slot = new Border { Width = 20, Margin = new Thickness(0, 0, 12, 0) };
            if (!string.IsNullOrEmpty(icon))
                slot.Child = Material.Icon(icon);
            else if (isChecked)
                slot.Child = Material.Icon("Check");
            DockPanel.SetDock(slot, Dock.Left);
            row.Children.Add(slot);

            double trailingWidth = 0;
            if (hasSubmenu || isChecked && !string.IsNullOrEmpty(icon))
            {
                AddTrailing(Material.Icon(hasSubmenu ? "ChevronRight" : "Check", 18));
                trailingWidth += 18;
            }
            if (!string.IsNullOrEmpty(shortcut))
            {
                AddTrailing(Label(shortcut, "OnSurfaceVariant"));
                trailingWidth += Measure(shortcut, 14);
            }

            var caption = Label(label, "OnSurface");
            caption.ToolTip = label;
            row.Children.Add(caption);
            item.Header = label;
            item.Tag = row;
            rows.Add(item);
            neededWidth = Math.Max(neededWidth, 56 + Measure(label, 14) + (trailingWidth > 0 ? 24 + trailingWidth : 0));

            void AddTrailing(FrameworkElement element)
            {
                DockPanel.SetDock(element, Dock.Right);
                element.Margin = new Thickness(trailingWidth == 0 ? 24 : 4, 0, 0, 0);
                row.Children.Add(element);
            }
        }

        double width = finderOptions ? 300 : Math.Clamp(Math.Ceiling(neededWidth / 8) * 8, 112, 400);
        foreach (var row in rows)
        {
            row.Width = width;
            parent.Items.Add(row);
        }
        return width;

        double Measure(string text, double size) => new FormattedText(text, CultureInfo.CurrentCulture, owner.FlowDirection,
            font, size, Brushes.Black, VisualTreeHelper.GetDpi(owner).PixelsPerDip).WidthIncludingTrailingWhitespace;
    }

    private static TextBlock Label(string text, string role)
    {
        var label = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Material.Paint(label, TextBlock.ForegroundProperty, role);
        return label;
    }
}
