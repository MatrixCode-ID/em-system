using System.Windows;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class VirtualGridTests
{
    [Fact]
    public async Task Highlight_navigation_finds_a_match_outside_the_current_virtual_window()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var provider = new GeneratedOrderProvider(600);
            var grid = new MatrixGrid
            {
                Provider = provider,
                Query = new GridQuery { Search = new SearchCriteria("SO-0000000400", ["Order"], FilterRows: false) }
            };
            foreach (var column in provider.Columns)
                grid.Columns.Add(column);
            var window = new Window { Width = 620, Height = 350, Content = grid, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await grid.GoToRowAsync(0);
                var match = new RowKey("SO-0000000400");
                Assert.DoesNotContain(grid.VisibleRows, row => row.Key == match);
                Assert.True(await grid.FindNextAsync());
                Assert.Equal(new CellAddress(match, "Order"), grid.State.FocusedCell);
                Assert.InRange(grid.VisibleRowOffset, 380, 399);
                await grid.GoToRowAsync(500);
                Assert.True(await grid.FindNextAsync(previous: true));
                Assert.Equal(new CellAddress(match, "Order"), grid.State.FocusedCell);
                Assert.Equal(600, grid.TotalRowCount);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Million_row_view_fetches_only_a_bounded_window_and_realizes_visible_headers()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var provider = new GeneratedOrderProvider(1_000_000, 160);
            var grid = new MatrixGrid { Provider = provider };
            foreach (var column in provider.Columns)
                grid.Columns.Add(column);
            var window = new Window { Width = 480, Height = 290, Content = grid, ShowInTaskbar = false };
            GridTheme.SetMode(window, GridThemeMode.Dark);
            window.Show();
            try
            {
                await grid.GoToRowAsync(0);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(1_000_000, grid.TotalRowCount);
                Assert.InRange(grid.VisibleRows.Count, 1, 24);
                Assert.InRange(grid.RealizedColumnHeaderCount, 1, 12);

                var oldKey = grid.VisibleRows[0].Key;
                grid.SelectCell(new CellAddress(oldKey, "Order"));
                await grid.GoToRowAsync(900_000);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(900_000, grid.VisibleRowOffset);
                Assert.Equal("SO-0000900001", grid.VisibleRows[0].Key.Value);
                Assert.Contains(oldKey, grid.State.SelectedRows);
                Assert.InRange(grid.RealizedRowCount, 1, 24);

                grid.ScrollColumnIntoView("Metric140");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.InRange(grid.RealizedColumnHeaderCount, 1, 12);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
