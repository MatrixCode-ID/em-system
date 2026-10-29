using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;

namespace Em.Ui.Wpf.Controls;

internal sealed class ComposerChoiceConverter : IValueConverter
{
   public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value.ToString() == parameter.ToString();
   public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true
      ? Enum.Parse<ComposerArrowColor>(parameter.ToString()!) : Binding.DoNothing;
}

internal sealed class ComposerItemPresenter : FrameworkElement
{
   public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(ComposerItem), typeof(ComposerItemPresenter),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ItemChanged));
   public ComposerItem? Item { get => (ComposerItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
   public ComposerItemPresenter() => RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
   private static void ItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
      var view = (ComposerItemPresenter)d;
      if (e.OldValue is ComposerItem old) PropertyChangedEventManager.RemoveHandler(old, view.Changed, "");
      if (e.NewValue is ComposerItem item) PropertyChangedEventManager.AddHandler(item, view.Changed, "");
   }
   private void Changed(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();
   protected override void OnRender(DrawingContext dc) { if (Item is { } item) ComposerGeometry.DrawItem(dc, item); }
}

internal sealed class ComposerOverlay : FrameworkElement
{
   private readonly HashSet<ComposerItem> _observed = [];
   public static readonly DependencyProperty ComposerProperty = DependencyProperty.Register(nameof(Composer), typeof(ImageComposerVm), typeof(ComposerOverlay),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ComposerChanged));
   public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(ComposerOverlay),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
   public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(nameof(Surface), typeof(Brush), typeof(ComposerOverlay),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
   public static readonly DependencyProperty WarningProperty = DependencyProperty.Register(nameof(Warning), typeof(Brush), typeof(ComposerOverlay),
      new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
   public ImageComposerVm? Composer { get => (ImageComposerVm?)GetValue(ComposerProperty); set => SetValue(ComposerProperty, value); }
   public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
   public Brush Surface { get => (Brush)GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
   public Brush Warning { get => (Brush)GetValue(WarningProperty); set => SetValue(WarningProperty, value); }
   internal double PreviewScale { get; set; } = 1;
   public ComposerOverlay() {
      SetResourceReference(AccentProperty, "themeAccentBrush");
      SetResourceReference(SurfaceProperty, "themeWindowBackgroundBrush");
      SetResourceReference(WarningProperty, "warningBrush");
   }
   private static void ComposerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
      var view = (ComposerOverlay)d;
      if (e.OldValue is ImageComposerVm old) PropertyChangedEventManager.RemoveHandler(old, view.Changed, "");
      if (e.NewValue is ImageComposerVm vm) PropertyChangedEventManager.AddHandler(vm, view.Changed, "");
      view.Observe();
   }
   private void Observe() {
      foreach (var item in _observed.Where(i => Composer == null || !Composer.Items.Contains(i)).ToArray()) {
         PropertyChangedEventManager.RemoveHandler(item, Changed, ""); _observed.Remove(item);
      }
      if (Composer != null) foreach (var item in Composer.Items.Where(i => !_observed.Contains(i))) {
         PropertyChangedEventManager.AddHandler(item, Changed, ""); _observed.Add(item);
      }
   }
   private void Changed(object? sender, PropertyChangedEventArgs e) { Observe(); InvalidateVisual(); }
   protected override void OnRender(DrawingContext dc) {
      if (Composer is not { } vm) return;
      var scale = Math.Max(.001, PreviewScale);
      var pen = new Pen(Accent, 1.2 / scale);
      dc.DrawRectangle(null, pen, new Rect(0, 0, vm.CanvasWidthMm, vm.CanvasHeightMm));
      foreach (var image in vm.Items.OfType<ComposerImageItem>().Where(i => i.IsLowResolution)) {
         var p = ComposerGeometry.World(image, new Point(0, 0));
         var iconSize = 14 / scale;
         dc.PushClip(new RectangleGeometry(new Rect(0, 0, vm.CanvasWidthMm, vm.CanvasHeightMm)));
         dc.DrawRoundedRectangle(Surface, new Pen(Warning, 1 / scale), new Rect(p, new Size(iconSize, iconSize)), 2 / scale, 2 / scale);
         dc.DrawText(EFontAwesomeIcon.Solid_TriangleExclamation.CreateFormattedText(Warning, 12 / scale), p + new Vector(1 / scale, 0));
         dc.Pop();
      }
      if (vm.SelectedItem is not { } item || vm.IsReadOnly) return;
      if (item is ComposerImageItem) {
         var box = vm.IsCropping ? vm.CropBounds : new Rect(0, 0, item.WidthMm, item.HeightMm);
         var center = ComposerGeometry.Center(item);
         dc.PushTransform(new RotateTransform(item.AngleDeg, center.X, center.Y));
         var rect = new Rect(item.XMm + box.X, item.YMm + box.Y, box.Width, box.Height);
         if (vm.IsCropping) {
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
               new RectangleGeometry(new Rect(item.XMm, item.YMm, item.WidthMm, item.HeightMm)), new RectangleGeometry(rect));
            dc.PushOpacity(.45); dc.DrawGeometry(Surface, null, outside); dc.Pop();
         }
         dc.DrawRectangle(null, pen, rect);
         if (!vm.IsCropping) dc.DrawLine(pen, new Point(rect.X + rect.Width / 2, rect.Top), new Point(rect.X + rect.Width / 2, rect.Top - 24 / scale));
         dc.Pop();
      }
      foreach (var (handle, point) in vm.Handles(6 / scale)) {
         var half = 4 / scale;
         if (handle == ComposerHandle.Rotate) dc.DrawEllipse(Surface, pen, point, half, half);
         else dc.DrawRectangle(Surface, pen, new Rect(point.X - half, point.Y - half, half * 2, half * 2));
      }
   }
}
