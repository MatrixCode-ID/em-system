using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using GiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Makes an element draggable, just by attaching its payload in XAML:
   /// <code>&lt;Grid shared:DragSource.Payload="{Binding}"&gt;</code>
   /// The element itself becomes the shadow that is dragged along, and its payload is handed as-is to the
   /// place where it is dropped.
   /// <para>
   /// This class does not know what the payload contains. What filters which payload may be accepted is
   /// the command on the receiving side (<see cref="DropTarget"/>), not here - one rule in one place.
   /// </para>
   /// </summary>
   public static class DragSource
   {
      /// <summary>
      /// The single format name used by all drags in this application. One name for all payload types, so
      /// the receiver only needs to open one door and then check the content itself.
      /// </summary>
      public const string PayloadFormat = "Em.Ui.Wpf.DragPayload";

      // Stored once, not recomposed every time it is attached and detached: AddHandler and RemoveHandler
      // match the delegate, and handing over the method group twice produces two different delegate objects
      // - the second would never remove the first.
      private static readonly MouseButtonEventHandler ArmHandler = OnPreviewMouseLeftButtonDown;
      private static readonly MouseEventHandler MoveHandler = OnMouseMove;

      // There is only one drag at a time, so the state is static. Keeping it per element would mean paying
      // one attached property for something that cannot happen twice.
      private static FrameworkElement? _armed;
      private static Point _origin;
      private static Point _grip;
      private static bool _dragging;
      private static DragGhostAdorner? _ghost;
      private static FrameworkElement? _ghostHost;

      /// <summary>
      /// The payload of the drag that is running from this application, or <c>null</c> when there is none.
      /// <see cref="DropTarget"/> reads it from here, not from the drag data, because a payload that is also
      /// carried out of the application as a file (<see cref="IVirtualFileSource"/>) travels through a COM
      /// data object that cannot carry a .NET object as-is.
      /// </summary>
      public static object? ActivePayload { get; private set; }

      #region Payload

      /// <summary>
      /// The payload this element carries when it is dragged. Attaching it also turns on all the drag
      /// plumbing on that element; removing it (<c>null</c>) turns it off again.
      /// </summary>
      public static readonly DependencyProperty PayloadProperty = DependencyProperty.RegisterAttached(
         "Payload",
         typeof(object),
         typeof(DragSource),
         new PropertyMetadata(null, OnPayloadChanged));

      /// <summary>Reads the payload attached to an element.</summary>
      /// <param name="element">The element being read.</param>
      /// <returns>Its payload, or <c>null</c> when the element cannot be dragged.</returns>
      public static object? GetPayload(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return element.GetValue(PayloadProperty);
      }

      /// <summary>Attaches a payload to an element.</summary>
      /// <param name="element">The element being attached to.</param>
      /// <param name="value">The payload; <c>null</c> means this element cannot be dragged.</param>
      public static void SetPayload(DependencyObject element, object? value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(PayloadProperty, value);
      }

      #endregion

      #region IsDragActive

      /// <summary>
      /// On at the root of the screen while a drag is running, and inherited by all its content. Read by
      /// elements that need to step aside during a drag - for example the scrim of a side sheet, which must
      /// stop capturing the mouse so the drop can land behind it - without that element needing to know who
      /// is being dragged.
      /// </summary>
      public static readonly DependencyProperty IsDragActiveProperty = DependencyProperty.RegisterAttached(
         "IsDragActive",
         typeof(bool),
         typeof(DragSource),
         new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

      /// <summary>Whether a drag is running over this element.</summary>
      /// <param name="element">The element being read.</param>
      /// <returns><c>true</c> while a drag is running.</returns>
      public static bool GetIsDragActive(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (bool)element.GetValue(IsDragActiveProperty);
      }

      /// <summary>Turns the drag marker on or off on an element.</summary>
      /// <param name="element">The element it is attached to - usually the root of the screen.</param>
      /// <param name="value">The state.</param>
      public static void SetIsDragActive(DependencyObject element, bool value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(IsDragActiveProperty, value);
      }

      #endregion

      private static void OnPayloadChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
         if (sender is not FrameworkElement element) return;

         // Detached first unconditionally: the payload of a row changes every time its DataContext changes, and
         // subscribing twice would mean the drag starts twice.
         element.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ArmHandler);
         element.RemoveHandler(UIElement.MouseMoveEvent, MoveHandler);

         if (e.NewValue == null) return;

         // handledEventsToo, not a plain '+='. The header of a collapsible card is a ToggleButton, and
         // ToggleButton marks MouseMove as already handled while it is pressed. With an ordinary subscription,
         // a card grabbed by its header would never start moving - and the header is precisely the one part of
         // the card that is not covered by claim rows.
         element.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, ArmHandler, true);
         element.AddHandler(UIElement.MouseMoveEvent, MoveHandler, true);
      }

      // Preview, not bubbling: the header of a collapsible card is a ToggleButton, and ToggleButton swallows
      // the ordinary MouseLeftButtonDown. Tunneling from the outside in means the innermost element is the
      // last to register itself - and the innermost is the one the person most precisely meant.
      private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
         if (_dragging || sender is not FrameworkElement element) return;

         _armed = element;
         _grip = e.GetPosition(element);
         _origin = e.GetPosition(null);
      }

      // Bubbling, not preview: a claim row sits inside a module card and both carry a payload, and rising from
      // the inside out means the row wins. What decides who departs is not e.Handled but _armed - the
      // innermost element registered when the button was pressed. Because the subscription receives events
      // that are already handled, e.Handled no longer stops anyone here, and _armed is indeed the better
      // guard: it answers "this is the one meant", not merely "someone already took care of it".
      private static void OnMouseMove(object sender, MouseEventArgs e) {
         if (_dragging || sender is not FrameworkElement element) return;
         if (!ReferenceEquals(_armed, element)) return;

         if (e.LeftButton != MouseButtonState.Pressed) {
            _armed = null;
            return;
         }

         var current = e.GetPosition(null);
         if (Math.Abs(current.X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
             Math.Abs(current.Y - _origin.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
         }

         if (GetPayload(element) is not { } payload) {
            _armed = null;
            return;
         }

         StartDrag(element, payload);
      }

      private static void StartDrag(FrameworkElement source, object payload) {
         var host = FindAdornerHost(source);
         var layer = host == null ? null : AdornerLayer.GetAdornerLayer(host);

         _armed = null;
         _dragging = true;

         try {
            // ToggleButton holds the mouse capture while it is pressed, and starting a drag while it still holds
            // the capture leaves its button stuck in the pressed state. Releasing it here also makes
            // drag-then-release not trigger its Click, while an ordinary click - which never reaches here - still
            // works as usual.
            Mouse.Capture(null);

            if (host != null && layer != null) {
               _ghostHost = host;
               _ghost = new DragGhostAdorner(host, source, _grip);
               layer.Add(_ghost);
               SetIsDragActive(host, true);
            }

            ActivePayload = payload;

            if (payload is IVirtualFileSource virtualFiles) {
               // Copy for the outside world, Move for a target inside the application that relocates
               // what it receives: the target picks, and OLE only lets it pick from what is offered.
               NativeDragDrop.Run(new VirtualFileDataObject(virtualFiles, PayloadFormat),
                  NativeDragDrop.DropEffectCopy | NativeDragDrop.DropEffectMove, MoveGhost);
            }
            else {
               source.GiveFeedback += OnGiveFeedback;
               DragDrop.DoDragDrop(source, new DataObject(PayloadFormat, payload), DragDropEffects.Copy);
            }
         }
         finally {
            source.GiveFeedback -= OnGiveFeedback;
            ActivePayload = null;

            if (_ghost != null) layer?.Remove(_ghost);
            if (host != null) SetIsDragActive(host, false);

            _ghost = null;
            _ghostHost = null;
            _dragging = false;
         }
      }

      // DragOver only fires over a valid target, so it is not enough to move the shadow wherever the cursor
      // goes. GiveFeedback fires at the source, continuously, throughout the drag - but it does not carry
      // the cursor position, so the position is asked from Windows.
      private static void OnGiveFeedback(object sender, GiveFeedbackEventArgs e) {
         e.UseDefaultCursors = false;
         e.Handled = true;

         Mouse.SetCursor(e.Effects == DragDropEffects.None ? Cursors.No : Cursors.Arrow);
         MoveGhost();
      }

      // The ghost lives in the screen's adorner layer, so beyond the screen's edge it would only
      // be clipped against it; it is hidden there instead, which is also what a drag heading out to
      // another application looks like.
      private static void MoveGhost() {
         if (_ghost == null || _ghostHost == null) return;
         if (!GetCursorPos(out var cursor)) return;

         var point = _ghostHost.PointFromScreen(new Point(cursor.X, cursor.Y));
         var inside = point.X >= 0 && point.Y >= 0 &&
                      point.X <= _ghostHost.ActualWidth && point.Y <= _ghostHost.ActualHeight;
         _ghost.Visibility = inside ? Visibility.Visible : Visibility.Hidden;
         _ghost.Offset(point);
      }

      // The shadow is drawn in the adorner layer of the screen that holds the element, not of the window:
      // the source and destination of a drag are inside one screen, and choosing the screen keeps the
      // coordinates simple.
      private static FrameworkElement? FindAdornerHost(DependencyObject source) {
         for (var node = source; node != null; node = VisualTreeHelper.GetParent(node)) {
            if (node is UserControl or Window) return (FrameworkElement)node;
         }

         return null;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct NativePoint
      {
         public int X;
         public int Y;
      }

      [DllImport("user32.dll", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      private static extern bool GetCursorPos(out NativePoint point);
   }
}
