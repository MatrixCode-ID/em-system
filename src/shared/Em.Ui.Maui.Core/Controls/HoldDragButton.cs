using System.Windows.Input;

namespace Em.Ui.Maui.Controls;

/// <summary>A native button that reserves a held vertical drag for reordering instead of activation.</summary>
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

#if ANDROID
   private Android.Views.View? _native;
   private IDispatcherTimer? _holdTimer;
   private bool _tracking, _holding, _tapCandidate;
   private float _startX, _startY, _lastY, _density;
   private long _startedAt;
   private object? _scopeAtDown;
   private VisualElement? _feedbackTarget;
   private double _originalOpacity, _originalY;

   /// <inheritdoc />
   protected override void OnHandlerChanging(HandlerChangingEventArgs args) {
      CancelInteraction();
      if (_native is not null) _native.Touch -= OnNativeTouch;
      _native = null;
      base.OnHandlerChanging(args);
   }
   /// <inheritdoc />
   protected override void OnHandlerChanged() {
      base.OnHandlerChanged();
      if (Handler?.PlatformView is Android.Views.View native) { _native = native; native.Touch += OnNativeTouch; }
   }
   private void OnNativeTouch(object? sender, Android.Views.View.TouchEventArgs args) {
      if (args.Event is not { } touch || _native is not { } native) return;
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
            if (_holding && _feedbackTarget is { } target) target.TranslationY = _originalY + Math.Clamp((_lastY - _startY) / _density, -24, 24);
            break;
         case Android.Views.MotionEventActions.Up:
            var delta = (touch.RawY - _startY) / _density;
            var sameScope = ReferenceEquals(_scopeAtDown, InteractionScope);
            var move = _tracking && _holding && sameScope && CanReorder && Math.Abs(delta) >= 32
               ? delta < 0 ? MoveBeforeCommand : MoveAfterCommand : null;
            var tap = _tracking && _tapCandidate && !_holding && sameScope && Environment.TickCount64 - _startedAt < Android.Views.ViewConfiguration.LongPressTimeout;
            CancelInteraction();
            if (move?.CanExecute(null) == true) move.Execute(null);
            else if (tap && Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
            break;
         case Android.Views.MotionEventActions.Cancel:
            CancelInteraction(); break;
      }
   }
   private void BeginHolding() {
      if (!_tracking || !_tapCandidate || !CanReorder || _native?.IsAttachedToWindow != true || !ReferenceEquals(_scopeAtDown, InteractionScope)) return;
      _holding = true; _tapCandidate = false;
      _native?.Parent?.RequestDisallowInterceptTouchEvent(true);
      _native?.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
      _feedbackTarget = DragTarget;
      if (_feedbackTarget is { } target) { _originalOpacity = target.Opacity; _originalY = target.TranslationY; target.Opacity *= 0.75; }
   }
#endif
   private void CancelInteraction() {
#if ANDROID
      _holdTimer?.Stop();
      if (_native is { } native) { native.Pressed = false; native.Parent?.RequestDisallowInterceptTouchEvent(false); }
      if (_feedbackTarget is { } target) { target.Opacity = _originalOpacity; target.TranslationY = _originalY; }
      _feedbackTarget = null; _tracking = _holding = _tapCandidate = false; _scopeAtDown = null;
#endif
   }
}
