using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Em.Controls.Grid;
using Em.Editors;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

/// <summary>Behaviour a host application relies on when it replaces another grid with MatrixGrid.</summary>
[Collection("WPF desktop")]
public sealed class GridHostIntegrationTests
{
    [Fact]
    public void Whole_number_settings_reject_fractions_and_format_without_decimals()
    {
        var settings = new NumericSettings
        {
            Minimum = 0, Maximum = 999999, AllowDecimals = false, DisplayFormat = "n0",
            Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US")
        };
        Assert.False(settings.Parse("1.5", typeof(int)).IsValid);
        Assert.False(settings.Parse("1000000", typeof(int)).IsValid);
        Assert.False(settings.Parse("-1", typeof(int)).IsValid);
        Assert.True(settings.Parse("1200", typeof(int)).IsValid);
        Assert.Equal("1,200", settings.Format(1200m));
    }

    [Fact]
    public async Task Summaries_use_their_format_count_text_and_in_cell_spin_editor_is_centred()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var rows = new ObservableCollection<SelectionRow> { new("Alpha", 1200), new("Bravo", 0), new("Charlie", 30) };
            var grid = new MatrixGrid { ItemsSource = rows, AutoGenerateColumns = false };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text) { IsReadOnly = true });
            grid.Columns.Add(new GridColumn("Count", "Count", GridValueKind.Number)
            {
                Alignment = GridColumnAlignment.Center,
                SummaryFormat = "#,##0;-#,##0;''",
                Editor = new NumericSettings
                {
                    Minimum = 0, Maximum = 999999, AllowDecimals = false, DisplayFormat = "#,##0;-#,##0;''",
                    Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US")
                }
            });
            grid.Query = grid.Query with
            {
                Summaries =
                [
                    new SummaryDescriptor("Name", SummaryOperation.Count, SummaryScope.Total),
                    new SummaryDescriptor("Count", SummaryOperation.Sum, SummaryScope.Total)
                ]
            };
            var window = GridSelectionTests.Show(grid);
            try
            {
                await GridSelectionTests.Idle();
                var labels = Descendants<TextBlock>(grid).Select(label => label.Text).ToArray();
                Assert.Contains("Count = 3", labels);
                Assert.Contains("Sum = 1,230", labels);
                // Zero shows empty through the section format; the value stays numeric.
                var zero = grid.VisibleRows.Single(row => grid.GetRowItem(row.Key) == rows[1]);
                Assert.Equal("", grid.GetDisplayText(zero.Key, "Count"));

                grid.BeginningEdit += (_, args) => args.Cancel = args.Item == rows[0];
                grid.SelectCell(new CellAddress(grid.GetRowKey(rows[0])!.Value, "Count"));
                Assert.False(grid.BeginEdit());
                grid.SelectCell(new CellAddress(zero.Key, "Count"));
                Assert.True(grid.BeginEdit());
                var editor = Descendants<Control>(grid).First(control => control is Em.Ui.Wpf.Controls.SpinEditor);
                Assert.Equal(HorizontalAlignment.Center, editor.HorizontalContentAlignment);
                grid.CancelEdit();
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task External_drops_reach_the_host_and_changed_objects_are_sorted_again()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var rows = new ObservableCollection<SelectionRow> { new("Alpha", 1), new("Bravo", 2), new("Charlie", 3) };
            var grid = new MatrixGrid { ItemsSource = rows, AutoGenerateColumns = false, AllowDrop = true };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text));
            grid.Columns.Add(new GridColumn("Count", "Count", GridValueKind.Number));
            grid.Query = grid.Query with { Sort = [new SortDescriptor("Count", true)] };
            int hostDragOver = 0;
            grid.DragOver += (_, _) => hostDragOver++;
            var window = GridSelectionTests.Show(grid);
            try
            {
                await GridSelectionTests.Idle();
                Assert.Same(rows[2], grid.GetRowItem(grid.VisibleRows[0].Key));
                rows[0].Count = 10;
                await GridSelectionTests.Idle();
                Assert.Same(rows[0], grid.GetRowItem(grid.VisibleRows[0].Key));

                // An external payload over the column header and the group panel bubbles to the grid's own handlers.
                var data = new DataObject("Host.Shoes", "model");
                foreach (var target in new FrameworkElement[]
                {
                    Descendants<Button>(grid).First(button => AutomationName(button) == "Count"),
                    Descendants<Border>(grid).First(border => AutomationName(border) == "Grouping panel")
                })
                {
                    var args = CreateDragEvent(data, target);
                    target.RaiseEvent(args);
                    Assert.False(args.Handled);
                }
                Assert.Equal(2, hostDragOver);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static string AutomationName(DependencyObject element) =>
        System.Windows.Automation.AutomationProperties.GetName(element);

    private static DragEventArgs CreateDragEvent(IDataObject data, UIElement target)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic).Single();
        var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, target, new Point(4, 4)]);
        args.RoutedEvent = DragDrop.DragOverEvent;
        return args;
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
}
