using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Em.Grid.Menus;

namespace Em.Ui.Wpf.Documents.Grid;

/// <summary>A virtualized WPF data grid with sorting, filtering, search, grouping, summaries, in-place editing, layouts, context menus, print preview and export.</summary>
public sealed partial class MatrixGrid
{
    /// <summary>Creates the UI Automation peer that exposes the grid as a table.</summary>
    /// <returns>The automation peer.</returns>
    protected override AutomationPeer OnCreateAutomationPeer() => new GridPeer(this);

    private bool OpenFocusedMenu(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Apps && !(key == Key.F10 && modifiers.HasFlag(ModifierKeys.Shift)))
            return false;
        if (ContextMenu is not { } menu)
            return false;
        menu.Placement = PlacementMode.Center;
        menu.Closed += ResetMenuPlacement;
        menu.IsOpen = true;
        return true;
    }

    private static void ResetMenuPlacement(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.ContextMenu menu)
        {
            menu.Closed -= ResetMenuPlacement;
            menu.ClearValue(System.Windows.Controls.ContextMenu.PlacementProperty);
        }
    }

    private MenuHit FocusedMenuHit()
    {
        if (State.FocusedCell is not { } cell)
            return new MenuHit(MenuRegion.RowCell);

        int index = FindRow(cell.Row);
        var group = index >= 0 ? VisibleRows[index].Group : null;
        return group switch
        {
            { IsFooter: true } => new MenuHit(MenuRegion.GroupFooter, cell.ColumnKey, cell.Row.Value, group.Level),
            not null => new MenuHit(MenuRegion.GroupRow, group.ColumnKey, cell.Row.Value, group.Level),
            _ => new MenuHit(MenuRegion.RowCell, cell.ColumnKey, cell.Row.Value)
        };
    }

    private sealed class GridPeer(MatrixGrid grid) : FrameworkElementAutomationPeer(grid)
    {
        protected override string GetClassNameCore() => nameof(MatrixGrid);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;
        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? grid.DocumentTitle : name;
        }
    }
}
