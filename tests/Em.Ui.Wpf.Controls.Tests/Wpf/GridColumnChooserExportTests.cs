using System.IO;
using System.IO.Compression;
using Em.Controls.Grid;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridColumnChooserExportTests
{
    [Fact]
    public async Task Columns_outside_the_chooser_keep_their_visibility_and_export_writes_to_a_path()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var rows = new[] { new SelectionRow("Alpha", 1), new SelectionRow("Bravo", 2) };
            var grid = new MatrixGrid { ItemsSource = rows, AutoGenerateColumns = false };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text));
            grid.Columns.Add(new GridColumn("Count", "Count", GridValueKind.Number)
            {
                IsVisible = false, ShowInColumnChooser = false, SearchWhenHidden = true
            });
            var window = GridSelectionTests.Show(grid);
            var folder = Path.Combine(Path.GetTempPath(), "matrixgrid-tests", Guid.NewGuid().ToString("N"));
            try
            {
                await GridSelectionTests.Idle();
                var layout = grid.CaptureLayout();
                var tampered = layout with
                {
                    Columns = [.. layout.Columns.Select(column => column with { IsVisible = true })]
                };
                grid.RestoreLayout(tampered);
                Assert.False(grid.Columns.Single(column => column.Key == "Count").IsVisible);
                Assert.True(grid.Columns.Single(column => column.Key == "Name").IsVisible);

                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "rows.xlsx");
                await grid.ExportToFileAsync(path, "xlsx");
                Assert.True(File.Exists(path));
                using (var archive = ZipFile.OpenRead(path))
                    Assert.Contains(archive.Entries, entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
                Assert.Single(Directory.GetFiles(folder));
                await Assert.ThrowsAsync<ArgumentException>(() => grid.ExportToFileAsync(" ", "xlsx"));
            }
            finally
            {
                window.Close();
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        });
    }
}
