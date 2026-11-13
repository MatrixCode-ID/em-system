using System.Windows.Media;
using Em.Controls.Grid;
using Em.Documents.Model;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridCellPresentationRulesTests
{
    [Fact]
    public async Task DisplayText_changes_shown_text_only_and_reaches_exports()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var rows = new[] { new SelectionRow("Alpha", 5), new SelectionRow("Bravo", 0), new SelectionRow("Charlie", 3) };
            var grid = new MatrixGrid { ItemsSource = rows, AutoGenerateColumns = false };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text));
            grid.Columns.Add(new GridColumn("Count", "Count", GridValueKind.Number));
            GridCellInfo? seen = null;
            grid.CellPresentations.Add(new GridCellPresentation
            {
                ColumnKey = "Count",
                DisplayText = info => info.Item is SelectionRow { Count: 0 } ? "—" : null,
                Opacity = info => info.Item is SelectionRow { Count: 0 } ? 0.55 : 1,
                Foreground = info => { seen = info; return Brushes.Gray; },
                ToolTip = info => info.Item is SelectionRow { Count: 0 } ? "Not available" : null
            });
            grid.Query = grid.Query with
            {
                Sort = [new SortDescriptor("Count")],
                Summaries = [new SummaryDescriptor("Count", SummaryOperation.Sum, SummaryScope.Total)]
            };
            var window = GridSelectionTests.Show(grid);
            try
            {
                await GridSelectionTests.Idle();
                // Sorting uses the value: the zero row comes first although it shows a dash.
                var first = grid.VisibleRows[0];
                Assert.Same(rows[1], grid.GetRowItem(first.Key));
                Assert.Equal("—", grid.GetDisplayText(first.Key, "Count"));
                Assert.Equal("5", grid.GetDisplayText(grid.VisibleRows[2].Key, "Count"));
                Assert.Equal(0m, first.Values.GetValueOrDefault("Count").Number);
                Assert.Contains(Descendants<System.Windows.Controls.TextBlock>(grid), label => label.Text == "Sum = 8");
                Assert.NotNull(seen);
                Assert.IsType<SelectionRow>(seen!.Item);

                var document = grid.CreateDocument();
                var table = document.Sections[0].Blocks.OfType<DocumentTable>().Single();
                var texts = new List<string?>();
                await foreach (var row in table.OpenRows(CancellationToken.None))
                    texts.Add(row.Cells.Length > 1 ? row.Cells[1].DisplayText ?? row.Cells[1].Value?.ToString() : null);
                Assert.Contains("—", texts);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject node) where T : System.Windows.DependencyObject
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
}
