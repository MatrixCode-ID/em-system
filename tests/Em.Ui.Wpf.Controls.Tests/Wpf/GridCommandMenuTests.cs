using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Controls.Grid.Menus;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridCommandMenuTests
{
    [Fact]
    public async Task Row_menu_commands_receive_the_row_object_and_follow_CanExecute()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var rows = new[] { new SelectionRow("Alpha", 1), new SelectionRow("Bravo", 2) };
            object? opened = null;
            var open = new TestCommand(parameter => opened = parameter, _ => true);
            var delete = new TestCommand(_ => { }, parameter => parameter is SelectionRow { Count: > 1 });
            var grid = new MatrixGrid { ItemsSource = rows, AutoGenerateColumns = false };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text) { IsReadOnly = true });
            grid.MenuDefinitions.Add(new GridMenuDefinition
            {
                Region = MenuRegion.RowCell,
                Mode = GridMenuMode.Replace,
                Items =
                {
                    new GridCommandItem { Label = "Open", Icon = "Edit", Command = open, Id = "open" },
                    new MenuSeparator(),
                    new GridCommandItem { Label = "Delete", Command = delete }
                }
            });
            object? menuRow = null;
            grid.MenuOpening += (_, args) => menuRow = args.RowItem;
            object? activated = null;
            grid.RowDoubleClickCommand = new TestCommand(parameter => activated = parameter, _ => true);
            var window = GridSelectionTests.Show(grid);
            try
            {
                await GridSelectionTests.Idle();
                var rowViews = Descendants<FrameworkElement>(grid)
                    .Where(element => element.DataContext is GridRow && element.ContextMenu is not null).ToArray();
                var alphaView = rowViews.Single(view => grid.GetRowItem(((GridRow)view.DataContext).Key) == rows[0]);
                var bravoView = rowViews.Single(view => grid.GetRowItem(((GridRow)view.DataContext).Key) == rows[1]);

                alphaView.ContextMenu.IsOpen = true;
                await GridSelectionTests.Idle();
                Assert.Same(rows[0], menuRow);
                var items = alphaView.ContextMenu.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header);
                Assert.Equal(2, items.Count);
                Assert.True(items["Open"].IsEnabled);
                Assert.False(items["Delete"].IsEnabled);
                items["Open"].Command.Execute(items["Open"].CommandParameter);
                Assert.Same(rows[0], opened);
                alphaView.ContextMenu.IsOpen = false;

                bravoView.ContextMenu.IsOpen = true;
                await GridSelectionTests.Idle();
                items = bravoView.ContextMenu.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header);
                Assert.True(items["Delete"].IsEnabled);
                bravoView.ContextMenu.IsOpen = false;

                grid.ActivateRowForTest(grid.GetRowKey(rows[1])!.Value);
                Assert.Same(rows[1], activated);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private sealed class TestCommand(Action<object?> execute, Func<object?, bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
    }
}

internal static class GridTestExtensions
{
    /// <summary>Runs the row double-click command the way a double-click on a read-only cell does.</summary>
    public static void ActivateRowForTest(this MatrixGrid grid, RowKey key)
    {
        var item = grid.GetRowItem(key);
        if (grid.RowDoubleClickCommand is { } command && command.CanExecute(item))
            command.Execute(item);
    }
}
