using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Editors;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridControlTests
{
    [Fact]
    public async Task Grid_binds_sorts_selects_and_updates_the_same_object_from_a_cell_editor()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var first = new Entry { Name = "Cedar", Count = 4 };
            var second = new Entry { Name = "Blue", Count = 2 };
            var model = new ObservableCollection<Entry> { first, second, new() { Name = "Orchid", Count = 6 } };
            var grid = new MatrixGrid { ItemsSource = model, AutoGenerateColumns = false };
            grid.Columns.Add(new("Name", "Name", GridValueKind.Text) { Editor = new TextSettings { MaxLength = 10 } });
            grid.Columns.Add(new("Count", "Count", GridValueKind.Number)
            {
                Editor = new NumericSettings { Minimum = 1, Maximum = 10 }
            });
            var window = new Window { Content = grid, Width = 440, Height = 260, ShowInTaskbar = false };
            GridTheme.SetMode(window, GridThemeMode.Light);
            window.Show();
            window.Activate();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(3, grid.VisibleRows.Count);
                var blue = grid.VisibleRows[1].Key;
                grid.SortBy("Name");
                Assert.Equal(blue, grid.VisibleRows[0].Key);
                grid.SelectCell(new(blue, "Name"));
                grid.Focus();
                Press(grid, Key.Right);
                Assert.Equal(new CellAddress(blue, "Count"), grid.State.FocusedCell);
                Assert.True(grid.BeginEdit());
                var editor = Assert.IsType<Em.Ui.Wpf.Controls.SpinEditor>(Find<Em.Ui.Wpf.Controls.SpinEditor>(grid));
                var input = Assert.IsType<TextBox>(Find<TextBox>(editor));
                input.Text = "22";
                Assert.False(grid.CommitEdit());
                Assert.Equal(2, second.Count);
                Assert.NotNull(grid.EditingCell);
                input.Text = "7";
                Assert.True(grid.CommitEdit());
                Assert.Equal(7, second.Count);
                Assert.Null(grid.EditingCell);

                grid.Query = grid.Query with
                {
                    Filter = new FilterCondition("Count", FilterOperator.GreaterOrEqual, GridValue.From(6))
                };
                Assert.Equal(2, grid.VisibleRows.Count);
                Assert.Contains(blue, grid.State.SelectedRows);
                model.Add(new Entry { Name = "Pine", Count = 8 });
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(3, grid.VisibleRows.Count);
                Assert.True(grid.ResizeColumn("Count", 170));
                Assert.True(grid.MoveColumn("Count", "Name"));
                Assert.Equal("Count", grid.Columns[0].Key);

                GridTheme.SetMode(window, GridThemeMode.Dark);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(0x0c, Assert.IsType<SolidColorBrush>(grid.Background).Color.R);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (Find<T>(VisualTreeHelper.GetChild(root, index)) is { } result)
                return result;
        return null;
    }

    private static void Press(UIElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!,
            Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    public sealed class Entry
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
}
