using System.Windows.Input;

namespace Em.Ui.Maui.Controls;

/// <summary>The drop position reported by a <see cref="HoldDragButton"/> in list reorder mode.</summary>
/// <param name="Target">The zero-based index the held item takes inside its bounded list.</param>
public sealed record HoldDragReorder(int Target);

/// <summary>A native button that reserves a held vertical drag for reordering instead of activation.</summary>
/// <remarks>
/// Two modes exist. Without <see cref="ReorderCount"/> the held drag only chooses before or after
/// (<see cref="MoveBeforeCommand"/>/<see cref="MoveAfterCommand"/>). With <see cref="ReorderCount"/> above one the
/// button belongs to a bounded list: the dragged visual follows the finger but never leaves the first and last
/// position of that list, the rows it passes move aside to show where it lands, and <see cref="ReorderCommand"/>
/// receives the final position on release.
/// </remarks>
public class HoldDragButton : Button
{
   /// <summary>Enables held reordering while preserving ordinary tap and keyboard activation.</summary>
   public static readonly BindableProperty CanReorderProperty = BindableProperty.Create(nameof(CanReorder), typeof(bool), typeof(HoldDragButton), false,
      propertyChanged: (b, _, n) => { if (!(bool)n) ((HoldDragButton)b).CancelInteraction(); });
   /// <summary>The command invoked after a held upward drag.</summary>
   public static readonly BindableProperty MoveBeforeCommandProperty = BindableProperty.Create(nameof(MoveBeforeCommand), typeof(ICommand), typeof(HoldDragButton));
   /// <summary>The command invoked after a held downward drag.</summary>
   public static readonly BindableProperty MoveAfterCommandProperty = BindableProperty.Create(nameof(MoveAfterCommand), typeof(ICommand), typeof(HoldDragButton));
   /// <summary>The visual receiving temporary held feedback.</summary>
   public static readonly BindableProperty DragTargetProperty = BindableProperty.Create(nameof(DragTarget), typeof(VisualElement), typeof(HoldDragButton));
   /// <summary>An identity token whose replacement cancels an in-progress touch.</summary>
   public static readonly BindableProperty InteractionScopeProperty = BindableProperty.Create(nameof(InteractionScope), typeof(object), typeof(HoldDragButton),
      propertyChanged: (b, _, _) => ((HoldDragButton)b).CancelInteraction());
   /// <summary>The position of this item inside its bounded list.</summary>
   public static readonly BindableProperty ReorderIndexProperty = BindableProperty.Create(nameof(ReorderIndex), typeof(int), typeof(HoldDragButton), 0);
   /// <summary>The number of items in the bounded list; one or less keeps the before/after mode.</summary>
   public static readonly BindableProperty ReorderCountProperty = BindableProperty.Create(nameof(ReorderCount), typeof(int), typeof(HoldDragButton), 0);
   /// <summary>The command receiving a <see cref="HoldDragReorder"/> when released over another position.</summary>
   public static readonly BindableProperty ReorderCommandProperty = BindableProperty.Create(nameof(ReorderCommand), typeof(ICommand), typeof(HoldDragButton));
   /// <summary>The whole list row whose height is the pitch between positions; defaults to <see cref="DragTarget"/>.</summary>
   public static readonly BindableProperty ReorderItemProperty = BindableProperty.Create(nameof(ReorderItem), typeof(VisualElement), typeof(HoldDragButton));
   /// <inheritdoc cref="CanReorderProperty"/>
   public bool CanReorder { get => (bool)GetValue(CanReorderProperty); set => SetValue(CanReorderProperty, value); }
   /// <inheritdoc cref="MoveBeforeCommandProperty"/>
   public ICommand? MoveBeforeCommand { get => (ICommand?)GetValue(MoveBeforeCommandProperty); set => SetValue(MoveBeforeCommandProperty, value); }
   /// <inheritdoc cref="MoveAfterCommandProperty"/>
   public ICommand? MoveAfterCommand { get => (ICommand?)GetValue(MoveAfterCommandProperty); set => SetValue(MoveAfterCommandProperty, value); }
   /// <inheritdoc cref="DragTargetProperty"/>
   public VisualElement? DragTarget { get => (VisualElement?)GetValue(DragTargetProperty); set => SetValue(DragTargetProperty, value); }
   /// <inheritdoc cref="InteractionScopeProperty"/>
   public object? InteractionScope { get => GetValue(InteractionScopeProperty); set => SetValue(InteractionScopeProperty, value); }
   /// <inheritdoc cref="ReorderIndexProperty"/>
   public int ReorderIndex { get => (int)GetValue(ReorderIndexProperty)!; set => SetValue(ReorderIndexProperty, value); }
   /// <inheritdoc cref="ReorderCountProperty"/>
   public int ReorderCount { get => (int)GetValue(ReorderCountProperty)!; set => SetValue(ReorderCountProperty, value); }
   /// <inheritdoc cref="ReorderCommandProperty"/>
   public ICommand? ReorderCommand { get => (ICommand?)GetValue(ReorderCommandProperty); set => SetValue(ReorderCommandProperty, value); }
   /// <inheritdoc cref="ReorderItemProperty"/>
   public VisualElement? ReorderItem { get => (VisualElement?)GetValue(ReorderItemProperty); set => SetValue(ReorderItemProperty, value); }

   private bool IsListMode => ReorderCount > 1 && ReorderIndex >= 0 && ReorderIndex < ReorderCount;

#if ANDROID
   private Android.Views.View? _native;
   private IDispatcherTimer? _holdTimer;
   private bool _tracking, _holding, _tapCandidate;
   private float _startX, _startY, _lastY, _density;
   private long _startedAt;
   private object? _scopeAtDown;
   private VisualElement? _feedbackTarget;
   private double _originalOpacity, _originalY;
   // List mode state, captured when holding begins so a rebinding row cannot change the bounds mid-drag.
   private bool _listMode;
   private int _listIndex, _listCount, _listTarget;
   private double _step;
   // Rows are moved on the list's own item views: a child moved past its row wrapper would be clipped.
   private AndroidX.RecyclerView.Widget.RecyclerView? _list;
   private Android.Views.View? _liftedRow;
   private int _listPosition;
   private float _originalZ, _rowY;
   private readonly List<(Android.Views.View View, float Y)> _shifted = [];

   // A virtualized row can keep receiving binding updates after its handler has
   // disconnected and disposed the Java peer. A cached view is not proof of life.
   private Android.Views.View? CurrentNative => _native is { Handle: var handle } native
      && handle != IntPtr.Zero && ReferenceEquals(Handler?.PlatformView, native) ? native : null;

   /// <inheritdoc />
   protected override void OnHandlerChanging(HandlerChangingEventArgs args) {
      CancelInteraction();
      if (_native is { Handle: var handle } native && handle != IntPtr.Zero) native.Touch -= OnNativeTouch;
      _native = null;
      base.OnHandlerChanging(args);
   }
   /// <inheritdoc />
   protected override void OnHandlerChanged() {
      base.OnHandlerChanged();
      if (Handler?.PlatformView is Android.Views.View native) { _native = native; native.Touch += OnNativeTouch; }
   }
   private void OnNativeTouch(object? sender, Android.Views.View.TouchEventArgs args) {
      if (args.Event is not { } touch || CurrentNative is not { } native || !ReferenceEquals(sender, native)) return;
      args.Handled = true;
      switch (touch.ActionMasked) {
         case Android.Views.MotionEventActions.Down:
            CancelInteraction();
            _tracking = _tapCandidate = true; _startX = touch.RawX; _startY = _lastY = touch.RawY;
            _startedAt = Environment.TickCount64; _scopeAtDown = InteractionScope;
            _density = native.Resources?.DisplayMetrics?.Density ?? 1f;
            native.Pressed = true;
            if (CanReorder) {
               if (_holdTimer is null) {
                  _holdTimer = Dispatcher.CreateTimer(); _holdTimer.IsRepeating = false;
                  _holdTimer.Tick += (_, _) => BeginHolding();
               }
               _holdTimer.Interval = TimeSpan.FromMilliseconds(Android.Views.ViewConfiguration.LongPressTimeout);
               _holdTimer.Start();
            }
            break;
         case Android.Views.MotionEventActions.Move:
            if (!_tracking) break;
            _lastY = touch.RawY;
            if (!_holding && (Math.Abs(touch.RawX - _startX) > 8 * _density || Math.Abs(touch.RawY - _startY) > 8 * _density)) {
               _holdTimer?.Stop(); _tapCandidate = false; native.Pressed = false;
            }
            if (_holding && _feedbackTarget is { } target) {
               var delta = (_lastY - _startY) / _density;
               if (_listMode) {
                  // The card stays between the first and last row of its own list.
                  var offset = Math.Clamp(delta, -_listIndex * _step, (_listCount - 1 - _listIndex) * _step);
                  if (Alive(_liftedRow) is { } row) row.TranslationY = _rowY + (float)(offset * _density);
                  else target.TranslationY = _originalY + offset;
                  var covered = Math.Clamp(_listIndex + (int)Math.Round(offset / _step), 0, _listCount - 1);
                  if (covered != _listTarget) { _listTarget = covered; ShiftNeighbours(); }
               }
               else target.TranslationY = _originalY + Math.Clamp(delta, -24, 24);
            }
            break;
         case Android.Views.MotionEventActions.Up:
            var released = (touch.RawY - _startY) / _density;
            var sameScope = ReferenceEquals(_scopeAtDown, InteractionScope);
            var held = _tracking && _holding && sameScope && CanReorder;
            var listMode = _listMode; var listIndex = _listIndex; var drop = new HoldDragReorder(_listTarget);
            var move = held && !listMode && Math.Abs(released) >= 32 ? released < 0 ? MoveBeforeCommand : MoveAfterCommand : null;
            // Without reordering a long press is still an ordinary activation, as on a plain button.
            var tap = _tracking && _tapCandidate && !_holding && sameScope && (!CanReorder || Environment.TickCount64 - _startedAt < Android.Views.ViewConfiguration.LongPressTimeout);
            if (held && listMode && drop.Target != listIndex && ReorderCommand is { } reorder && reorder.CanExecute(drop)) {
               // The rows already show the new order. Resetting them and moving the items in the same message
               // lets the next frame draw the final order directly, without a frame of the old order and
               // without the item animator sliding the rows back from their old slots.
               var list = Alive(_list); var animator = list?.GetItemAnimator();
               list?.SetItemAnimator(null);
               CancelInteraction();
               reorder.Execute(drop);
               if (list is not null) list.PostDelayed(() => { if (Alive(list) is { } live && live.GetItemAnimator() is null) live.SetItemAnimator(animator); }, 300);
               break;
            }
            CancelInteraction();
            if (held && listMode) break;
            if (move?.CanExecute(null) == true) move.Execute(null);
            else if (tap && Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
            break;
         case Android.Views.MotionEventActions.Cancel:
            CancelInteraction(); break;
      }
   }
   private void BeginHolding() {
      if (!_tracking || !_tapCandidate || !CanReorder || CurrentNative is not { IsAttachedToWindow: true } native || !ReferenceEquals(_scopeAtDown, InteractionScope)) return;
      _holding = true; _tapCandidate = false;
      native.Parent?.RequestDisallowInterceptTouchEvent(true);
      native.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
      _feedbackTarget = DragTarget;
      if (_feedbackTarget is { } target) { _originalOpacity = target.Opacity; _originalY = target.TranslationY; target.Opacity *= 0.75; }
      _step = (ReorderItem ?? DragTarget)?.Height ?? 0;
      _listMode = IsListMode && _step > 0;
      if (!_listMode) return;
      _listIndex = _listTarget = ReorderIndex; _listCount = ReorderCount; LiftRow();
      // The list item itself is the exact pitch between neighbouring rows.
      if (Alive(_liftedRow) is { Height: > 0 } row) _step = row.Height / _density;
   }
   // A recycled list draws its rows in order, so the dragged row is raised above its neighbours while it moves.
   private void LiftRow() {
      for (var view = (ReorderItem ?? DragTarget)?.Handler?.PlatformView as Android.Views.View; view?.Parent is Android.Views.View parent; view = parent) {
         if (parent is not AndroidX.RecyclerView.Widget.RecyclerView list) continue;
         var position = list.GetChildAdapterPosition(view);
         if (position < 0) return;
         _list = list; _liftedRow = view; _listPosition = position; _rowY = view.TranslationY;
         _originalZ = view.TranslationZ; view.TranslationZ = _originalZ + 8 * _density;
         return;
      }
   }
   // Moves the rows between the held row and the position it covers by one row, so the gap shows where it lands.
   private void ShiftNeighbours() {
      ResetNeighbours();
      if (Alive(_list) is not { } list) return;
      for (var i = 0; i < _listCount; i++) {
         var shift = _listIndex < _listTarget && i > _listIndex && i <= _listTarget ? -_step
            : _listTarget < _listIndex && i >= _listTarget && i < _listIndex ? _step : 0;
         if (shift == 0 || Alive(list.FindViewHolderForAdapterPosition(_listPosition - _listIndex + i)?.ItemView) is not { } view) continue;
         _shifted.Add((view, view.TranslationY)); view.TranslationY += (float)(shift * _density);
      }
   }
   private void ResetNeighbours() {
      foreach (var (view, y) in _shifted) if (Alive(view) is { } live) live.TranslationY = y;
      _shifted.Clear();
   }
   private static T? Alive<T>(T? view) where T : Java.Lang.Object => view is { Handle: var handle } && handle != IntPtr.Zero ? view : null;
#endif
   private void CancelInteraction() {
#if ANDROID
      _holdTimer?.Stop();
      if (CurrentNative is { } native) { native.Pressed = false; native.Parent?.RequestDisallowInterceptTouchEvent(false); }
      if (_feedbackTarget is { } target) { target.Opacity = _originalOpacity; target.TranslationY = _originalY; }
      if (Alive(_liftedRow) is { } row) { row.TranslationY = _rowY; row.TranslationZ = _originalZ; }
      ResetNeighbours();
      _list = null; _liftedRow = null; _listMode = false;
      _feedbackTarget = null; _tracking = _holding = _tapCandidate = false; _scopeAtDown = null;
#endif
   }
}
