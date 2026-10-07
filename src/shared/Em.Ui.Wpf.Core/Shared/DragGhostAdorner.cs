using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// The shadow image that is dragged along with the cursor: a replica of an element, drawn on the
   /// adorner layer and moved through <see cref="Offset"/> while the drag runs.
   /// <para>
   /// This adorner knows nothing about what is being dragged - it only copies the look of its source
   /// element. That is what lets any screen use it: what differs between screens is <i>what</i> is dragged,
   /// not how the shadow is drawn.
   /// </para>
   /// </summary>
   public sealed class DragGhostAdorner : Adorner
   {
      private readonly VisualCollection _visuals;
      private readonly Rectangle _ghost;
      private readonly TranslateTransform _position = new();
      private readonly Point _grip;

      /// <summary>
      /// Creates a new shadow for an element.
      /// </summary>
      /// <param name="adornedElement">
      /// The element being adorned - usually the root of the screen. All positions given to
      /// <see cref="Offset"/> are computed relative to this element.
      /// </param>
      /// <param name="source">The element being replicated; its look is copied as-is together with its size.</param>
      /// <param name="grip">
      /// The grip point: where in <paramref name="source"/> the mouse button was pressed. Without it the
      /// shadow would jump to the cursor corner as soon as the drag starts.
      /// </param>
      public DragGhostAdorner(UIElement adornedElement, FrameworkElement source, Point grip) : base(adornedElement) {
         ArgumentNullException.ThrowIfNull(source);

         // The container of its children is filled before anything else. IsHitTestVisible below is a property
         // that is forced down to all children, so setting it makes WPF walk this object's children right then
         // - through VisualChildrenCount, which reads this field.
         _visuals = new VisualCollection(this);
         _grip = grip;

         // If the shadow also took part in hit-testing, it would always be hit first and the drop would never
         // find its target - the cursor would always be "over" its own shadow.
         IsHitTestVisible = false;

         _ghost = new Rectangle {
            Width = source.RenderSize.Width,
            Height = source.RenderSize.Height,
            RadiusX = 10,
            RadiusY = 10,
            Opacity = 0.75,
            IsHitTestVisible = false,
            RenderTransform = _position,
            Fill = new VisualBrush(source),
            Effect = new DropShadowEffect {
               BlurRadius = 16,
               ShadowDepth = 3,
               Opacity = 0.35,
               Color = Colors.Black
            }
         };

         _visuals.Add(_ghost);
      }

      /// <summary>
      /// Moves the shadow to the current cursor position, in the coordinates of the adorned element. The grip
      /// point is subtracted by itself, so the caller only needs to hand over the cursor position.
      /// </summary>
      /// <param name="position">The cursor position relative to the adorned element.</param>
      public void Offset(Point position) {
         _position.X = position.X - _grip.X;
         _position.Y = position.Y - _grip.Y;
      }

      /// <inheritdoc />
      protected override int VisualChildrenCount => _visuals.Count;

      /// <inheritdoc />
      protected override Visual GetVisualChild(int index) => _visuals[index];

      /// <inheritdoc />
      protected override Size MeasureOverride(Size constraint) {
         _ghost.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
         return _ghost.DesiredSize;
      }

      /// <inheritdoc />
      protected override Size ArrangeOverride(Size finalSize) {
         _ghost.Arrange(new Rect(new Point(0, 0), _ghost.DesiredSize));
         return finalSize;
      }
   }
}
