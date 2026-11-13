using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Grid;

public sealed partial class MatrixGrid
{
    private static readonly DependencyPropertyKey IsWaitingPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsWaiting), typeof(bool), typeof(MatrixGrid), new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="IsWaiting"/> dependency property.</summary>
    public static readonly DependencyProperty IsWaitingProperty = IsWaitingPropertyKey.DependencyProperty;

    /// <summary>Whether a grid operation is running and the built-in waiter is holding input.</summary>
    public bool IsWaiting => (bool)GetValue(IsWaitingProperty);

    private Control? _waiter;
    private ContextMenu? _openGridMenu;
    private CancellationTokenSource? _waitingLoad;

    private Control BuildWaiter()
    {
        var waiter = new Control
        {
            Style = Material.Style("GridWaiter"),
            Visibility = IsWaiting ? Visibility.Visible : Visibility.Collapsed
        };
        AutomationProperties.SetName(waiter, "Grid busy");
        AutomationProperties.SetAutomationId(waiter, "GridWaiter");
        Panel.SetZIndex(waiter, int.MaxValue);
        return waiter;
    }

    private void UpdateWaiter()
    {
        bool waiting = _waitingLoad is not null || _finderScan is not null || _matchNavigation is not null;
        if (IsWaiting == waiting)
            return;

        SetValue(IsWaitingPropertyKey, waiting);
        if (_waiter is { } waiter)
        {
            if (waiting)
                ClipWaiterAroundFinder();
            waiter.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!waiting)
            return;

        if (_openGridMenu is { } menu)
            menu.IsOpen = false;
        if (_columnFilterPopup is { } popup)
            popup.IsOpen = false;
        if (IsMouseCaptureWithin && !IsFinderInput(Mouse.Captured))
            Mouse.Capture(null);
    }

    /// <summary>
    /// The finder bar stays live while the waiter holds the grid, so the user can keep typing; each new query
    /// cancels the running search. The clip removes the bar from both the scrim and its hit testing.
    /// </summary>
    private void ClipWaiterAroundFinder()
    {
        if (_waiter is not { } waiter || _surfaceHost is not { } host)
            return;
        if (_searchBar is not { IsVisible: true } bar || host.ActualWidth <= 0 || host.ActualHeight <= 0)
        {
            waiter.Clip = null;
            return;
        }
        var finder = bar.TransformToAncestor(host).TransformBounds(new Rect(bar.RenderSize));
        waiter.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, host.ActualWidth, host.ActualHeight)), new RectangleGeometry(finder));
    }

    private bool IsFinderInput(object? source) =>
        source is DependencyObject element && _searchBar is { } bar
        && (ReferenceEquals(element, bar) || bar.IsAncestorOf(element));

    private bool HoldsInput(RoutedEventArgs e) => IsWaiting && !IsFinderInput(e.OriginalSource);

    private void TrackGridMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            if (IsWaiting)
                menu.IsOpen = false;
            else if (menu.IsOpen)
                _openGridMenu = menu;
        };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openGridMenu, menu))
                _openGridMenu = null;
        };
    }

    /// <summary>Blocks text input while the waiter holds input.</summary>
    /// <param name="e">The input event data.</param>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (HoldsInput(e))
            e.Handled = true;
        base.OnPreviewTextInput(e);
    }

    /// <summary>Blocks key input while the waiter holds input.</summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (HoldsInput(e))
            e.Handled = true;
        base.OnPreviewKeyUp(e);
    }

    /// <summary>Blocks scrolling while the waiter holds input.</summary>
    /// <param name="e">The wheel event data.</param>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (IsWaiting)
            e.Handled = true;
        base.OnPreviewMouseWheel(e);
    }

    /// <summary>Rejects drops while the waiter holds input.</summary>
    /// <param name="e">The drag event data.</param>
    protected override void OnPreviewDrop(DragEventArgs e)
    {
        if (IsWaiting)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        base.OnPreviewDrop(e);
    }

    /// <summary>Suppresses context menus while the waiter holds input.</summary>
    /// <param name="e">The context menu event data.</param>
    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        if (IsWaiting)
            e.Handled = true;
        base.OnContextMenuOpening(e);
    }
}
