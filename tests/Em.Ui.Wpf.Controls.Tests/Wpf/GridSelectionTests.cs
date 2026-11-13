using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Ui.Wpf.Controls;
using Xunit;

namespace Em.Ui.Wpf.Controls.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class GridSelectionTests
{
    [Fact]
    public async Task SelectedItem_binds_two_way_and_survives_reload_sort_and_filter()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var a = new SelectionRow("Alpha", 3);
            var b = new SelectionRow("Bravo", 1);
            var c = new SelectionRow("Charlie", 2);
            var source = new ObservableCollection<SelectionRow> { a, b, c };
            var model = new SelectionModel { Selected = b };
            var grid = new MatrixGrid { ItemsSource = source, AutoGenerateColumns = false, DataContext = model };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text));
            grid.Columns.Add(new GridColumn("Count", "Count", GridValueKind.Number));
            grid.SetBinding(MatrixGrid.SelectedItemProperty, new Binding(nameof(SelectionModel.Selected)));
            var window = Show(grid);
            try
            {
                await Idle();
                // The value set before the rows loaded is applied once they are loaded.
                Assert.Same(b, grid.SelectedItem);
                Assert.Equal(grid.GetRowKey(b), grid.State.FocusedCell?.Row);

                grid.SelectCell(new CellAddress(grid.GetRowKey(c)!.Value, "Name"));
                Assert.Same(c, model.Selected);

                model.Selected = a;
                Assert.Contains(grid.GetRowKey(a)!.Value, grid.State.SelectedRows);
                Assert.Single(grid.State.SelectedRows);

                grid.SortBy("Count");
                await Idle();
                Assert.Same(a, grid.SelectedItem);

                // A property change rebuilds and re-sorts the rows; the selected object stays selected.
                a.Count = 0;
                await Idle();
                Assert.Same(a, grid.VisibleRows[0] is { } first ? grid.GetRowItem(first.Key) : null);
                Assert.Same(a, model.Selected);

                source.Remove(b);
                source.Add(new SelectionRow("Delta", 9));
                await Idle();
                Assert.Same(a, model.Selected);

                model.Selected = null;
                Assert.Empty(grid.State.SelectedRows);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SelectedItems_follow_multi_selection_and_host_changes()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var rows = Enumerable.Range(1, 5).Select(index => new SelectionRow($"Row {index}", index)).ToArray();
            var selected = new ObservableCollection<object>();
            var grid = new MatrixGrid
            {
                ItemsSource = rows, AutoGenerateColumns = false, SelectionMode = GridSelectionMode.Multiple,
                SelectedItems = selected
            };
            grid.Columns.Add(new GridColumn("Name", "Name", GridValueKind.Text));
            var window = Show(grid);
            try
            {
                await Idle();
                grid.SelectCell(new CellAddress(grid.GetRowKey(rows[1])!.Value, "Name"));
                grid.SelectCell(new CellAddress(grid.GetRowKey(rows[3])!.Value, "Name"), extendRange: true);
                Assert.Equal(new object[] { rows[1], rows[2], rows[3] }, selected);
                grid.SelectCell(new CellAddress(grid.GetRowKey(rows[2])!.Value, "Name"), toggle: true);
                Assert.Equal(new object[] { rows[1], rows[3] }, selected);

                selected.Add(rows[0]);
                Assert.Equal(3, grid.State.SelectedRows.Count);
                Assert.Contains(grid.GetRowKey(rows[0])!.Value, grid.State.SelectedRows);

                grid.SelectionMode = GridSelectionMode.Single;
                Assert.Single(grid.State.SelectedRows);
                Assert.Single(selected);
                grid.SelectCell(new CellAddress(grid.GetRowKey(rows[4])!.Value, "Name"), toggle: true);
                Assert.Equal(new object[] { rows[4] }, selected);
                Assert.Same(rows[4], grid.SelectedItem);
            }
            finally
            {
                window.Close();
            }
        });
    }

    internal static Window Show(FrameworkElement content)
    {
        var window = new Window { Content = content, Width = 520, Height = 300, ShowInTaskbar = false };
        GridTheme.SetMode(window, GridThemeMode.Light);
        window.Show();
        window.Activate();
        return window;
    }

    internal static async Task Idle()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private sealed class SelectionModel : INotifyPropertyChanged
    {
        private object? _selected;
        public event PropertyChangedEventHandler? PropertyChanged;

        public object? Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }
    }
}

internal sealed class SelectionRow(string name, int count) : INotifyPropertyChanged
{
    private int _count = count;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; } = name;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            OnChanged();
        }
    }

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
