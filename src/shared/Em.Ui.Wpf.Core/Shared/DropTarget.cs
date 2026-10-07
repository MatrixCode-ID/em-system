using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Makes an element able to receive a drop from <see cref="DragSource"/>, just by handing over the
   /// command that does the work in XAML:
   /// <code>&lt;Grid shared:DropTarget.Command="{Binding Commands[GrantDroppedCommand]}"&gt;</code>
   /// <para>
   /// There is no type filtering here: what answers whether a payload may land is the command's own
   /// <c>CanExecute</c>, and that answer also decides the shape of the cursor. Putting the same rule in two
   /// places means two places that can disagree.
   /// </para>
   /// </summary>
   public static class DropTarget
   {
      #region Command

      /// <summary>
      /// The command that runs when a payload is dropped on this element, with the payload as its parameter.
      /// Attaching it also turns on <see cref="UIElement.AllowDrop"/>.
      /// </summary>
      public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
         "Command",
         typeof(ICommand),
         typeof(DropTarget),
         new PropertyMetadata(null, OnCommandChanged));

      /// <summary>Reads the drop receiver command attached to an element.</summary>
      /// <param name="element">The element being read.</param>
      /// <returns>Its command, or <c>null</c> when this element is not a drop place.</returns>
      public static ICommand? GetCommand(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (ICommand?)element.GetValue(CommandProperty);
      }

      /// <summary>Attaches a drop receiver command to an element.</summary>
      /// <param name="element">The element being attached to.</param>
      /// <param name="value">The command; <c>null</c> means this element stops receiving drops.</param>
      public static void SetCommand(DependencyObject element, ICommand? value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(CommandProperty, value);
      }

      #endregion

      #region Target

      /// <summary>
      /// The drop target on this element, usually the <c>{Binding}</c> of a row. When it is set, the
      /// command's parameter becomes a <see cref="DropRequest"/> that carries both the payload and this
      /// target; when empty, the parameter is the payload itself.
      /// </summary>
      public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
         "Target",
         typeof(object),
         typeof(DropTarget),
         new PropertyMetadata(null));

      /// <summary>Reads the drop target of an element.</summary>
      /// <param name="element">The element being read.</param>
      /// <returns>Its target, or <c>null</c>.</returns>
      public static object? GetTarget(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return element.GetValue(TargetProperty);
      }

      /// <summary>Attaches a drop target to an element.</summary>
      /// <param name="element">The element being attached to.</param>
      /// <param name="value">The target.</param>
      public static void SetTarget(DependencyObject element, object? value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(TargetProperty, value);
      }

      #endregion

      #region Effect

      /// <summary>
      /// The cursor shape when a payload may land here: <see cref="DragDropEffects.Copy"/> (default) or e.g.
      /// <see cref="DragDropEffects.Move"/> for a place that moves what is dropped. Display only - what
      /// really happens is still decided by its command.
      /// </summary>
      public static readonly DependencyProperty EffectProperty = DependencyProperty.RegisterAttached(
         "Effect",
         typeof(DragDropEffects),
         typeof(DropTarget),
         new PropertyMetadata(DragDropEffects.Copy));

      /// <summary>Reads the drop cursor shape of an element.</summary>
      /// <param name="element">The element being read.</param>
      /// <returns>The effect that is shown.</returns>
      public static DragDropEffects GetEffect(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (DragDropEffects)element.GetValue(EffectProperty);
      }

      /// <summary>Attaches the drop cursor shape to an element.</summary>
      /// <param name="element">The element being attached to.</param>
      /// <param name="value">The effect that is shown.</param>
      public static void SetEffect(DependencyObject element, DragDropEffects value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(EffectProperty, value);
      }

      #endregion

      #region IsDraggingOver

      private static readonly DependencyPropertyKey IsDraggingOverKey = DependencyProperty.RegisterAttachedReadOnly(
         "IsDraggingOver",
         typeof(bool),
         typeof(DropTarget),
         new PropertyMetadata(false));

      /// <summary>
      /// On while a payload that <i>may</i> land is hovering over this element. Only for XAML to read - this
      /// is what lets the highlight of the drop area be drawn through a trigger, without a single line of the
      /// highlight's state entering the view model.
      /// </summary>
      public static readonly DependencyProperty IsDraggingOverProperty = IsDraggingOverKey.DependencyProperty;

      /// <summary>Whether a payload that may land is hovering over this element.</summary>
      /// <param name="element">The element being read.</param>
      /// <returns><c>true</c> while the payload hovers over this element.</returns>
      public static bool GetIsDraggingOver(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (bool)element.GetValue(IsDraggingOverProperty);
      }

      #endregion

      private static void OnCommandChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
         if (sender is not UIElement element) return;

         element.DragEnter -= OnDragOver;
         element.DragOver -= OnDragOver;
         element.DragLeave -= OnDragLeave;
         element.Drop -= OnDrop;

         element.AllowDrop = e.NewValue != null;
         if (e.NewValue == null) {
            element.SetValue(IsDraggingOverKey, false);
            return;
         }

         element.DragEnter += OnDragOver;
         element.DragOver += OnDragOver;
         element.DragLeave += OnDragLeave;
         element.Drop += OnDrop;
      }

      // DragOver fires continuously while the cursor is over this element, so it is also what reasserts its
      // highlight. That is what suppresses flicker when the cursor crosses the cards inside it: the highlight
      // is turned on again faster than the eye can see it go out.
      private static void OnDragOver(object sender, DragEventArgs e) {
         if (sender is not UIElement element) return;

         var accepted = CanAccept(element, e.Data, out _);

         e.Effects = accepted ? GetEffect(element) : DragDropEffects.None;
         e.Handled = true;
         element.SetValue(IsDraggingOverKey, accepted);
         ClearOuterTargets(element);
      }

      // A target inside another - a folder row inside the list that also takes drops - handles the
      // drag itself, so the outer one hears neither DragOver nor DragLeave while the pointer is over
      // the inner one, and would keep the highlight it had a moment ago. The inner one switches it
      // off on its way through.
      private static void ClearOuterTargets(DependencyObject element) {
         for (var node = VisualTreeHelper.GetParent(element); node != null; node = VisualTreeHelper.GetParent(node)) {
            if (node.GetValue(CommandProperty) != null) node.SetValue(IsDraggingOverKey, false);
         }
      }

      // DragLeave also fires when the cursor merely moves to a child of the element, so leaving is confirmed
      // from the cursor's own coordinates - not from the event alone.
      private static void OnDragLeave(object sender, DragEventArgs e) {
         if (sender is not FrameworkElement element) return;

         var cursor = e.GetPosition(element);
         if (cursor.X >= 0 && cursor.Y >= 0 &&
             cursor.X <= element.ActualWidth && cursor.Y <= element.ActualHeight) {
            return;
         }

         element.SetValue(IsDraggingOverKey, false);
         e.Handled = true;
      }

      private static void OnDrop(object sender, DragEventArgs e) {
         if (sender is not UIElement element) return;

         element.SetValue(IsDraggingOverKey, false);
         ClearOuterTargets(element);

         if (!CanAccept(element, e.Data, out var payload)) {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
         }

         e.Effects = GetEffect(element);
         e.Handled = true;
         GetCommand(element)?.Execute(payload);
      }

      private static bool CanAccept(UIElement element, IDataObject? data, out object? payload) {
         payload = null;

         if (data == null) return false;

         object? dropped = null;
         if (data.GetDataPresent(DragSource.PayloadFormat)) {
            // A drag of our own that also carries virtual files runs over a COM data object, which
            // cannot hand back the .NET payload - DragSource keeps it for exactly this read.
            dropped = DragSource.ActivePayload ?? data.GetData(DragSource.PayloadFormat);
         }
         else if (data.GetDataPresent(DataFormats.FileDrop) &&
                  data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) {
            dropped = new DroppedFiles(paths);
         }

         if (dropped == null) return false;

         payload = GetTarget(element) is { } target ? new DropRequest(dropped, target) : dropped;
         return GetCommand(element) is { } command && command.CanExecute(payload);
      }
   }
}
