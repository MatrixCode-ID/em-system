namespace Em.Ui.Wpf.Controls;

/// <summary>Preview zoom and pan. View state only: never marks the composition as changed or affects export.</summary>
public sealed partial class ImageComposerVm
{
   /// <summary>Smallest preview zoom, in percent of the fitted canvas.</summary>
   public const double MinZoomPercent = 25;
   /// <summary>Largest preview zoom, in percent of the fitted canvas.</summary>
   public const double MaxZoomPercent = 800;

   /// <summary>Preview zoom in percent, where 100 fits the canvas to the workspace; clamped to 25–800.</summary>
   public double ZoomPercent {
      get => Get(100d);
      set {
         if (!Set(Math.Clamp(ComposerGeometry.Finite(value, 100), MinZoomPercent, MaxZoomPercent))) return;
         OnPropertyChanged(nameof(ZoomScale));
         Commands[nameof(ResetZoomCommand)]?.RaiseCanExecuteChanged();
      }
   }
   /// <summary>Preview zoom as a scale factor (<see cref="ZoomPercent"/> / 100).</summary>
   public double ZoomScale => ZoomPercent / 100;
   /// <summary>Horizontal preview pan offset in device-independent pixels.</summary>
   public double PanX { get => Get(0d); set { if (Set(ComposerGeometry.Finite(value))) Commands[nameof(ResetZoomCommand)]?.RaiseCanExecuteChanged(); } }
   /// <summary>Vertical preview pan offset in device-independent pixels.</summary>
   public double PanY { get => Get(0d); set { if (Set(ComposerGeometry.Finite(value))) Commands[nameof(ResetZoomCommand)]?.RaiseCanExecuteChanged(); } }

   private bool ResetZoomCommandAllowed() => ZoomPercent != 100 || PanX != 0 || PanY != 0;
   private void ResetZoomCommand() { ZoomPercent = 100; PanX = 0; PanY = 0; }
}
