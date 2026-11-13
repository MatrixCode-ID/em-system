using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Controls.Grid.Menus;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridOperationsTests
{
    [Fact]
    public async Task Group_menu_summaries_layout_and_search_work_in_a_real_window()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var grid = new MatrixGrid
            {
                ItemsSource = new[] { new Order("Blue Harbor", 4), new Order("Blue Harbor", 6), new Order("Orchid", 2) },
                ShowAutoFilter = true
            };
            var window = new Window { Content = grid, Width = 670, Height = 490, ShowInTaskbar = false };
            GridTheme.SetMode(window, GridThemeMode.Dark);
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var footer = Descendants<ScrollViewer>(grid).Single(view =>
                    System.Windows.Automation.AutomationProperties.GetName(view) == "Total summaries");
                var firstKey = grid.VisibleRows[0].Key;
                grid.SelectCell(new CellAddress(firstKey, "Name"));
                footer.ContextMenu.IsOpen = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var textItems = footer.ContextMenu.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header);
                Assert.False(textItems["Sum"].IsEnabled);
                Assert.False(textItems["Average"].IsEnabled);
                Assert.True(textItems["Min"].IsEnabled);
                Assert.True(textItems["Max"].IsEnabled);
                Assert.True(textItems["Count"].IsEnabled);
                Assert.True(textItems["None"].IsEnabled);
                footer.ContextMenu.IsOpen = false;

                grid.SelectCell(new CellAddress(firstKey, "Units"));
                footer.ContextMenu.IsOpen = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var numericItems = footer.ContextMenu.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(7, numericItems.Length);
                Assert.Contains(numericItems, item => Equals(item.Header, "Customize…"));
                Assert.All(numericItems, item => Assert.True(item.IsEnabled));
                footer.ContextMenu.IsOpen = false;

                Assert.True(grid.ResizeColumn("Name", 44));
                Assert.True(grid.ResizeColumn("Units", 44));
                Assert.True(grid.BestFitColumn("Name"));
                var single = grid.CaptureLayout();
                Assert.True(single.Columns.Single(column => column.Key == "Name").Width >= 80);
                Assert.Equal(44d, single.Columns.Single(column => column.Key == "Units").Width);

                var header = Descendants<Button>(grid).First(button =>
                    System.Windows.Automation.AutomationProperties.GetName(button) == "Name");
                MenuHit? hit = null;
                grid.MenuOpening += (_, opening) => { hit = opening.Hit; };
                header.ContextMenu.IsOpen = true;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(MenuRegion.ColumnHeader, hit!.Region);
                Assert.Equal("Name", hit.ColumnKey);
                var fitAll = header.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Best fit (all columns)"));
                var groupAction = header.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Group by this column"));
                fitAll.Command.Execute(fitAll.CommandParameter);
                grid.SetColumnFilter("Units", new FilterCondition("Units", FilterOperator.Greater, GridValue.From(0)));
                grid.UpdateLayout();
                var unitsHeader = Descendants<Button>(grid).Single(button =>
                    System.Windows.Automation.AutomationProperties.GetName(button) == "Units");
                var unitsLabel = Descendants<TextBlock>(unitsHeader).Single(label => label.Text == "Units");
                var required = new FormattedText(unitsLabel.Text, CultureInfo.CurrentCulture, unitsLabel.FlowDirection,
                    new Typeface(unitsLabel.FontFamily, unitsLabel.FontStyle, unitsLabel.FontWeight, unitsLabel.FontStretch),
                    unitsLabel.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(unitsLabel).PixelsPerDip).WidthIncludingTrailingWhitespace;
                Assert.True(unitsLabel.ActualWidth >= required);
                grid.SetColumnFilter("Units", null);
                groupAction.Command.Execute(groupAction.CommandParameter);
                Assert.Single(grid.Query.Groups);
                Assert.Contains(grid.VisibleRows, row => row.Group is { IsFooter: false, Count: 2 });

                grid.ToggleSummary("Units", SummaryScope.Total, SummaryOperation.Sum);
                grid.ToggleSummary("Units", SummaryScope.Group, SummaryOperation.Average);
                grid.ToggleSummary("Units", SummaryScope.Group, SummaryOperation.Count);
                Assert.Contains(grid.VisibleRows, row => row.Group is { IsFooter: true } && row.Summaries.Count == 2);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Contains(Descendants<TextBlock>(grid), label => label.Text.Contains("Sum = 12"));

                grid.ExpandGroups(false);
                Assert.DoesNotContain(grid.VisibleRows, row => row.Group is null);
                var collapsed = Assert.IsType<GroupRowInfo>(grid.VisibleRows[0].Group);
                grid.ToggleGroup(collapsed);
                Assert.Contains(grid.VisibleRows, row => row.Group is null);

                grid.SetColumnFilter("Units", new FilterCondition("Units", FilterOperator.Greater, GridValue.From(4)));
                Assert.Equal(1, grid.ResultRowCount);
                grid.Query = grid.Query with { Search = new SearchCriteria("Units:6", ["Units"], FilterRows: false) };
                Assert.True(grid.FindNext());
                var layout = GridLayout.Load(grid.CaptureLayout().Save());
                grid.Query = GridQuery.Empty;
                grid.RestoreLayout(layout);
                Assert.Equal(1, grid.ResultRowCount);
                Assert.Single(grid.Query.Groups);
                Assert.True(grid.ShowAutoFilter);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Finder_counts_each_visible_matching_cell_and_wraps_without_changing_selection()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var grid = new MatrixGrid
            {
                ItemsSource = new[]
                {
                    new FindRow("North North", "North", "North", 6),
                    new FindRow("West", "Open", "North", 9),
                    new FindRow("North", "North", "North", 1)
                }
            };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text));
            grid.Columns.Add(new GridColumn("Status", "Status", GridValueKind.Text));
            grid.Columns.Add(new GridColumn("Hidden", "Hidden", GridValueKind.Text) { IsVisible = false });
            grid.Columns.Add(new GridColumn("Units", "Units", GridValueKind.Number));
            var window = new Window { Content = grid, Width = 550, Height = 360, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                grid.SetColumnFilter("Units", new FilterCondition("Units", FilterOperator.Greater, GridValue.From(4)));
                grid.SelectCell(new CellAddress(grid.VisibleRows[1].Key, "Name"));
                var selected = grid.State.SelectedRows;
                var focused = grid.State.FocusedCell;
                var filter = grid.Query.Filter;

                grid.RestoreLayout(grid.CaptureLayout() with
                {
                    ShowSearch = true,
                    FinderSearch = new SearchCriteria("North", ["Name", "Status"], FilterRows: false)
                });
                var counter = Descendants<TextBlock>(grid).Single(text =>
                    AutomationProperties.GetAutomationId(text) == "GridSearchMatches");
                var previous = Descendants<Button>(grid).Single(button =>
                    AutomationProperties.GetName(button) == "Previous match");
                var next = Descendants<Button>(grid).Single(button =>
                    AutomationProperties.GetName(button) == "Next match");
                Assert.Equal("1 / 2", counter.Text);
                Assert.True(previous.IsEnabled && next.IsEnabled);
                previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("2 / 2", counter.Text);
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("1 / 2", counter.Text);
                Assert.Equal(focused, grid.State.FocusedCell);
                Assert.True(selected.SetEquals(grid.State.SelectedRows));
                Assert.Equal(filter, grid.Query.Filter);
                Assert.Equal(2, grid.ResultRowCount);
            }
            finally { window.Close(); }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match)
            yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i)))
                yield return child;
    }

    public sealed record Order(string Name, int Units);
    public sealed record FindRow(string Name, string Status, string Hidden, int Units);
}
