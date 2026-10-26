namespace Em.Ui.Maui.Controls;

/// <summary>Interruptible easing for modal surfaces. This policy does not simulate physics.</summary>
public sealed class MotionPolicy
{
   private int _generation;
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
   public void Cancel(VisualElement view) { _generation++; view.CancelAnimations(); }
   /// <summary>Replaces previous motion. Only the latest transition may change visibility.</summary>
   public async Task SetOpenAsync(VisualElement view, bool open, double width) {
      var generation = ++_generation;
      view.CancelAnimations();
      if (open) view.IsVisible = true;
      if (ReducedMotion) view.TranslationX = open ? 0 : width;
      else await view.TranslateToAsync(open ? 0 : width, 0, open ? 220u : 160u, open ? Easing.CubicOut : Easing.CubicIn);
      if (generation == _generation) { view.TranslationX = open ? 0 : width; view.IsVisible = open; }
   }
}
