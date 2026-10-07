using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Color = System.Windows.Media.Color;
using ListBox = System.Windows.Controls.ListBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// Drag behavior for the tab strip of <see cref="TabbedMainWindow"/>: a tab is moved left/right in its
   /// own strip, dragged onto the tab strip of another window, or dragged out and released on empty space
   /// to become a new window. Installed in XAML through
   /// <c>local:TabbedMainWindowTabDrag.IsEnabled="True"</c> on the <see cref="ListBox"/> of the tab strip.
   /// </summary>
   public static class TabbedMainWindowTabDrag
   {
      // How far past the strip's top or bottom edge the pointer has to go before the tab counts as
      // pulled out of it.
      private const double TearOffDistance = 24;

      /// <summary>Turns on the tab drag behavior on a tab strip.</summary>
      public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
         "IsEnabled", typeof(bool), typeof(TabbedMainWindowTabDrag),
         new PropertyMetadata(false, IsEnabledChanged));

      /// <summary>Reads <see cref="IsEnabledProperty"/>.</summary>
      public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

      /// <summary>Mengisi <see cref="IsEnabledProperty"/>.</summary>
      public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

      // Every live strip, so a tab dragged out of one window can be dropped onto another.
      private static readonly List<ListBox> Strips = [];

      private static DragState? _drag;

      private sealed class DragState(ListBox strip, ListBoxItem item, TabbedMainWindowTab tab, Point start) {
         public ListBox Strip { get; } = strip;
         // Holds the mouse capture during a drag. Not the strip itself: a ListBox that has the capture
         // selects whatever item the pointer passes over, which would switch tabs mid-drag.
         public ListBoxItem Item { get; set; } = item;
         public TabbedMainWindowTab Tab { get; } = tab;
         public Point Start { get; } = start;
         public bool Started { get; set; }
         public bool Outside { get; set; }
         public bool Finishing { get; set; }
         public bool Moving { get; set; }
         public Popup? Ghost { get; set; }
      }

      private static void IsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
         if (d is not ListBox strip) return;

         if ((bool)e.NewValue) {
            strip.Loaded += StripLoaded;
            strip.Unloaded += StripUnloaded;
            strip.PreviewMouseLeftButtonDown += StripMouseDown;
            strip.PreviewMouseMove += StripMouseMove;
            strip.PreviewMouseLeftButtonUp += StripMouseUp;
            strip.LostMouseCapture += StripLostCapture;
            if (strip.IsLoaded) Strips.Add(strip);
         }
         else {
            strip.Loaded -= StripLoaded;
            strip.Unloaded -= StripUnloaded;
            strip.PreviewMouseLeftButtonDown -= StripMouseDown;
            strip.PreviewMouseMove -= StripMouseMove;
            strip.PreviewMouseLeftButtonUp -= StripMouseUp;
            strip.LostMouseCapture -= StripLostCapture;
            Strips.Remove(strip);
         }
      }

      private static void StripLoaded(object sender, RoutedEventArgs e) {
         var strip = (ListBox)sender;
         if (!Strips.Contains(strip)) Strips.Add(strip);
      }

      private static void StripUnloaded(object sender, RoutedEventArgs e) => Strips.Remove((ListBox)sender);

      private static void StripMouseDown(object sender, MouseButtonEventArgs e) {
         var strip = (ListBox)sender;
         // The close button inside a tab answers its own click; pressing it is never the start of a drag.
         if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject, strip) != null) return;
         if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject, strip) is not { } item
             || item.DataContext is not TabbedMainWindowTab tab) return;

         _drag = new DragState(strip, item, tab, e.GetPosition(strip));
      }

      private static void StripMouseMove(object sender, MouseEventArgs e) {
         var strip = (ListBox)sender;
         if (_drag == null || _drag.Strip != strip) return;

         if (e.LeftButton != MouseButtonState.Pressed) {
            Cancel();
            return;
         }

         var position = e.GetPosition(strip);
         if (!_drag.Started) {
            if (Math.Abs(position.X - _drag.Start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - _drag.Start.Y) < SystemParameters.MinimumVerticalDragDistance)
               return;

            _drag.Started = true;
            _drag.Item.CaptureMouse();
         }

         var vm = VmOf(strip);
         if (vm == null) return;

         var outside = position.Y < -TearOffDistance || position.Y > strip.ActualHeight + TearOffDistance;
         // The last tab of the main window stays where it is; it can still be reordered (a no-op), but
         // it is never pulled out.
         if (outside && !vm.CanMoveOut(_drag.Tab)) outside = false;

         _drag.Outside = outside;
         if (outside) {
            ShowGhost(strip, ToDip(strip, strip.PointToScreen(position)));
            return;
         }

         HideGhost();
         var index = ReorderIndex(strip, _drag.Tab, position.X);
         if (index >= 0) {
            var drag = _drag;
            drag.Moving = true;
            vm.MoveTab(drag.Tab, index);
            strip.UpdateLayout();
            drag.Moving = false;

            // The strip may hand the moved tab a new container, and the capture does not follow it.
            if (strip.ItemContainerGenerator.ContainerFromItem(drag.Tab) is ListBoxItem item && item != drag.Item)
               drag.Item = item;
            if (!drag.Item.IsMouseCaptured) drag.Item.CaptureMouse();
         }
         e.Handled = true;
      }

      private static void StripMouseUp(object sender, MouseButtonEventArgs e) {
         var strip = (ListBox)sender;
         if (_drag == null || _drag.Strip != strip) return;

         var drag = _drag;
         if (!drag.Started) {
            _drag = null;
            return;
         }

         var screen = strip.PointToScreen(e.GetPosition(strip));
         drag.Finishing = true;
         drag.Item.ReleaseMouseCapture();
         HideGhost();
         _drag = null;
         e.Handled = true;

         if (!drag.Outside) return;

         // Deferred: the drop may close the window this very mouse event is still being delivered to.
         strip.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Drop(strip, drag.Tab, screen));
      }

      // Only the dragged tab losing the capture ends the drag: the strip itself also reports losing it
      // the moment the tab takes it over.
      private static void StripLostCapture(object sender, MouseEventArgs e) {
         if (_drag is { Started: true, Finishing: false, Moving: false } drag
             && drag.Strip == sender && e.OriginalSource == drag.Item)
            Cancel();
      }

      private static void Cancel() {
         if (_drag == null) return;

         var drag = _drag;
         _drag = null;
         HideGhost(drag);
         if (drag.Started && drag.Item.IsMouseCaptured) {
            drag.Finishing = true;
            drag.Item.ReleaseMouseCapture();
         }
      }

      // screen is in device pixels: that is what PointFromScreen expects on every strip, whatever the
      // DPI of the monitor it sits on.
      private static void Drop(ListBox source, TabbedMainWindowTab tab, Point screen) {
         if (Window.GetWindow(source) is not TabbedMainWindow sourceWindow) return;
         var sourceVm = sourceWindow.Vm;
         if (!sourceVm.Tabs.Contains(tab)) return;

         var target = Strips.FirstOrDefault(s => s != source && IsOverStrip(s, screen));
         if (target != null) {
            if (Window.GetWindow(target) is not TabbedMainWindow targetWindow || !targetWindow.Vm.CanAcceptTab(tab))
               return;

            var index = InsertIndex(target, target.PointFromScreen(screen).X);
            _ = sourceWindow.MoveTabToAsync(tab, targetWindow, index);
            return;
         }

         // A lone tab may still join another window, but never becomes a window of its own again.
         if (!sourceVm.CanTearOff(tab)) return;

         _ = sourceWindow.TearOffAsync(tab, ToDip(source, screen));
      }

      private static bool IsOverStrip(ListBox strip, Point screen) {
         if (!strip.IsVisible || PresentationSource.FromVisual(strip) == null) return false;

         var local = strip.PointFromScreen(screen);
         // The strip hugs its tabs, so the empty rest of its column counts as well - dropping there
         // appends the tab. The column ends where the tab list button and the toolbar begin.
         var width = Math.Max(strip.ActualWidth, LayoutInformation.GetLayoutSlot(strip).Width);
         return local.Y >= 0 && local.Y <= strip.ActualHeight && local.X >= 0 && local.X <= width;
      }

      // The index the dragged tab should move to, or -1 to stay. A neighbour is only overtaken once
      // the pointer has gone far enough into it that the dragged tab, placed there, would still sit
      // under the pointer - otherwise tabs of different widths keep swapping back and forth.
      private static int ReorderIndex(ListBox strip, TabbedMainWindowTab tab, double x) {
         var from = strip.Items.IndexOf(tab);
         var dragged = Bounds(strip, from);
         if (dragged == null) return -1;

         for (var i = 0; i < strip.Items.Count; i++) {
            if (i == from || Bounds(strip, i) is not { } other) continue;
            if (x < other.Left || x > other.Right) continue;

            if (i > from && x > other.Right - dragged.Value.Width) return i;
            if (i < from && x < other.Left + dragged.Value.Width) return i;
         }

         return -1;
      }

      private static int InsertIndex(ListBox strip, double x) {
         for (var i = 0; i < strip.Items.Count; i++) {
            if (Bounds(strip, i) is { } bounds && x < bounds.Left + bounds.Width / 2) return i;
         }

         return strip.Items.Count;
      }

      private static Rect? Bounds(ListBox strip, int index) {
         if (index < 0 || strip.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement item)
            return null;

         return item.TransformToAncestor(strip).TransformBounds(new Rect(item.RenderSize));
      }

      private static void ShowGhost(ListBox strip, Point dip) {
         if (_drag == null) return;

         _drag.Ghost ??= CreateGhost(strip, _drag.Tab);
         _drag.Ghost.HorizontalOffset = dip.X + 12;
         _drag.Ghost.VerticalOffset = dip.Y + 12;
         _drag.Ghost.IsOpen = true;
      }

      private static void HideGhost() {
         if (_drag != null) HideGhost(_drag);
      }

      private static void HideGhost(DragState drag) {
         if (drag.Ghost != null) drag.Ghost.IsOpen = false;
      }

      // The ghost borrows the window's own background and foreground, so it follows the theme without
      // naming a colour of either.
      private static Popup CreateGhost(ListBox strip, TabbedMainWindowTab tab) {
         var window = Window.GetWindow(strip);
         var card = new Border {
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(8),
            Background = window?.Background,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Opacity = 0.92,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.3 },
            Child = new TextBlock {
               Text = tab.Title,
               FontSize = 13,
               FontWeight = FontWeights.SemiBold,
               MaxWidth = 240,
               TextTrimming = TextTrimming.CharacterEllipsis,
               Foreground = window?.Foreground
            }
         };

         return new Popup {
            Child = card,
            Placement = PlacementMode.AbsolutePoint,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Focusable = false
         };
      }

      private static TabbedMainWindowVm? VmOf(ListBox strip) => (Window.GetWindow(strip) as TabbedMainWindow)?.Vm;

      private static Point ToDip(Visual visual, Point device) =>
         PresentationSource.FromVisual(visual)?.CompositionTarget?.TransformFromDevice.Transform(device) ?? device;

      private static T? FindAncestor<T>(DependencyObject? element, DependencyObject stop) where T : DependencyObject {
         while (element != null && element != stop) {
            if (element is T match) return match;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
               ? VisualTreeHelper.GetParent(element)
               : LogicalTreeHelper.GetParent(element);
         }

         return null;
      }
   }
}
