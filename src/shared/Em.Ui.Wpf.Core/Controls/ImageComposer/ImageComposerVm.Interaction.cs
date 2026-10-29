using System.Windows;

namespace Em.Ui.Wpf.Controls;

/// <summary>Pointer and keyboard editing for the physical canvas.</summary>
public sealed partial class ImageComposerVm
{
   private ComposerHandle _handle;
   private Point _pointerStart;
   private Point _pointerLast;
   private Rect _originalBox;
   private double _originalAngle;
   private ComposerArrowItem? _newArrow;
   private bool _dirtyBeforeArrow;

   internal IEnumerable<(ComposerHandle Handle, Point Point)> Handles(double tolerance) {
      if (SelectedItem == null || IsReadOnly) yield break;
      if (SelectedItem is ComposerArrowItem arrow) {
         yield return (ComposerHandle.Start, new Point(arrow.StartXMm, arrow.StartYMm));
         yield return (ComposerHandle.End, new Point(arrow.EndXMm, arrow.EndYMm));
         yield break;
      }
      var item = SelectedItem;
      var box = IsCropping ? CropBounds : new Rect(0, 0, item.WidthMm, item.HeightMm);
      var center = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
      foreach (var (handle, point) in new[] {
         (ComposerHandle.NorthWest, box.TopLeft), (ComposerHandle.North, new Point(center.X, box.Top)),
         (ComposerHandle.NorthEast, box.TopRight), (ComposerHandle.East, new Point(box.Right, center.Y)),
         (ComposerHandle.SouthEast, box.BottomRight), (ComposerHandle.South, new Point(center.X, box.Bottom)),
         (ComposerHandle.SouthWest, box.BottomLeft), (ComposerHandle.West, new Point(box.Left, center.Y)) })
         yield return (handle, ComposerGeometry.World(item, point));
      if (!IsCropping) yield return (ComposerHandle.Rotate, ComposerGeometry.World(item, new Point(center.X, box.Top - tolerance * 4)));
   }
   internal ComposerHandle HandleAt(Point point, double tolerance) => Handles(tolerance)
      .Where(h => (h.Point - point).Length <= tolerance).Select(h => h.Handle).FirstOrDefault();

   internal bool BeginPointer(Point point, double tolerance) {
      if (IsReadOnly || HasInitialImage) return false;
      EndPointer();
      if (Tool == ComposerTool.Arrow) {
         _dirtyBeforeArrow = HasChanges;
         var arrow = new ComposerArrowItem { StartXMm = point.X, StartYMm = point.Y, EndXMm = point.X, EndYMm = point.Y,
            Color = ArrowColor, ThicknessMm = ArrowThicknessMm };
         Items.Add(arrow); SelectedItem = arrow; _newArrow = arrow;
         _handle = ComposerHandle.End;
      }
      else {
         var handle = HandleAt(point, tolerance);
         if (handle == ComposerHandle.None) {
            if (IsCropping) {
               if (SelectedItem == null || !CropBounds.Contains(ComposerGeometry.Local(SelectedItem, point))) return false;
            }
            else SelectedItem = Items.Reverse().FirstOrDefault(i => ComposerGeometry.Hit(i, point, tolerance));
            if (SelectedItem == null) return false;
            handle = ComposerHandle.Move;
         }
         _handle = handle;
      }
      _pointerStart = _pointerLast = point;
      if (SelectedItem != null) {
         _originalBox = IsCropping ? CropBounds : new Rect(SelectedItem.XMm, SelectedItem.YMm, SelectedItem.WidthMm, SelectedItem.HeightMm);
         _originalAngle = SelectedItem.AngleDeg;
      }
      return true;
   }

   internal void MovePointer(Point point, bool shift) {
      if (_handle == ComposerHandle.None || SelectedItem == null || IsReadOnly || HasInitialImage) return;
      var item = SelectedItem;
      var delta = point - _pointerLast;
      _pointerLast = point;
      if (item is ComposerArrowItem arrow) {
         if (_handle is ComposerHandle.Start or ComposerHandle.Move) { arrow.StartXMm += delta.X; arrow.StartYMm += delta.Y; }
         if (_handle is ComposerHandle.End or ComposerHandle.Move) { arrow.EndXMm += delta.X; arrow.EndYMm += delta.Y; }
         return;
      }
      var originalCenter = new Point(_originalBox.X + _originalBox.Width / 2, _originalBox.Y + _originalBox.Height / 2);
      if (_handle == ComposerHandle.Rotate) {
         var center = ComposerGeometry.Center(item);
         var start = _pointerStart - center; var current = point - center;
         var angle = _originalAngle + Vector.AngleBetween(start, current);
         item.AngleDeg = ComposerGeometry.Snap(angle, shift);
         return;
      }
      var localDelta = ComposerGeometry.Rotate(point, new Point(), -_originalAngle) - ComposerGeometry.Rotate(_pointerStart, new Point(), -_originalAngle);
      if (IsCropping) {
         CropBounds = _handle == ComposerHandle.Move
            ? new Rect(_originalBox.TopLeft + localDelta, _originalBox.Size)
            : Rect.Intersect(ComposerGeometry.Resize(_originalBox, _handle, localDelta, false), new Rect(0, 0, item.WidthMm, item.HeightMm));
         return;
      }
      if (_handle == ComposerHandle.Move) { MoveSelected(delta); return; }
      var resized = ComposerGeometry.Resize(new Rect(0, 0, _originalBox.Width, _originalBox.Height), _handle, localDelta, !shift);
      var centerBeforeRotation = new Point(_originalBox.X + resized.X + resized.Width / 2, _originalBox.Y + resized.Y + resized.Height / 2);
      var resizedCenter = ComposerGeometry.Rotate(centerBeforeRotation, originalCenter, _originalAngle);
      item.WidthMm = resized.Width; item.HeightMm = resized.Height;
      item.XMm = resizedCenter.X - resized.Width / 2; item.YMm = resizedCenter.Y - resized.Height / 2;
   }
   internal void EndPointer() {
      _handle = ComposerHandle.None;
      if (_newArrow is { } arrow) {
         _newArrow = null;
         if ((new Point(arrow.EndXMm, arrow.EndYMm) - new Point(arrow.StartXMm, arrow.StartYMm)).Length < 2) {
            Items.Remove(arrow); HasChanges = _dirtyBeforeArrow;
         }
      }
   }
   internal void MoveSelected(Vector delta) {
      if (!HasSelection) return;
      if (SelectedItem is ComposerArrowItem arrow) {
         arrow.StartXMm += delta.X; arrow.StartYMm += delta.Y;
         arrow.EndXMm += delta.X; arrow.EndYMm += delta.Y;
      }
      else if (SelectedItem is { } image) { image.XMm += delta.X; image.YMm += delta.Y; }
   }
   internal void Escape() {
      if (IsReadOnly) return;
      if (IsCropping) CancelCropCommand();
      else { EndPointer(); SelectedItem = null; Tool = ComposerTool.Select; }
   }
}
