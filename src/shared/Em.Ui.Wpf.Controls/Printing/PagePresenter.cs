using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Em.Documents.Layout;

namespace Em.Ui.Wpf.Controls;

/// <summary>Draws one laid-out document page, scaled by a zoom factor.</summary>
public sealed class PagePresenter : FrameworkElement
{
    /// <summary>The number of device-independent pixels in one point.</summary>
    public const double DipsPerPoint = 96d / 72d;
    private static readonly Lazy<GlyphTypeface> Face = new(() =>
        new GlyphTypeface(new Uri("pack://application:,,,/Em.Ui.Wpf.Controls;component/Documents/Assets/NotoSans-Regular.ttf")));

    /// <summary>Identifies the <see cref="Page"/> dependency property.</summary>
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(DocumentPage), typeof(PagePresenter),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the <see cref="Zoom"/> dependency property.</summary>
    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(PagePresenter),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is double number && double.IsFinite(number) && number > 0);

    /// <summary>Gets or sets the page to draw.</summary>
    public DocumentPage? Page
    {
        get => (DocumentPage?)GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>Gets or sets the zoom factor, where 1 draws the page at its printed size.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Measures the zoomed page size.</summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size.</returns>
    protected override Size MeasureOverride(Size availableSize) => Page is { } page
        ? new Size(page.Setup.PageWidth * DipsPerPoint * Zoom, page.Setup.PageHeight * DipsPerPoint * Zoom)
        : new Size();

    /// <summary>Draws the page.</summary>
    /// <param name="drawing">The drawing context.</param>
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Page is not { } page)
            return;
        drawing.PushTransform(new ScaleTransform(DipsPerPoint * Zoom, DipsPerPoint * Zoom));
        drawing.DrawDrawing(CreateDrawing(page, (float)VisualTreeHelper.GetDpi(this).PixelsPerDip));
        drawing.Pop();
    }

    /// <summary>Converts a laid-out page into a WPF drawing in device-independent pixels.</summary>
    /// <param name="page">The page.</param>
    /// <param name="pixelsPerDip">The display scale used for glyph rendering.</param>
    /// <returns>The drawing.</returns>
    public static DrawingGroup CreateDrawing(DocumentPage page, float pixelsPerDip = 1)
    {
        ArgumentNullException.ThrowIfNull(page);
        var bounds = new Rect(0, 0, page.Setup.PageWidth, page.Setup.PageHeight);
        var result = new DrawingGroup { ClipGeometry = new RectangleGeometry(bounds) };
        using (var drawing = result.Open())
        {
            drawing.DrawRectangle(Brushes.White, null, bounds);
            foreach (var command in page.Commands)
            {
                switch (command)
                {
                    case FillRectangle rectangle:
                        drawing.DrawRectangle(Paint(rectangle.Color), null,
                            new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
                        break;
                    case DrawLine line:
                        drawing.DrawLine(new Pen(Paint(line.Color), line.Width),
                            new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                        break;
                    case DrawGlyphs text:
                        var run = new GlyphRun(Face.Value, 0, false, text.Size, pixelsPerDip,
                            text.Glyphs.Select(glyph => glyph.Id).ToArray(), new Point(text.X, text.Baseline),
                            text.Glyphs.Select(glyph => glyph.Advance).ToArray(), null, null, null, null, null,
                            XmlLanguage.GetLanguage("en-US"));
                        drawing.DrawGlyphRun(Paint(text.Color), run);
                        break;
                }
            }
        }
        if (result.CanFreeze)
            result.Freeze();
        return result;
    }

    private static Brush Paint(DocumentColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }
}
