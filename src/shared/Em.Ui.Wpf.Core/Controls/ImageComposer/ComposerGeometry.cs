using System.Windows;
using System.Windows.Media;

namespace Em.Ui.Wpf.Controls;

internal enum ComposerHandle { None, Move, NorthWest, North, NorthEast, East, SouthEast, South, SouthWest, West, Rotate, Start, End }

internal static class ComposerGeometry
{
   internal static double Pixels(double mm, double dpi) => mm / 25.4 * dpi;
   internal static double Millimeters(double pixels, double dpi) => pixels / dpi * 25.4;
   internal static double Finite(double value, double fallback = 0) => double.IsFinite(value) ? value : fallback;
   internal static double Snap(double angle, bool free) => free ? angle : Math.Round(angle / 15) * 15;
   internal static Point Rotate(Point point, Point center, double angle) {
      var r = angle * Math.PI / 180;
      var v = point - center;
      return center + new Vector(v.X * Math.Cos(r) - v.Y * Math.Sin(r), v.X * Math.Sin(r) + v.Y * Math.Cos(r));
   }
   internal static Point Center(ComposerItem item) => new(item.XMm + item.WidthMm / 2, item.YMm + item.HeightMm / 2);
   internal static Point Local(ComposerItem item, Point point) => Rotate(point, Center(item), -item.AngleDeg) - new Vector(item.XMm, item.YMm);
   internal static Point World(ComposerItem item, Point point) => Rotate(point + new Vector(item.XMm, item.YMm), Center(item), item.AngleDeg);
   internal static bool Hit(ComposerItem item, Point point, double tolerance) {
      if (item is ComposerArrowItem arrow) {
         var a = new Point(arrow.StartXMm, arrow.StartYMm);
         var b = new Point(arrow.EndXMm, arrow.EndYMm);
         var v = b - a;
         var t = v.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(point - a, v) / v.LengthSquared, 0, 1);
         return (point - (a + v * t)).Length <= Math.Max(tolerance, arrow.ThicknessMm * 1.5);
      }
      return new Rect(0, 0, item.WidthMm, item.HeightMm).Contains(Local(item, point));
   }
   internal static Rect Resize(Rect box, ComposerHandle handle, Vector delta, bool keepRatio) {
      var left = handle is ComposerHandle.NorthWest or ComposerHandle.West or ComposerHandle.SouthWest;
      var right = handle is ComposerHandle.NorthEast or ComposerHandle.East or ComposerHandle.SouthEast;
      var top = handle is ComposerHandle.NorthWest or ComposerHandle.North or ComposerHandle.NorthEast;
      var bottom = handle is ComposerHandle.SouthWest or ComposerHandle.South or ComposerHandle.SouthEast;
      var w = Math.Max(1, box.Width + (left ? -delta.X : right ? delta.X : 0));
      var h = Math.Max(1, box.Height + (top ? -delta.Y : bottom ? delta.Y : 0));
      if (keepRatio && (left || right) && (top || bottom)) {
         var horizontalChange = (w - box.Width) / box.Width;
         var verticalChange = (h - box.Height) / box.Height;
         var scale = 1 + (Math.Abs(horizontalChange) >= Math.Abs(verticalChange) ? horizontalChange : verticalChange);
         scale = Math.Max(scale, Math.Max(1 / box.Width, 1 / box.Height));
         w = box.Width * scale;
         h = box.Height * scale;
      }
      return new Rect(left ? box.Right - w : box.Left, top ? box.Bottom - h : box.Top, w, h);
   }
   internal static Int32Rect CropPixels(Rect crop, ComposerImageItem item) {
      var x = Math.Clamp((int)Math.Round(crop.X / item.WidthMm * item.PixelWidth), 0, item.PixelWidth - 1);
      var y = Math.Clamp((int)Math.Round(crop.Y / item.HeightMm * item.PixelHeight), 0, item.PixelHeight - 1);
      var right = Math.Clamp((int)Math.Round(crop.Right / item.WidthMm * item.PixelWidth), x + 1, item.PixelWidth);
      var bottom = Math.Clamp((int)Math.Round(crop.Bottom / item.HeightMm * item.PixelHeight), y + 1, item.PixelHeight);
      return new Int32Rect(x, y, right - x, bottom - y);
   }
   internal static Brush Ink(ComposerArrowColor color) {
      var value = color switch {
         ComposerArrowColor.Black => Colors.Black,
         ComposerArrowColor.Blue => Color.FromRgb(0x15, 0x65, 0xC0),
         ComposerArrowColor.Yellow => Color.FromRgb(0xF9, 0xA8, 0x25),
         _ => Color.FromRgb(0xD3, 0x2F, 0x2F)
      };
      var brush = new SolidColorBrush(value);
      brush.Freeze();
      return brush;
   }
   internal static Point[] ArrowHead(ComposerArrowItem arrow) {
      var end = new Point(arrow.EndXMm, arrow.EndYMm);
      var direction = end - new Point(arrow.StartXMm, arrow.StartYMm);
      if (direction.Length == 0) direction = new Vector(1, 0);
      direction.Normalize();
      var back = end - direction * Math.Max(3, 4 * arrow.ThicknessMm);
      var side = new Vector(-direction.Y, direction.X) * Math.Max(2, 3 * arrow.ThicknessMm) / 2;
      return [end, back + side, back - side];
   }
   internal static void DrawItem(DrawingContext dc, ComposerItem item) {
      if (item is ComposerImageItem image) {
         var center = Center(image);
         dc.PushTransform(new RotateTransform(image.AngleDeg, center.X, center.Y));
         dc.DrawImage(image.Source, new Rect(image.XMm, image.YMm, image.WidthMm, image.HeightMm));
         dc.Pop();
      }
      else if (item is ComposerArrowItem arrow) {
         var ink = Ink(arrow.Color);
         var head = ArrowHead(arrow);
         var headBase = new Point((head[1].X + head[2].X) / 2, (head[1].Y + head[2].Y) / 2);
         dc.DrawLine(new Pen(ink, arrow.ThicknessMm), new Point(arrow.StartXMm, arrow.StartYMm), headBase);
         var path = new StreamGeometry();
         using (var context = path.Open()) { context.BeginFigure(head[0], true, true); context.PolyLineTo([head[1], head[2]], true, false); }
         path.Freeze();
         dc.DrawGeometry(ink, null, path);
      }
   }
}
