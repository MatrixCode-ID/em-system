using System.Windows.Media.Imaging;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Controls;

/// <summary>The fixed printing ink colors available for annotations.</summary>
public enum ComposerArrowColor
{
   /// <summary>Red ink (#D32F2F).</summary>
   Red,
   /// <summary>Black ink (#000000).</summary>
   Black,
   /// <summary>Blue ink (#1565C0).</summary>
   Blue,
   /// <summary>Yellow ink (#F9A825).</summary>
   Yellow
}

/// <summary>The active canvas interaction.</summary>
public enum ComposerTool
{
   /// <summary>Select and arrange an item.</summary>
   Select,
   /// <summary>Draw an arrow by dragging.</summary>
   Arrow,
   /// <summary>Edit a permanent image crop.</summary>
   Crop
}

/// <summary>An item measured in millimeters from the canvas origin.</summary>
public abstract class ComposerItem : MvvmModelBase
{
   /// <summary>Left position before rotation, in millimeters.</summary>
   public double XMm { get => Get(0d); set => Set(ComposerGeometry.Finite(value)); }
   /// <summary>Top position before rotation, in millimeters.</summary>
   public double YMm { get => Get(0d); set => Set(ComposerGeometry.Finite(value)); }
   /// <summary>Width in millimeters, at least one.</summary>
   public double WidthMm { get => Get(1d); set => Set(Math.Max(1, ComposerGeometry.Finite(value, 1))); }
   /// <summary>Height in millimeters, at least one.</summary>
   public double HeightMm { get => Get(1d); set => Set(Math.Max(1, ComposerGeometry.Finite(value, 1))); }
   /// <summary>Clockwise rotation about the bounding box center, in degrees.</summary>
   public double AngleDeg { get => Get(0d); set => Set(ComposerGeometry.Finite(value) % 360); }
}

/// <summary>A fully decoded, frozen image with physical layout and effective resolution.</summary>
public sealed class ComposerImageItem : ComposerItem
{
   /// <summary>Creates an item with a private frozen snapshot of the image.</summary>
   public ComposerImageItem(BitmapSource source) {
      Source = Freeze(source);
      PropertyChanged += (_, e) => {
         if (e.PropertyName is nameof(WidthMm) or nameof(HeightMm) or nameof(Source)) {
            OnPropertyChanged(nameof(PixelWidth)); OnPropertyChanged(nameof(PixelHeight));
            OnPropertyChanged(nameof(EffectiveDpi)); OnPropertyChanged(nameof(IsLowResolution));
            OnPropertyChanged(nameof(ResolutionWarning));
         }
      };
   }
   /// <summary>The frozen image pixels; cropping replaces this snapshot.</summary>
   public BitmapSource Source { get => Get<BitmapSource>(); internal set => Set(Freeze(value)); }
   /// <summary>Source width in pixels.</summary>
   public int PixelWidth => Source.PixelWidth;
   /// <summary>Source height in pixels.</summary>
   public int PixelHeight => Source.PixelHeight;
   /// <summary>The lower of the horizontal and vertical printing resolutions.</summary>
   public double EffectiveDpi => Math.Min(PixelWidth / (WidthMm / 25.4), PixelHeight / (HeightMm / 25.4));
   /// <summary>Whether the effective resolution is below 150 dpi.</summary>
   public bool IsLowResolution => EffectiveDpi < 150;
   /// <summary>The resolution warning shown by the preview.</summary>
   public string ResolutionWarning => $"Effective resolution {EffectiveDpi:0} dpi; the printed image may look blurry.";
   private static BitmapSource Freeze(BitmapSource source) {
      ArgumentNullException.ThrowIfNull(source);
      if (source.IsFrozen) return source;
      var copy = source.Clone(); copy.Freeze(); return copy;
   }
}

/// <summary>An arrow with independently movable endpoints in millimeters.</summary>
public sealed class ComposerArrowItem : ComposerItem
{
   /// <summary>The horizontal start coordinate.</summary>
   public double StartXMm { get => Get(0d); set => Set(ComposerGeometry.Finite(value)); }
   /// <summary>The vertical start coordinate.</summary>
   public double StartYMm { get => Get(0d); set => Set(ComposerGeometry.Finite(value)); }
   /// <summary>The horizontal end coordinate.</summary>
   public double EndXMm { get => Get(0d); set => Set(ComposerGeometry.Finite(value)); }
   /// <summary>The vertical end coordinate.</summary>
   public double EndYMm { get => Get(0d); set => Set(ComposerGeometry.Finite(value)); }
   /// <summary>The printing ink color.</summary>
   public ComposerArrowColor Color { get => Get(ComposerArrowColor.Red); set => Set(value); }
   /// <summary>The shaft thickness: 0.5, 1 or 2 millimeters.</summary>
   public double ThicknessMm { get => Get(1d); set => Set(value <= .75 ? .5 : value < 1.5 ? 1d : 2d); }
}
