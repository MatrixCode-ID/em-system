using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Controls;

/// <summary>Arranges images and arrows in physical units and exports a single PNG on the UI thread.</summary>
public sealed partial class ImageComposerVm : MvvmModelBase
{
   private readonly IComposerInput _input;
   private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
   private readonly HashSet<ComposerItem> _observed = [];
   private bool _loading;

   /// <summary>Creates a composer using the Windows clipboard and themed dialogs.</summary>
   public ImageComposerVm() : this(new ComposerInput()) { }

   internal ImageComposerVm(IComposerInput input) {
      _input = input;
      RegisterCommand(nameof(InsertCommand), InsertCommand, InsertCommandAllowed);
      RegisterCommand(nameof(PasteCommand), PasteCommand, PasteCommandAllowed);
      RegisterCommand(nameof(DeleteCommand), DeleteCommand, DeleteCommandAllowed);
      RegisterCommand(nameof(BringToFrontCommand), BringToFrontCommand, BringToFrontCommandAllowed);
      RegisterCommand(nameof(SendToBackCommand), SendToBackCommand, SendToBackCommandAllowed);
      RegisterCommand(nameof(FitCommand), FitCommand, FitCommandAllowed);
      RegisterCommand(nameof(CropCommand), CropCommand, CropCommandAllowed);
      RegisterCommand(nameof(ApplyCropCommand), ApplyCropCommand, ApplyCropCommandAllowed);
      RegisterCommand(nameof(CancelCropCommand), CancelCropCommand, CancelCropCommandAllowed);
      RegisterCommand(nameof(ArrowToolCommand), ArrowToolCommand, ArrowToolCommandAllowed);
      RegisterCommand(nameof(ClearCommand), ClearCommand, ClearCommandAllowed);
      RegisterCommand(nameof(ResetZoomCommand), ResetZoomCommand, ResetZoomCommandAllowed);
      Items.CollectionChanged += (_, _) => {
         foreach (var item in _observed.Where(i => !Items.Contains(i)).ToArray()) {
            item.PropertyChanged -= ItemChanged; _observed.Remove(item);
         }
         foreach (var item in Items.Where(i => !_observed.Contains(i))) {
            item.PropertyChanged += ItemChanged; _observed.Add(item);
         }
         if (SelectedItem != null && !Items.Contains(SelectedItem)) SelectedItem = null;
         ContentChanged();
      };
   }

   /// <summary>Canvas width, clamped to 10–1000 millimeters. Existing item positions remain unchanged.</summary>
   public double CanvasWidthMm { get => Get(100d); set { if (Set(Math.Clamp(ComposerGeometry.Finite(value, 100), 10, 1000))) ContentChanged(); } }
   /// <summary>Canvas height, clamped to 10–1000 millimeters.</summary>
   public double CanvasHeightMm { get => Get(100d); set { if (Set(Math.Clamp(ComposerGeometry.Finite(value, 100), 10, 1000))) ContentChanged(); } }
   /// <summary>PNG resolution, clamped to 72–1200 dpi; defaults to 300.</summary>
   public int OutputDpi { get => Get(300); set { if (Set(Math.Clamp(value, 72, 1200))) ContentChanged(); } }
   /// <summary>Disables editing commands and canvas gestures.</summary>
   public bool IsReadOnly { get => Get(false); set { if (Set(value)) { EndPointer(); ResetTool(); RefreshState(); } } }
   /// <summary>Whether the built-in size inputs are shown.</summary>
   public bool IsCanvasSizeEditable { get => Get(true); set { if (Set(value)) RefreshState(); } }
   /// <summary>Items ordered from bottom to top. Collection changes are tracked as edits.</summary>
   public ObservableCollection<ComposerItem> Items { get; } = [];
   /// <summary>The selected editable item, or null.</summary>
   public ComposerItem? SelectedItem {
      get => Get<ComposerItem?>();
      set {
         if (value != null && !Items.Contains(value)) return;
         if (Set(value)) {
            EndPointer(); ResetTool();
            if (value is ComposerArrowItem arrow) {
               Set(arrow.Color, propertyName: nameof(ArrowColor));
               Set(arrow.ThicknessMm, propertyName: nameof(ArrowThicknessMm));
            }
            RefreshState();
         }
      }
   }
   /// <summary>The active interaction tool. Crop requires a selected image.</summary>
   public ComposerTool Tool {
      get => Get(ComposerTool.Select);
      set {
         if (IsReadOnly || HasInitialImage || (value == ComposerTool.Crop && SelectedItem is not ComposerImageItem)) return;
         if (Set(value)) {
            if (value == ComposerTool.Crop && SelectedItem is { } item) CropBounds = new Rect(0, 0, item.WidthMm, item.HeightMm);
            RefreshState();
         }
      }
   }
   /// <summary>The default arrow color; changing it also updates the selected arrow when editing is allowed.</summary>
   public ComposerArrowColor ArrowColor {
      get => Get(ComposerArrowColor.Red);
      set { if (CanEdit && Set(value) && SelectedItem is ComposerArrowItem arrow) arrow.Color = value; }
   }
   /// <summary>The default arrow shaft thickness: 0.5, 1 or 2 millimeters.</summary>
   public double ArrowThicknessMm {
      get => Get(1d);
      set { if (CanEdit && Set(value <= .75 ? .5 : value < 1.5 ? 1d : 2d) && SelectedItem is ComposerArrowItem arrow) arrow.ThicknessMm = ArrowThicknessMm; }
   }
   /// <summary>Whether a saved, flattened image locks this composition.</summary>
   public bool HasInitialImage => InitialImage != null;
   /// <summary>The frozen, locked image, stretched to the current canvas size.</summary>
   public BitmapSource? InitialImage { get => Get<BitmapSource?>(); private set => Set(value); }
   /// <summary>Whether the composition contains no images or annotations.</summary>
   public bool IsEmpty => !HasInitialImage && Items.Count == 0;
   /// <summary>Whether content or output settings changed since loading or saving.</summary>
   public bool HasChanges { get => Get(false); set => Set(value); }
   /// <summary>Whether any editable image is below 150 effective dpi.</summary>
   public bool HasLowResolution => Items.OfType<ComposerImageItem>().Any(i => i.IsLowResolution);
   /// <summary>Whether the canvas size inputs currently allow editing.</summary>
   public bool CanEditCanvasSize => CanEdit && IsCanvasSizeEditable;
   /// <summary>Whether image and annotation tools currently allow editing.</summary>
   public bool CanEdit => !IsReadOnly && !HasInitialImage && Tool != ComposerTool.Crop;
   /// <summary>Whether the crop Apply and Cancel tools are active.</summary>
   public bool IsCropping => Tool == ComposerTool.Crop;
   /// <summary>Whether the arrow tool is active.</summary>
   public bool IsArrowTool => Tool == ComposerTool.Arrow;
   /// <summary>The temporary crop rectangle in the selected image's local millimeter coordinates.</summary>
   public Rect CropBounds {
      get => Get(Rect.Empty);
      set {
         if (!IsCropping || IsReadOnly || SelectedItem is not ComposerImageItem image || value.IsEmpty) return;
         var width = Math.Clamp(ComposerGeometry.Finite(value.Width, 1), 1, image.WidthMm);
         var height = Math.Clamp(ComposerGeometry.Finite(value.Height, 1), 1, image.HeightMm);
         Set(new Rect(Math.Clamp(ComposerGeometry.Finite(value.X), 0, image.WidthMm - width),
            Math.Clamp(ComposerGeometry.Finite(value.Y), 0, image.HeightMm - height), width, height));
      }
   }

   /// <summary>Loads a saved composition as a locked base image and resets the dirty flag.</summary>
   public void LoadInitialImage(Stream png) {
      _dispatcher.VerifyAccess();
      var image = Decode(png);
      _loading = true;
      try { EndPointer(); SelectedItem = null; Items.Clear(); ResetTool(); InitialImage = image; }
      finally { _loading = false; }
      RefreshState(); MarkSaved();
   }
   /// <summary>Loads saved PNG bytes as a locked composition.</summary>
   public void LoadInitialImage(byte[] png) { using var stream = new MemoryStream(png, false); LoadInitialImage(stream); }
   /// <summary>Inserts a fully decoded image centered at its natural size, limited to 80% of the canvas.</summary>
   public void InsertImage(Stream stream) {
      _dispatcher.VerifyAccess();
      if (CanEdit) InsertImage(Decode(stream));
   }
   internal void InsertImage(BitmapSource source) {
      if (!CanEdit) return;
      var width = ComposerGeometry.Millimeters(source.PixelWidth, source.DpiX > 0 ? source.DpiX : 96);
      var height = ComposerGeometry.Millimeters(source.PixelHeight, source.DpiY > 0 ? source.DpiY : 96);
      var scale = Math.Min(1, Math.Min(CanvasWidthMm * .8 / width, CanvasHeightMm * .8 / height));
      // Preserve aspect ratio even for unusually thin images while respecting the minimum item size.
      scale = Math.Max(scale, Math.Max(1 / width, 1 / height));
      var item = new ComposerImageItem(source) { WidthMm = width * scale, HeightMm = height * scale };
      item.XMm = (CanvasWidthMm - item.WidthMm) / 2; item.YMm = (CanvasHeightMm - item.HeightMm) / 2;
      Items.Add(item); SelectedItem = item;
   }
   /// <summary>Encodes the clipped white canvas at <see cref="OutputDpi"/>. Call on the owning UI/STA thread.</summary>
   public byte[] FlattenPng() {
      _dispatcher.VerifyAccess();
      if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
         throw new InvalidOperationException("FlattenPng must be called on the owning UI/STA thread.");
      var width = checked((int)Math.Round(ComposerGeometry.Pixels(CanvasWidthMm, OutputDpi)));
      var height = checked((int)Math.Round(ComposerGeometry.Pixels(CanvasHeightMm, OutputDpi)));
      var visual = new DrawingVisual();
      RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
      using (var dc = visual.RenderOpen()) {
         // DrawingVisual uses DIPs; RenderTargetBitmap applies the requested output DPI exactly once.
         dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width * 96d / OutputDpi, height * 96d / OutputDpi));
         dc.PushTransform(new ScaleTransform(96 / 25.4, 96 / 25.4));
         dc.PushClip(new RectangleGeometry(new Rect(0, 0, CanvasWidthMm, CanvasHeightMm)));
         if (InitialImage != null) dc.DrawImage(InitialImage, new Rect(0, 0, CanvasWidthMm, CanvasHeightMm));
         foreach (var item in Items) ComposerGeometry.DrawItem(dc, item);
         dc.Pop(); dc.Pop();
      }
      var bitmap = new RenderTargetBitmap(width, height, OutputDpi, OutputDpi, PixelFormats.Pbgra32);
      bitmap.Render(visual);
      var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
      using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
   }
   /// <summary>Resets the dirty flag after the caller successfully saves the flattened image.</summary>
   public void MarkSaved() => HasChanges = false;

   private static BitmapSource Decode(Stream stream) {
      var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
      image.Freeze(); return image;
   }
   private void ContentChanged() {
      if (!_loading) HasChanges = true;
      RefreshState();
   }
   private void ItemChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName is nameof(ComposerImageItem.PixelWidth) or nameof(ComposerImageItem.PixelHeight)
         or nameof(ComposerImageItem.EffectiveDpi) or nameof(ComposerImageItem.IsLowResolution) or nameof(ComposerImageItem.ResolutionWarning)) return;
      ContentChanged();
   }
   internal void RefreshState() {
      foreach (var name in new[] { nameof(HasInitialImage), nameof(IsEmpty), nameof(HasLowResolution),
         nameof(CanEditCanvasSize), nameof(CanEdit), nameof(IsCropping), nameof(IsArrowTool) }) OnPropertyChanged(name);
      foreach (var command in Commands) command.RaiseCanExecuteChanged();
   }
   private void ResetTool() { Set(ComposerTool.Select, propertyName: nameof(Tool)); }
   private bool HasSelection => CanEdit && SelectedItem != null;
   private bool InsertCommandAllowed() => CanEdit;
   private bool PasteCommandAllowed() => CanEdit && _input.CanPaste();
   private bool DeleteCommandAllowed() => HasSelection;
   private bool BringToFrontCommandAllowed() => HasSelection && Items.IndexOf(SelectedItem!) < Items.Count - 1;
   private bool SendToBackCommandAllowed() => HasSelection && Items.IndexOf(SelectedItem!) > 0;
   private bool FitCommandAllowed() => HasSelection && SelectedItem is ComposerImageItem;
   private bool CropCommandAllowed() => FitCommandAllowed();
   private bool ApplyCropCommandAllowed() => !IsReadOnly && IsCropping && SelectedItem is ComposerImageItem && !CropBounds.IsEmpty;
   private bool CancelCropCommandAllowed() => !IsReadOnly && IsCropping;
   private bool ArrowToolCommandAllowed() => CanEdit;
   private bool ClearCommandAllowed() => !IsReadOnly && !IsCropping;
   private void InsertCommand() => InsertFiles(_input.PickFiles(DialogOwner));
   private void InsertFiles(IEnumerable<string> files) {
      foreach (var path in files.Where(ComposerInput.IsImageFile)) {
         try { using var stream = File.OpenRead(path); InsertImage(stream); }
         catch (Exception ex) when (ex is IOException or FileFormatException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.ExternalException) {
            _input.Error(DialogOwner, $"Could not load image '{Path.GetFileName(path)}'. {ex.Message}");
         }
      }
   }
   private void PasteCommand() {
      try {
         if (_input.ClipboardImage() is { } source) InsertImage(source);
         else InsertFiles(_input.ClipboardFiles());
      }
      catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or NotSupportedException or ArgumentException or IOException or FileFormatException) {
         _input.Error(DialogOwner, $"Could not paste the image. {ex.Message}");
      }
   }
   private void DeleteCommand() { if (SelectedItem is { } item) Items.Remove(item); }
   private void BringToFrontCommand() { if (SelectedItem is { } item) Items.Move(Items.IndexOf(item), Items.Count - 1); }
   private void SendToBackCommand() { if (SelectedItem is { } item) Items.Move(Items.IndexOf(item), 0); }
   private void FitCommand() {
      if (SelectedItem is not ComposerImageItem image) return;
      var scale = Math.Min(CanvasWidthMm / image.PixelWidth, CanvasHeightMm / image.PixelHeight);
      image.AngleDeg = 0; image.WidthMm = image.PixelWidth * scale; image.HeightMm = image.PixelHeight * scale;
      image.XMm = (CanvasWidthMm - image.WidthMm) / 2; image.YMm = (CanvasHeightMm - image.HeightMm) / 2;
   }
   private void CropCommand() => Tool = ComposerTool.Crop;
   private void ApplyCropCommand() {
      if (SelectedItem is not ComposerImageItem image) return;
      var pixels = ComposerGeometry.CropPixels(CropBounds, image);
      var sx = image.WidthMm / image.PixelWidth; var sy = image.HeightMm / image.PixelHeight;
      var localCenter = new Point((pixels.X + pixels.Width / 2d) * sx, (pixels.Y + pixels.Height / 2d) * sy);
      var center = ComposerGeometry.World(image, localCenter);
      var cropped = new CroppedBitmap(image.Source, pixels); cropped.Freeze();
      image.Source = cropped;
      image.WidthMm = pixels.Width * sx; image.HeightMm = pixels.Height * sy;
      image.XMm = center.X - image.WidthMm / 2; image.YMm = center.Y - image.HeightMm / 2;
      EndPointer(); ResetTool(); ContentChanged();
   }
   private void CancelCropCommand() { EndPointer(); ResetTool(); RefreshState(); }
   private void ArrowToolCommand() => Tool = Tool == ComposerTool.Arrow ? ComposerTool.Select : ComposerTool.Arrow;
   private void ClearCommand() {
      if (!_input.ConfirmClear(DialogOwner)) return;
      EndPointer(); SelectedItem = null; Items.Clear(); InitialImage = null; ResetTool(); ContentChanged();
   }
}
