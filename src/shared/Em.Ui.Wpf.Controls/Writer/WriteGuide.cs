using System.Windows;
using System.Windows.Media;

namespace Em.Ui.Wpf.Controls;

/// <summary>
/// The dotted line a word processor draws down the page while a ruler marker is held: it shows where the indent, tab
/// stop or margin being dragged will land in the text. It sits over the pages and never takes the mouse.
/// </summary>
internal sealed class WriteGuide : FrameworkElement
{
    private double? x;

    internal WriteGuide()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>Shows the line at <paramref name="position"/> (in this element's coordinates), or hides it with null.</summary>
    internal void Show(double? position)
    {
        if (x == position)
            return;
        x = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (x is not { } at || at < 0 || at > ActualWidth)
            return;
        var brush = TryFindResource("MatrixGrid.Brush.OnSurfaceVariant") as Brush ?? Brushes.Gray;
        var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dot };
        pen.Freeze();
        // Half a pixel keeps the one-pixel line sharp.
        double line = Math.Round(at) + .5;
        drawing.DrawLine(pen, new(line, 0), new(line, ActualHeight));
    }
}
