namespace Em.Ui.Maui.Controls;

/// <summary>Interruptible easing for modal surfaces. This policy does not simulate physics.</summary>
public sealed class MotionPolicy
{
   private int _generation;
   private readonly Func<bool> _reducedMotion;
   private readonly Func<VisualElement, CancellationToken, Task> _waitForFrame;
   private CancellationTokenSource? _waiting;
   /// <summary>Uses the platform preference, or an injected preference for console verification.</summary>
   public MotionPolicy(Func<bool>? reducedMotion = null, Func<VisualElement, CancellationToken, Task>? waitForFrame = null) {
      _reducedMotion = reducedMotion ?? (() => ReducedMotion);
      _waitForFrame = waitForFrame ?? WaitForFrameAsync;
   }
   /// <summary>Reads Android's animator scale on every transition, including scale zero.</summary>
   public static bool ReducedMotion {
      get {
#if ANDROID
         return Android.Provider.Settings.Global.GetFloat(Android.App.Application.Context.ContentResolver,
            Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0;
#else
         return true;
#endif
      }
   }
   /// <summary>Cancels previous motion and leaves no invisible input-blocking surface.</summary>
   public void Cancel(VisualElement view) { _generation++; _waiting?.Cancel(); _waiting = null; view.CancelAnimations(); }
   /// <summary>Replaces previous motion. Only the latest transition may change visibility.</summary>
   public async Task SetOpenAsync(VisualElement view, bool open, double width) {
      Cancel(view); var generation = _generation;
      view.InputTransparent = !open;
      if (!open && !view.IsVisible) { view.TranslationX = width; return; }
      if (open && !view.IsVisible) view.TranslationX = width;
      if (open) view.IsVisible = true;
      if (_reducedMotion()) view.TranslationX = open ? 0 : width;
      else await view.TranslateToAsync(open ? 0 : width, 0, open ? 220u : 160u, open ? Easing.CubicOut : Easing.CubicIn);
      if (generation == _generation) { view.TranslationX = open ? 0 : width; view.IsVisible = open; }
   }
   /// <summary>Reveals mounted content without delaying navigation or retaining the outgoing view.</summary>
   public async Task EnterAsync(VisualElement view, double offset = 12, double opacity = 1) {
      Cancel(view); var generation = _generation;
      if (!_reducedMotion()) {
         view.Opacity = 0; view.TranslationY = offset;
         await Task.WhenAll(view.FadeToAsync(opacity, 180, Easing.CubicOut), view.TranslateToAsync(0, 0, 220, Easing.CubicOut));
      }
      if (generation == _generation) { view.Opacity = opacity; view.TranslationX = 0; view.TranslationY = 0; }
   }
   /// <summary>Slides a mounted page horizontally after its first layout, reversing direction on Back.</summary>
   public async Task SwipeAsync(VisualElement view, bool backward = false) {
      Cancel(view); var generation = _generation;
      view.Opacity = 1; view.TranslationY = 0;
      if (_reducedMotion()) { view.TranslationX = 0; return; }
      using var waiting = new CancellationTokenSource();
      _waiting = waiting;
      view.TranslationX = (backward ? -1 : 1) * Math.Max(0, view.Width);
      try {
         await _waitForFrame(view, waiting.Token);
         if (waiting.IsCancellationRequested || generation != _generation) return;
         if (!_reducedMotion()) {
            view.TranslationX = (backward ? -1 : 1) * Math.Max(0, view.Width);
            await view.TranslateToAsync(0, 0, 240, Easing.CubicOut);
         }
         if (generation == _generation) view.TranslationX = 0;
      }
      catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
      finally { if (ReferenceEquals(_waiting, waiting)) _waiting = null; }
   }
   private static async Task WaitForFrameAsync(VisualElement view, CancellationToken cancellationToken) {
#if ANDROID
      if (view.Handler?.PlatformView is not Android.Views.View native) return;
      var observer = native.ViewTreeObserver;
      if (observer is not { IsAlive: true }) return;
      var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using var listener = new ReadyFrame(() => ready.TrySetResult());
      using var cancellation = cancellationToken.Register(() => ready.TrySetCanceled(cancellationToken));
      observer.AddOnPreDrawListener(listener);
      native.Invalidate();
      try { await ready.Task; }
      finally { if (observer.IsAlive) observer.RemoveOnPreDrawListener(listener); }
#else
      await Task.CompletedTask;
#endif
   }
#if ANDROID
   private sealed class ReadyFrame(Action ready) : Java.Lang.Object, Android.Views.ViewTreeObserver.IOnPreDrawListener {
      public bool OnPreDraw() { ready(); return true; }
   }
#endif
}
