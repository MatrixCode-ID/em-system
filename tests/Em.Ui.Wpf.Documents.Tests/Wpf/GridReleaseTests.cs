using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Grid;
using Em.Editors;
using Em.Grid.Menus;
using Em.Ui.Wpf.Documents.Editors;
using Em.Ui.Wpf.Documents.Grid;
using Xunit;

namespace Em.Ui.Wpf.Documents.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridReleaseTests
{
    [Fact]
    public async Task Six_editors_commit_through_grid_and_keep_source_subscription_scoped_to_window()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var entry = new Order();
            var source = new ObservedList(entry);
            var grid = new MatrixGrid { ItemsSource = source, AutoGenerateColumns = false };
            var options = new[] { new EditorOption("east", "East"), new EditorOption("west", "West") };
            grid.Columns.Add(new("Name", "Name", GridValueKind.Text) { Editor = new TextSettings { MaxLength = 8 } });
            grid.Columns.Add(new("Units", "Units", GridValueKind.Number) { Editor = new NumericSettings { Minimum = 1, Maximum = 50 } });
            grid.Columns.Add(new("Priority", "Priority", GridValueKind.Boolean));
            grid.Columns.Add(new("Date", "Date", GridValueKind.Date) { Editor = new DateSettings { EditFormat = "yyyy-MM-dd" } });
            grid.Columns.Add(new("Method", "Method", GridValueKind.Text) { Editor = new ComboSettings { Options = [.. options] } });
            grid.Columns.Add(new("Warehouse", "Warehouse", GridValueKind.Text) { Editor = new LookupSettings { Options = [.. options] } });
            var window = new Window { Content = grid, Width = 1020, Height = 460, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(1, source.Subscribers);
                var row = Assert.Single(grid.VisibleRows).Key;
                await Edit<TextEditor>("Name", "Cedar");
                await Edit<SpinEditor>("Units", 4m);
                await Edit<CheckEditor>("Priority", true);
                await Edit<DateEditor>("Date", new DateOnly(2026, 11, 12));
                await Edit<ComboEditor>("Method", "west");
                await Edit<LookupEditor>("Warehouse", "west");
                Assert.Equal(("Cedar", 4, true, new DateOnly(2026, 11, 12), "west", "west"),
                    (entry.Name, entry.Units, entry.Priority, entry.Date, entry.Method, entry.Warehouse));

                grid.SelectCell(new CellAddress(row, "Priority"));
                Assert.True(grid.BeginEdit());
                var check = Assert.Single(Descendants<CheckBox>(Assert.Single(Descendants<CheckEditor>(grid))));
                Assert.False(check.IsThreeState);
                Assert.True(check.IsChecked);
                check.IsChecked = false;
                check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.False(entry.Priority);
                Assert.Null(grid.EditingCell);

                async Task Edit<T>(string column, object value) where T : class, IGridEditor
                {
                    grid.SelectCell(new CellAddress(row, column));
                    Assert.True(grid.BeginEdit());
                    var editor = Assert.Single(Descendants<T>(grid));
                    editor.EditValue = value;
                    Assert.True(grid.CommitEdit(), editor.Error);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Assert.Null(grid.EditingCell);
                }
            }
            finally { window.Close(); }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(0, source.Subscribers);
            Assert.Equal(0, grid.RealizedRowCount);
        });
    }

    [Fact]
    public async Task Body_menu_key_uses_focused_group_and_header_enter_remains_available()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var grid = new MatrixGrid { ItemsSource = new[] { new Order() }, DocumentTitle = "Keyboard test" };
            var window = new Window { Content = grid, Width = 900, Height = 460, ShowInTaskbar = false };
            window.Show();
            window.Activate();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
                Assert.Equal(AutomationControlType.DataGrid, peer.GetAutomationControlType());
                Assert.Equal("Keyboard test", peer.GetName());
                var header = Descendants<Button>(grid).First(button => AutomationProperties.GetName(button) == "Name");
                Assert.False(Press(header, Key.Enter).Handled);
                grid.GroupBy("Name");
                var group = grid.VisibleRows.First(row => row.Group is { IsFooter: false });
                grid.SelectCell(new CellAddress(group.Key, "Units"));
                grid.Focus();
                MenuHit? hit = null;
                grid.MenuOpening += (_, e) => hit = e.Hit;
                Assert.True(Press(grid, Key.Apps).Handled);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(MenuRegion.GroupRow, hit?.Region);
                Assert.Equal(group.Key.Value, hit?.RowKey);
                grid.ContextMenu.IsOpen = false;
                Assert.True(Press(grid, Key.Enter).Handled);
                Assert.DoesNotContain(grid.VisibleRows, row => row.Group is null);
            }
            finally { window.Close(); }
        });
    }

    private static KeyEventArgs Press(UIElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element)!, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        element.RaiseEvent(args);
        return args;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : class
    {
        if (node is T match)
            yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i)))
                yield return child;
    }

    public sealed class Order
    {
        public string Name { get; set; } = "Blue";
        public int Units { get; set; } = 2;
        public bool Priority { get; set; }
        public DateOnly Date { get; set; } = new(2026, 11, 1);
        public string Method { get; set; } = "east";
        public string Warehouse { get; set; } = "east";
    }

    private sealed class ObservedList(Order entry) : IEnumerable, INotifyCollectionChanged
    {
        private NotifyCollectionChangedEventHandler? _handlers;
        public int Subscribers => _handlers?.GetInvocationList().Length ?? 0;
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add => _handlers += value;
            remove => _handlers -= value;
        }

        public IEnumerator GetEnumerator() => new[] { entry }.GetEnumerator();
    }
}
