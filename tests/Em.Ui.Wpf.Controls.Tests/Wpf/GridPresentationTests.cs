using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridPresentationTests
{
    [Fact]
    public async Task Frozen_header_and_template_stay_at_viewport_edge_and_details_follow_row_identity()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var grid = new MatrixGrid { AutoGenerateColumns = false, ItemsSource = new[] { new { Name = "Orchid" } } };
            grid.Columns.Add(new("Name", "Frozen name", GridValueKind.Text) { IsFrozen = true, Band = "Order", Width = 140 });
            for (int i = 0; i < 16; i++)
                grid.Columns.Add(new($"c{i}", $"Field {i}", GridValueKind.Text) { Band = "Fields", Width = 128 });

            grid.CellPresentations.Add(new() { ColumnKey = "Name", Template = Label("Text") });
            grid.RowDetailsTemplate = Label("Key.Value");
            var window = new Window { Content = grid, Width = 630, Height = 500, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var header = Descendants<Button>(grid).Single(button => AutomationProperties.GetName(button) == "Frozen name");
                double x = header.TranslatePoint(new Point(), grid).X;
                Assert.Contains(Descendants<TextBlock>(grid), label => label.Text == "Order");
                grid.ScrollColumnIntoView("c12");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(x, header.TranslatePoint(new Point(), grid).X, 2);
                Assert.Contains(Descendants<ContentPresenter>(grid), view => view.Content is GridCellInfo { Text: "Orchid" });

                var key = Assert.Single(grid.VisibleRows).Key;
                Assert.True(grid.ToggleRowDetails(key));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Contains(Descendants<ContentPresenter>(grid), view => view.Content is Em.Controls.Grid.Data.GridRow row && row.Key == key);
                Assert.True(grid.ToggleRowDetails(key));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.DoesNotContain(Descendants<ContentPresenter>(grid), view => view.Content is Em.Controls.Grid.Data.GridRow && view.ContentTemplate == grid.RowDetailsTemplate);

                var layout = GridLayout.Load(grid.CaptureLayout().Save());
                Assert.True(grid.SetColumnFrozen("Name", false));
                grid.RestoreLayout(layout);
                Assert.True(grid.Columns[0].IsFrozen);
                Assert.Equal("Order", grid.Columns[0].Band);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Narrow_grouped_rows_and_headers_align_to_the_left_and_fill_the_viewport()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var grid = new MatrixGrid { ItemsSource = new[] { new { Name = "Blue", Count = 4 } } };
            var window = new Window { Content = grid, Width = 820, Height = 520, ShowInTaskbar = false };
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                grid.GroupBy("Name");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var group = Descendants<Canvas>(grid).First(row =>
                    row.DataContext is Em.Controls.Grid.Data.GridRow { Group.IsFooter: false });
                var header = Descendants<Button>(grid).First(button => AutomationProperties.GetName(button) == "Count");
                Assert.InRange(group.TranslatePoint(new Point(), grid).X, 0, 2);
                Assert.InRange(group.ActualWidth, grid.ActualWidth - 24, grid.ActualWidth);
                Assert.InRange(header.TranslatePoint(new Point(), grid).X, 24, 28);
            }
            finally { window.Close(); }
        });
    }

    private static DataTemplate Label(string path)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(path));
        return new DataTemplate { VisualTree = label };
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match)
            yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i)))
                yield return child;
    }
}
