using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using WpfGrid = System.Windows.Controls.Grid;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixGrid
{
    private Window CreateDialog(string title, UIElement content, double width) => Material.Dialog(this, title, content, width);

    private void OpenColumnChooser()
    {
        if (_surfaceHost is not { } host)
            return;
        if (_columnChooser is { } open)
        {
            open.Focus();
            return;
        }

        EnsureExplicitColumns();
        var initialOrder = Columns.Select(column => column.Key).ToArray();
        var search = new TextBox { Style = Material.Style("SearchField"), Margin = new Thickness(0, 12, 0, 8),
            ToolTip = "Search columns" };
        AutomationProperties.SetName(search, "Search columns");
        var list = new StackPanel();
        var reset = Material.Button("Reset", "OutlinedButton");
        var done = Material.Button("Done", "FilledButton");
        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(done, Dock.Right);
        actions.Children.Add(reset);
        actions.Children.Add(done);

        var body = new DockPanel();
        DockPanel.SetDock(actions, Dock.Bottom);
        body.Children.Add(actions);
        var heading = new TextBlock { Text = "Columns", FontSize = 22 };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(search, Dock.Top);
        body.Children.Add(heading);
        body.Children.Add(search);
        body.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var sheet = Material.Panel(body, "SurfaceContainerLow", 16, new Thickness(16));
        Material.Scope(sheet);
        sheet.Width = Math.Min(408, Math.Max(0, host.ActualWidth - 4));
        sheet.HorizontalAlignment = HorizontalAlignment.Right;
        sheet.Margin = new Thickness(0, 4, 4, 4);
        Panel.SetZIndex(sheet, 1);
        _columnChooser = search;
        host.Children.Add(sheet);
        host.SizeChanged += ResizeSheet;
        Columns.CollectionChanged += RefreshColumns;
        sheet.Unloaded += (_, _) =>
        {
            host.SizeChanged -= ResizeSheet;
            Columns.CollectionChanged -= RefreshColumns;
            if (ReferenceEquals(_columnChooser, search))
                _columnChooser = null;
        };
        sheet.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            CloseSheet();
            e.Handled = true;
        };
        search.TextChanged += (_, _) => Populate();
        done.Click += (_, _) => CloseSheet();
        reset.Click += (_, _) =>
        {
            _updatingColumns = true;
            try
            {
                for (int index = 0; index < initialOrder.Length; index++)
                {
                    int current = IndexOfColumn(initialOrder[index]);
                    if (current >= 0 && current != index && index < Columns.Count)
                        Columns.Move(current, index);
                }
                for (int index = 0; index < Columns.Count; index++)
                {
                    var column = Columns[index];
                    if (!column.IsVisible && column.ShowInColumnChooser)
                        Columns[index] = column with { IsVisible = true };
                }
            }
            finally
            {
                _updatingColumns = false;
                RebuildColumns();
                Populate();
            }
        };
        Populate();
        search.Focus();

        void ResizeSheet(object sender, SizeChangedEventArgs e) => sheet.Width = Math.Min(408, Math.Max(0, e.NewSize.Width - 4));
        void RefreshColumns(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!_updatingColumns)
                Populate();
        }
        void CloseSheet()
        {
            host.Children.Remove(sheet);
            Focus();
        }

        void Populate()
        {
            list.Children.Clear();
            foreach (var column in Columns.Where(column => column.ShowInColumnChooser
                && column.Header.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)))
            {
                var row = new DockPanel { Height = 44, Background = Brushes.Transparent, AllowDrop = true };
                var drag = Material.IconButton("DragIndicator", $"Drag {column.Header}");
                drag.Focusable = false;
                drag.PreviewMouseMove += (_, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed)
                        return;
                    var data = new DataObject(ColumnDragFormat, column.Key);
                    data.SetData(ColumnChooserDragFormat, column.Key);
                    DragDrop.DoDragDrop(drag, data, DragDropEffects.Move);
                };
                DockPanel.SetDock(drag, Dock.Left);
                row.Children.Add(drag);
                var hidden = new TextBlock { Text = "Hidden", FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Visibility = column.IsVisible ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(8, 0, 4, 0) };
                Material.Paint(hidden, TextBlock.ForegroundProperty, "OnSurfaceVariant");
                DockPanel.SetDock(hidden, Dock.Right);
                row.Children.Add(hidden);
                var check = new CheckBox { Content = column.Header, FontSize = 16, IsChecked = column.IsVisible };
                check.Click += (_, _) => SetColumnVisible(column.Key, check.IsChecked == true);
                row.Children.Add(check);
                row.DragOver += (_, e) =>
                {
                    e.Effects = e.Data.GetDataPresent(ColumnChooserDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;
                };
                row.Drop += (_, e) =>
                {
                    if (e.Data.GetData(ColumnChooserDragFormat) is string dragged)
                        e.Handled = MoveColumn(dragged, column.Key, e.GetPosition(row).Y > row.ActualHeight / 2);
                };
                list.Children.Add(row);
            }
        }
    }

    private void OpenSummaryEditor(bool groupFooter)
    {
        var columns = (Columns.Count > 0 ? Columns.ToArray() : _generatedColumns).Where(column => column.IsVisible).ToArray();
        var pending = Query.Summaries.ToHashSet();
        var operations = new[] { SummaryOperation.Count, SummaryOperation.Sum, SummaryOperation.Min,
            SummaryOperation.Max, SummaryOperation.Average };
        var controls = new Dictionary<SummaryDescriptor, List<CheckBox>>();
        var tabs = new TabControl();
        AddTab("Total footer", SummaryScope.Total);
        AddTab("Group footer", SummaryScope.Group);
        AddTab("Group row", SummaryScope.Group);
        tabs.SelectedIndex = groupFooter ? 1 : 0;
        var body = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Material.Button("Cancel");
        var ok = Material.Button("OK", "FilledButton");
        ok.IsDefault = true;
        cancel.IsCancel = true;
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        DockPanel.SetDock(actions, Dock.Bottom);
        body.Children.Add(actions);
        body.Children.Add(tabs);
        var dialog = CreateDialog("Customize summaries", body, 690);
        ok.Click += (_, _) =>
        {
            var current = Query.Summaries;
            var ordered = current.Where(pending.Contains).Concat(pending.Except(current)).ToImmutableArray();
            SetCurrentValue(QueryProperty, Query with { Summaries = ordered });
            dialog.DialogResult = true;
        };
        dialog.ShowDialog();

        void AddTab(string title, SummaryScope scope)
        {
            var table = new WpfGrid { Margin = new Thickness(0, 12, 0, 0) };
            table.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (var operation in operations)
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            AddRow(0, new TextBlock { Text = "Column", FontWeight = FontWeights.Medium });
            for (int index = 0; index < operations.Length; index++)
                Place(table, new TextBlock { Text = operations[index].ToString(), FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, 0, index + 1);
            for (int index = 0; index < columns.Length; index++)
            {
                var column = columns[index];
                AddRow(index + 1, new TextBlock { Text = column.Header, TextTrimming = TextTrimming.CharacterEllipsis });
                for (int op = 0; op < operations.Length; op++)
                {
                    var descriptor = new SummaryDescriptor(column.Key, operations[op], scope);
                    var check = new CheckBox { IsChecked = pending.Contains(descriptor), HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center, IsEnabled = SummaryRules.Allows(column.Kind, descriptor.Operation) };
                    AutomationProperties.SetName(check, $"{descriptor.Operation} of {column.Header}");
                    if (!controls.TryGetValue(descriptor, out var copies))
                        controls[descriptor] = copies = [];
                    copies.Add(check);
                    check.Click += (_, _) =>
                    {
                        if (check.IsChecked == true)
                            pending.Add(descriptor);
                        else
                            pending.Remove(descriptor);
                        foreach (var copy in copies)
                            copy.IsChecked = check.IsChecked;
                    };
                    Place(table, check, index + 1, op + 1);
                }
            }
            tabs.Items.Add(new TabItem { Header = title,
                Content = new ScrollViewer { Content = table, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

            void AddRow(int row, TextBlock caption)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
                caption.VerticalAlignment = VerticalAlignment.Center;
                Place(table, caption, row, 0);
            }
        }

        static void Place(WpfGrid table, FrameworkElement element, int row, int column)
        {
            WpfGrid.SetRow(element, row);
            WpfGrid.SetColumn(element, column);
            table.Children.Add(element);
        }
    }
}
