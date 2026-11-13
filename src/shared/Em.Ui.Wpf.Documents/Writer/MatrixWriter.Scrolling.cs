// Ctrl+wheel zoom follows WordCraft (crates/ui-egui/src/scroll.rs, canvas.rs), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Em.Write.Layout;

namespace Em.Ui.Wpf.Documents.Writer;

public sealed partial class MatrixWriter
{
    internal const double MinZoom = .1, MaxZoom = 5;
    // Natural logarithm of the zoom factor of one wheel notch: two steps of about 10%.
    private const double NotchZoom = .2;
    private const double PageGap = 24;

    private double? zoomTarget;
    private Point zoomAnchor;
    private TimeSpan? lastFrame;
    private bool animating;
    private bool revealCaret;

    /// <summary>Sets the zoom (0.1 to 5) at once, keeping the middle of the view in place.</summary>
    public void SetZoom(double value)
    {
        zoomTarget = null;
        StopAnimation();
        ZoomAround(value, ViewCenter());
        RefreshToolbar();
    }

    private Point ViewCenter() => scroll is null ? new() : new(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2);

    private void OnWheelZoom(object sender, MouseWheelEventArgs args)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || scroll is null)
            return;
        args.Handled = true;
        double target = Math.Clamp((zoomTarget ?? zoom) * Math.Exp(NotchZoom * args.Delta / 120.0), MinZoom, MaxZoom);
        zoomAnchor = args.GetPosition(scroll);
        zoomTarget = target;
        if (!animating)
        {
            animating = true;
            lastFrame = null;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        var now = (args as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        if (lastFrame == now)
            return;
        double dt = lastFrame is { } previous ? Math.Clamp((now - previous).TotalSeconds, 0, .1) : 1 / 60.0;
        lastFrame = now;
        if (zoomTarget is not { } target || !IsVisible)
        {
            if (zoomTarget is { } pending)
                ZoomAround(pending, zoomAnchor);
            zoomTarget = null;
            StopAnimation();
            RefreshToolbar();
            return;
        }
        // About 90% of the remaining ratio in 0.1 s, so zooming in and out feel alike.
        double next = zoom * Math.Pow(target / zoom, 1 - Math.Pow(.1, dt / .1));
        if (Math.Abs(next - target) < .002)
        {
            zoomTarget = null;
            ZoomAround(target, zoomAnchor);
            StopAnimation();
            RefreshToolbar();
            return;
        }
        ZoomAround(next, zoomAnchor);
    }

    private void StopAnimation()
    {
        if (!animating)
            return;
        animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>Changes the zoom so that the document point under <paramref name="anchor"/> (view coordinates) stays there.</summary>
    private void ZoomAround(double value, Point anchor)
    {
        if (!double.IsFinite(value))
            return;
        value = Math.Clamp(value, MinZoom, MaxZoom);
        if (scroll is null || surface is null || pages.Length == 0)
        {
            zoom = value;
            Refresh();
            return;
        }
        double old = zoom;
        double contentY = scroll.VerticalOffset + anchor.Y, contentX = scroll.HorizontalOffset + anchor.X;
        int number = Math.Clamp(surface.PageAt(contentY - PageGap), 1, pages.Length);
        var page = pages[number - 1];
        double pageY = (contentY - surface.PageTop(number)) / old;
        double oldLeft = Math.Max(PageGap, (surface.ActualWidth - page.Settings.Width * old) / 2);
        double pageX = (contentX - oldLeft) / old;

        zoom = value;
        double width = Math.Max(pages.Max(item => item.Settings.Width) * zoom + PageGap * 2, scroll.ViewportWidth);
        double newLeft = Math.Max(PageGap, (width - page.Settings.Width * zoom) / 2);
        pendingOffset = Math.Max(0, surface.PageTop(number) + pageY * zoom - anchor.Y);
        Refresh();
        scroll.ScrollToVerticalOffset(pendingOffset);
        scroll.ScrollToHorizontalOffset(Math.Max(0, newLeft + pageX * zoom - anchor.X));
    }

    /// <summary>Where a page of the given zoomed width starts in the view, after horizontal scrolling; null before layout.</summary>
    internal double? PageScreenLeft(double width) => surface is null || scroll is null || surface.ActualWidth <= 0 ? null :
        Math.Max(PageGap, (surface.ActualWidth - width) / 2) - scroll.HorizontalOffset;

    /// <summary>The page that holds the caret, searching from the page in view, and the caret on it.</summary>
    internal (WritePage Page, WriteCaret Caret)? CaretPage()
    {
        var caret = Session.Selection.Caret;
        int start = Math.Clamp(page - 1, 0, Math.Max(0, pages.Length - 1));
        for (int distance = 0; distance < pages.Length; distance++)
        {
            foreach (int index in (int[])[start + distance, start - distance])
            {
                if ((uint)index >= pages.Length || distance == 0 && index != start)
                    continue;
                if (pages[index].FindCaret(caret) is { } found)
                    return (pages[index], found);
            }
        }
        return null;
    }

    /// <summary>Scrolls the least distance that shows the caret, once the pages holding it are laid out.</summary>
    private void RevealCaret()
    {
        if (!revealCaret || scroll is null || surface is null || pages.Length == 0 || scroll.ViewportHeight <= 0)
            return;
        if (CaretPage() is not var (holder, caret))
            return;
        revealCaret = false;
        double top = surface.PageTop(holder.Number) + caret.Top * zoom;
        double bottom = top + caret.Height * zoom;
        double margin = Math.Min(24, scroll.ViewportHeight / 4);
        if (top < scroll.VerticalOffset)
            ScrollTo(top - margin);
        else if (bottom > scroll.VerticalOffset + scroll.ViewportHeight)
            ScrollTo(bottom - scroll.ViewportHeight + margin);
        double left = Math.Max(PageGap, (surface.ActualWidth - holder.Settings.Width * zoom) / 2) + caret.X * zoom;
        if (left < scroll.HorizontalOffset)
            scroll.ScrollToHorizontalOffset(Math.Max(0, left - margin));
        else if (left > scroll.HorizontalOffset + scroll.ViewportWidth)
            scroll.ScrollToHorizontalOffset(left - scroll.ViewportWidth + margin);
    }
}
