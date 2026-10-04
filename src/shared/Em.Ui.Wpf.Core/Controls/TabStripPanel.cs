using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Panel deretan tab yang menangani tab banyak dengan dua tahap. Selama muat, setiap tab selebar
   /// isinya. Kalau tidak muat, tab-tab yang paling lebar disusutkan lebih dulu sampai semuanya muat,
   /// tapi tidak pernah lebih sempit dari <see cref="MinItemWidth"/>. Kalau di lebar minimum pun masih
   /// tidak muat, deretannya bisa digeser ke kiri/kanan.
   /// </summary>
   /// <remarks>
   /// Penggeserannya lewat <see cref="IScrollInfo"/>, jadi panel ini dipakai sebagai <c>ItemsPanel</c>
   /// di dalam <see cref="ScrollViewer"/> dengan <c>CanContentScroll="True"</c>. ScrollViewer itulah
   /// yang meneruskan roda mouse, perintah <see cref="ScrollBar.LineLeftCommand"/>/
   /// <see cref="ScrollBar.LineRightCommand"/> dari tombol panah, dan permintaan
   /// <c>BringIntoView</c> saat sebuah tab dipilih. Roda mouse ke atas/bawah menggeser deretan ke
   /// kiri/kanan, karena deretan tab hanya bergerak mendatar.
   /// </remarks>
   public class TabStripPanel : Panel, IScrollInfo
   {
      // How far one arrow click or one wheel notch moves the strip.
      private const double LineStep = 80;

      /// <summary>Mengidentifikasi property <see cref="MinItemWidth"/>.</summary>
      public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
         nameof(MinItemWidth), typeof(double), typeof(TabStripPanel),
         new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure));

      /// <summary>
      /// Lebar terkecil yang boleh dicapai sebuah tab saat disusutkan. Tab yang isinya memang lebih
      /// sempit dari ini tetap selebar isinya.
      /// </summary>
      public double MinItemWidth {
         get => (double)GetValue(MinItemWidthProperty);
         set => SetValue(MinItemWidthProperty, value);
      }

      private double[] _widths = [];
      private Size _extent;
      private Size _viewport;
      private double _offset;

      /// <inheritdoc />
      protected override Size MeasureOverride(Size availableSize) {
         var count = InternalChildren.Count;
         var desired = new double[count];
         var height = 0d;

         for (var i = 0; i < count; i++) {
            var child = InternalChildren[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            desired[i] = child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
         }

         _widths = Fit(desired, availableSize.Width, MinItemWidth);

         // Measured again at the width each tab is actually given, so a shrunk tab trims its title
         // instead of being cut off at its edge.
         for (var i = 0; i < count; i++)
            InternalChildren[i].Measure(new Size(_widths[i], availableSize.Height));

         var total = _widths.Sum();
         var viewportWidth = double.IsInfinity(availableSize.Width) ? total : availableSize.Width;
         UpdateScrollInfo(new Size(total, height), new Size(viewportWidth, height));

         return new Size(Math.Min(total, viewportWidth), height);
      }

      /// <inheritdoc />
      protected override Size ArrangeOverride(Size finalSize) {
         var x = -_offset;
         for (var i = 0; i < InternalChildren.Count && i < _widths.Length; i++) {
            InternalChildren[i].Arrange(new Rect(x, 0, _widths[i], finalSize.Height));
            x += _widths[i];
         }

         return finalSize;
      }

      // The widest tabs give way first: every tab is capped at one common width, the largest cap at
      // which the strip still fits, so short titles keep their natural size while long ones trim.
      private static double[] Fit(double[] desired, double available, double min) {
         if (double.IsInfinity(available) || desired.Sum() <= available) return desired;

         var sorted = desired.OrderBy(w => w).ToArray();
         var remaining = available;
         var left = sorted.Length;
         var cap = 0d;

         foreach (var width in sorted) {
            cap = remaining / left;
            if (width > cap) break;

            remaining -= width;
            left--;
            cap = left > 0 ? remaining / left : width;
         }

         cap = Math.Max(cap, min);
         return desired.Select(w => Math.Min(w, cap)).ToArray();
      }

      private void UpdateScrollInfo(Size extent, Size viewport) {
         if (extent == _extent && viewport == _viewport) return;

         _extent = extent;
         _viewport = viewport;
         _offset = Clamp(_offset);
         ScrollOwner?.InvalidateScrollInfo();
      }

      private double Clamp(double offset) =>
         Math.Max(0, Math.Min(offset, _extent.Width - _viewport.Width));

      #region IScrollInfo

      /// <inheritdoc />
      public bool CanHorizontallyScroll { get; set; }

      /// <inheritdoc />
      public bool CanVerticallyScroll { get; set; }

      /// <inheritdoc />
      public double ExtentWidth => _extent.Width;

      /// <inheritdoc />
      public double ExtentHeight => _extent.Height;

      /// <inheritdoc />
      public double ViewportWidth => _viewport.Width;

      /// <inheritdoc />
      public double ViewportHeight => _viewport.Height;

      /// <inheritdoc />
      public double HorizontalOffset => _offset;

      /// <inheritdoc />
      public double VerticalOffset => 0;

      /// <inheritdoc />
      public ScrollViewer? ScrollOwner { get; set; }

      /// <inheritdoc />
      public void SetHorizontalOffset(double offset) {
         offset = Clamp(offset);
         if (offset == _offset) return;

         _offset = offset;
         InvalidateArrange();
         ScrollOwner?.InvalidateScrollInfo();
      }

      /// <inheritdoc />
      public void SetVerticalOffset(double offset) { }

      /// <inheritdoc />
      public void LineLeft() => SetHorizontalOffset(_offset - LineStep);

      /// <inheritdoc />
      public void LineRight() => SetHorizontalOffset(_offset + LineStep);

      /// <inheritdoc />
      public void LineUp() => LineLeft();

      /// <inheritdoc />
      public void LineDown() => LineRight();

      /// <inheritdoc />
      public void PageLeft() => SetHorizontalOffset(_offset - _viewport.Width);

      /// <inheritdoc />
      public void PageRight() => SetHorizontalOffset(_offset + _viewport.Width);

      /// <inheritdoc />
      public void PageUp() => PageLeft();

      /// <inheritdoc />
      public void PageDown() => PageRight();

      /// <inheritdoc />
      public void MouseWheelLeft() => LineLeft();

      /// <inheritdoc />
      public void MouseWheelRight() => LineRight();

      /// <inheritdoc />
      public void MouseWheelUp() => LineLeft();

      /// <inheritdoc />
      public void MouseWheelDown() => LineRight();

      /// <inheritdoc />
      public Rect MakeVisible(Visual visual, Rect rectangle) {
         // Scrolls the whole tab into view, whichever part of it asked.
         for (var i = 0; i < InternalChildren.Count && i < _widths.Length; i++) {
            var child = InternalChildren[i];
            if (child != visual && !child.IsAncestorOf(visual)) continue;

            var left = 0d;
            for (var j = 0; j < i; j++) left += _widths[j];
            var right = left + _widths[i];

            if (left < _offset) SetHorizontalOffset(left);
            else if (right > _offset + _viewport.Width) SetHorizontalOffset(right - _viewport.Width);

            return new Rect(new Point(left - _offset, 0), new Size(_widths[i], _viewport.Height));
         }

         return rectangle;
      }

      #endregion
   }
}
