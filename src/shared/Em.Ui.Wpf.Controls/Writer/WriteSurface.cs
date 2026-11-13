using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Documents.Layout;
using Em.Editing;
using Em.Controls.Write.Editing;
using Em.Controls.Write.Layout;
using Em.Controls.Write.Model;

namespace Em.Ui.Wpf.Controls;

internal sealed class WriteSurface : FrameworkElement
{
    private const double Gap = 24;
    private static readonly Typeface Display = new("Segoe UI");
    private static readonly ConditionalWeakTable<WriteImage, BitmapImage> Images = new();
    private readonly MatrixWriter owner;
    private WritePosition? drag;
    private bool composing;

    internal WriteSurface(MatrixWriter owner)
    {
        this.owner = owner;
        Focusable = true;
        ClipToBounds = true;
        TextCompositionManager.AddPreviewTextInputStartHandler(this, (_, args) =>
        {
            if (owner.Mode == EditorMode.Full && args.TextComposition.CompositionText.Length > 0)
            {
                composing = true;
                owner.BeginComposition(args.TextComposition.CompositionText);
            }
        });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(this, (_, args) =>
        {
            if (composing)
                owner.BeginComposition(args.TextComposition.CompositionText);
        });
    }

    internal double PageTop(int number)
    {
        double top = Gap;
        for (int i = 0; i < number - 1 && i < owner.Pages.Length; i++)
            top += owner.Pages[i].Settings.Height * owner.Zoom + Gap;
        return top;
    }

    internal int PageAt(double offset)
    {
        double y = Gap;
        foreach (var page in owner.Pages)
        {
            y += page.Settings.Height * owner.Zoom + Gap;
            if (y > offset + Gap)
                return page.Number;
        }
        return Math.Max(1, owner.PageCount);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = owner.Pages.Select(page => page.Settings.Width).DefaultIfEmpty(816).Max() * owner.Zoom + Gap * 2;
        double height = owner.Pages.Length == 0 ? 1056 * owner.Zoom + Gap * 2 : PageTop(owner.Pages.Length + 1);
        return new(double.IsFinite(availableSize.Width) ? Math.Max(width, availableSize.Width) : width, height);
    }

    // The palette is swapped when the inherited theme mode changes; repaint once the new brushes are in place.
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == global::Em.Ui.Wpf.Controls.GridTheme.ModeProperty)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(InvalidateVisual));
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (!IsVisible && !owner.RenderOffscreen)
            return;
        drawing.DrawRectangle(Brush("SurfaceContainerHighest"), null, new Rect(RenderSize));
        double offset = owner.VerticalOffset;
        double viewport = (Parent as ScrollViewer)?.ViewportHeight ?? owner.ActualHeight;
        if (viewport <= 0)
            viewport = 800;
        if (owner.Pages.Length == 0)
        {
            double width = 816 * owner.Zoom;
            var placeholder = new Rect(Math.Max(Gap, (ActualWidth - width) / 2), Gap, width, 1056 * owner.Zoom);
            drawing.DrawRectangle(Brush("SurfaceContainerLow"), null, placeholder);
            var stroke = new Pen(Brush("OutlineVariant"), 3);
            for (int i = 0; i < 7; i++)
                drawing.DrawLine(stroke, new(placeholder.X + width * .12, Gap + (90 + i * 22) * owner.Zoom),
                    new(placeholder.X + width * (.8 - (i % 3) * .07), Gap + (90 + i * 22) * owner.Zoom));
            return;
        }

        int first = Math.Max(1, PageAt(offset) - 1);
        for (int index = first - 1; index < owner.Pages.Length; index++)
        {
            var page = owner.Pages[index];
            double top = PageTop(page.Number);
            if (top > offset + viewport + Gap)
                break;
            double scale = owner.Zoom;
            double left = Math.Max(Gap, (ActualWidth - page.Settings.Width * scale) / 2);
            drawing.PushTransform(new MatrixTransform(scale, 0, 0, scale, left, top));
            drawing.DrawRectangle(Brushes.White, new Pen(Brush("OutlineVariant"), 1),
                new Rect(0, 0, page.Settings.Width, page.Settings.Height));
            var selection = owner.Session.Selection;
            var story = selection.Caret.Story;
            if (story != WriteStory.Body)
            {
                var boundary = new Pen(Brush("Primary"), 1) { DashStyle = new([4, 3], 0) };
                double y = story is WriteStory.Header or WriteStory.FirstHeader or WriteStory.EvenHeader ?
                    page.Settings.Top : page.Settings.Height - page.Settings.Bottom;
                drawing.DrawLine(boundary, new(page.Settings.Left, y), new(page.Settings.Width - page.Settings.Right, y));
            }

            foreach (var rectangle in page.SelectionBounds(selection))
                drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 65, 95, 145)), null,
                    new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
            foreach (var command in page.Commands)
            {
                if (command is DrawGlyphs text)
                {
                    bool inBody = text.Baseline >= page.Settings.Top && text.Baseline <= page.Settings.Height - page.Settings.Bottom;
                    if (story != WriteStory.Body && inBody)
                        drawing.PushOpacity(.45);
                    DrawGlyphs(drawing, text);
                    if (story != WriteStory.Body && inBody)
                        drawing.Pop();
                }
                else if (command is FillRectangle fill)
                    drawing.DrawRectangle(ColorBrush(fill.Color), null, new Rect(fill.X, fill.Y, fill.Width, fill.Height));
                else if (command is DrawLine line)
                    drawing.DrawLine(new(ColorBrush(line.Color), line.Width), new(line.X1, line.Y1), new(line.X2, line.Y2));
            }
            foreach (var picture in page.Images)
            {
                try
                {
                    var image = Images.GetValue(picture.Image, LoadImage);
                    var rectangle = picture.Bounds;
                    drawing.DrawImage(image, new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
                }
                catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException) { }
            }
            if (owner.Mode == EditorMode.Full && IsKeyboardFocusWithin && owner.CaretVisible &&
                page.FindCaret(selection.Caret) is { } caret)
            {
                drawing.DrawLine(new(Brush("Primary"), 2), new(caret.X, caret.Top), new(caret.X, caret.Top + caret.Height));
                if (owner.CompositionText is { Length: > 0 } composition)
                {
                    var text = new FormattedText(composition, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Display, 15,
                        Brush("Primary"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    text.SetTextDecorations(TextDecorations.Underline);
                    drawing.DrawText(text, new(caret.X, caret.Top));
                }
            }
            drawing.Pop();
        }
    }

    private static void DrawGlyphs(DrawingContext drawing, DrawGlyphs command)
    {
        if (command.Glyphs.Length == 0)
            return;
        var face = WpfFontProvider.GlyphFace(command.Face, command.SyntheticBold, command.SyntheticItalic);
        var run = new GlyphRun(face, 0, false, command.Size, 1,
            command.Glyphs.Select(glyph => glyph.Id).ToArray(), new Point(command.X, command.Baseline),
            command.Glyphs.Select(glyph => glyph.Advance).ToArray(), null, null, null, null, null, null);
        drawing.DrawGlyphRun(ColorBrush(command.Color), run);
    }

    private static BitmapImage LoadImage(WriteImage source)
    {
        using var data = new MemoryStream(source.Bytes.ToArray());
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = data;
        image.EndInit();
        image.Freeze();
        return image;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);
        var point = args.GetPosition(this);
        var page = owner.Pages.ElementAtOrDefault(PageAt(point.Y) - 1);
        if (page is null)
            return;
        double y = (point.Y - PageTop(page.Number)) / owner.Zoom;
        double left = Math.Max(Gap, (ActualWidth - page.Settings.Width * owner.Zoom) / 2);
        WriteStory requested = owner.Session.Selection.Caret.Story;
        if (args.ClickCount == 2 && owner.Mode == EditorMode.Full)
        {
            requested = y < page.Settings.Top ? WriteStory.Header :
                y > page.Settings.Height - page.Settings.Bottom ? WriteStory.Footer : WriteStory.Body;
            if (requested != owner.Session.Selection.Caret.Story)
                owner.Session.EditStory(requested, page.Section);
        }
        if (page.HitTest((point.X - left) / owner.Zoom, y, requested) is not { } target)
        {
            Focus();
            return;
        }
        Focus();
        drag = Keyboard.Modifiers == ModifierKeys.Shift ? owner.Session.Selection.Anchor : target;
        if (args.ClickCount == 2 && owner.Session.GetParagraph(target) is { } paragraph && paragraph.Length > 0)
        {
            int start = target.Offset, end = target.Offset;
            while (start > 0 && !char.IsWhiteSpace(paragraph.Text[start - 1])) start--;
            while (end < paragraph.Length && !char.IsWhiteSpace(paragraph.Text[end])) end++;
            owner.Session.Select(new(target with { Offset = start }, target with { Offset = end }));
        }
        else
        {
            owner.Session.Select(new(drag.Value, target));
            CaptureMouse();
        }
        args.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (!IsMouseCaptured || drag is not { } anchor)
            return;
        var point = args.GetPosition(this);
        var page = owner.Pages.ElementAtOrDefault(PageAt(point.Y) - 1);
        if (page is null)
            return;
        double left = Math.Max(Gap, (ActualWidth - page.Settings.Width * owner.Zoom) / 2);
        if (page.HitTest((point.X - left) / owner.Zoom, (point.Y - PageTop(page.Number)) / owner.Zoom, anchor.Story) is { } target)
            owner.Session.Select(new(anchor, target));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonUp(args);
        drag = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    protected override void OnTextInput(TextCompositionEventArgs args)
    {
        base.OnTextInput(args);
        if (owner.Mode != EditorMode.Full || args.Text.Length == 0 || char.IsControl(args.Text[0]))
            return;
        composing = false;
        owner.BeginComposition(args.Text);
        args.Handled = owner.CommitComposition();
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        var session = owner.Session;
        switch (args.Key)
        {
            case Key.Back: args.Handled = session.Delete(true).Accepted; return;
            case Key.Delete: args.Handled = session.Delete(false).Accepted; return;
            case Key.Enter: args.Handled = session.InsertText("\n").Accepted; return;
            case Key.Tab: args.Handled = session.InsertText("\t").Accepted; return;
            case Key.Escape:
                if (session.Selection.Caret.Story != WriteStory.Body)
                    args.Handled = session.EditStory(WriteStory.Body, session.Selection.Caret.Section).Accepted;
                return;
        }
        var caret = session.Selection.Caret;
        if (session.GetParagraph(caret) is not { } paragraph)
            return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var target = caret;
        if (args.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            var boundaries = StringInfo.ParseCombiningCharacters(paragraph.Text).Append(paragraph.Length);
            int offset = args.Key switch
            {
                Key.Home => 0, Key.End => paragraph.Length,
                Key.Left => boundaries.LastOrDefault(offset => offset < caret.Offset, -1),
                _ => boundaries.FirstOrDefault(offset => offset > caret.Offset, -1)
            };
            if (offset >= 0)
                target = caret with { Offset = offset };
            else
            {
                var paragraphs = session.Paragraphs(caret.Story).ToArray();
                int index = Array.IndexOf(paragraphs, caret with { Offset = 0 }) + (args.Key == Key.Left ? -1 : 1);
                if ((uint)index < paragraphs.Length)
                    target = paragraphs[index] with { Offset = args.Key == Key.Left ? session.GetParagraph(paragraphs[index])!.Length : 0 };
            }
        }
        else if (args.Key is Key.Up or Key.Down)
        {
            // Move from the page that holds the caret, not the page scrolled into view.
            if (owner.CaretPage() is var (page, position))
                target = page.HitTest(position.X, position.Top + position.Height * (args.Key == Key.Up ? -.5 : 1.5), caret.Story) ?? caret;
        }
        else
            return;
        args.Handled = session.Select(new(shift ? session.Selection.Anchor : target, target));
    }

    private Brush Brush(string role) => TryFindResource("MatrixGrid.Brush." + role) as Brush ?? Brushes.Transparent;
    private static Brush ColorBrush(DocumentColor color) => new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
}
