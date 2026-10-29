using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Ui.Wpf.Controls;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Core.Tests;

public class ImageComposerTests
{
   private sealed class Input : IComposerInput
   {
      public bool Confirm = true;
      public BitmapSource? Image;
      public string[] Files = [];
      public List<string> Errors = [];
      public bool CanPaste() => Image != null || Files.Any(ComposerInput.IsImageFile);
      public BitmapSource? ClipboardImage() => Image;
      public IEnumerable<string> ClipboardFiles() => Files;
      public IEnumerable<string> PickFiles(Window? owner) => Files;
      public bool ConfirmClear(Window? owner) => Confirm;
      public void Error(Window? owner, string message) => Errors.Add(message);
   }
   private static ImageComposerVm Vm() => new(new Input());
   private static void Run(ImageComposerVm vm, string command) {
      var action = vm.Commands[command]; Assert.NotNull(action); Assert.True(action.CanExecute(null)); action.Execute(null);
   }
   private static BitmapSource Image(int width = 200, int height = 100, double dpi = 100, Color? color = null) {
      var c = color ?? Colors.Lime;
      var pixels = new byte[width * height * 4];
      for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = c.A; }
      var bitmap = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * 4);
      bitmap.Freeze(); return bitmap;
   }
   private static BitmapSource Decode(byte[] png) {
      using var stream = new MemoryStream(png);
      return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
   }
   private static byte[] Pixel(BitmapSource source, int x, int y) {
      var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
      var data = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), data, 4, 0); return data;
   }
   private static ComposerImageItem AddImage(ImageComposerVm vm) {
      vm.InsertImage(Image()); return Assert.IsType<ComposerImageItem>(vm.SelectedItem);
   }

   [Fact]
   public void GeometryConvertsPhysicalUnitsAndSnapsAngles() {
      Assert.Equal(300, ComposerGeometry.Pixels(25.4, 300), 8);
      Assert.Equal(25.4, ComposerGeometry.Millimeters(300, 300), 8);
      Assert.Equal(30, ComposerGeometry.Snap(23, false));
      Assert.Equal(23, ComposerGeometry.Snap(23, true));
      Assert.Equal(-15, ComposerGeometry.Snap(-19, false));
   }

   [WpfFact]
   public void FlattenHasPhysicalPixelDimensionsDpiAndWhiteBackground() {
      var vm = Vm(); vm.CanvasWidthMm = 100; vm.CanvasHeightMm = 50;
      var bitmap = Decode(vm.FlattenPng());
      Assert.Equal(1181, bitmap.PixelWidth); Assert.Equal(591, bitmap.PixelHeight);
      Assert.InRange(bitmap.DpiX, 299.9, 300.1); Assert.InRange(bitmap.DpiY, 299.9, 300.1);
      var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
      var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
      converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
      Assert.All(bytes, b => Assert.Equal(255, b));
   }

   [WpfFact]
   public void FlattenDrawsPrintingInkAndClipsOutsideItems() {
      var vm = Vm(); vm.CanvasWidthMm = 25.4; vm.CanvasHeightMm = 25.4; vm.OutputDpi = 100;
      vm.Items.Add(new ComposerArrowItem { StartXMm = 2, StartYMm = 12.7, EndXMm = 23, EndYMm = 12.7, ThicknessMm = 2 });
      var bitmap = Decode(vm.FlattenPng());
      var red = Pixel(bitmap, 40, 50); Assert.Equal(new byte[] { 0x2F, 0x2F, 0xD3, 255 }, red);
      vm.Items.Clear();
      vm.Items.Add(new ComposerImageItem(Image(100, 100)) { XMm = -12.7, YMm = 0, WidthMm = 25.4, HeightMm = 25.4 });
      bitmap = Decode(vm.FlattenPng());
      Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(bitmap, 10, 50));
      Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(bitmap, 80, 50));
      Assert.Equal(100, bitmap.PixelWidth);
   }

   [Fact]
   public void CornerResizePreservesRatioAndSideOrShiftResizeDoesNot() {
      var box = new Rect(10, 20, 40, 20);
      var locked = ComposerGeometry.Resize(box, ComposerHandle.NorthWest, new Vector(-10, -2), true);
      Assert.Equal(2, locked.Width / locked.Height, 8);
      Assert.Equal(box.BottomRight, locked.BottomRight);
      var free = ComposerGeometry.Resize(box, ComposerHandle.SouthEast, new Vector(10, 2), false);
      Assert.Equal(50, free.Width); Assert.Equal(22, free.Height);
      var side = ComposerGeometry.Resize(box, ComposerHandle.West, new Vector(10, 200), true);
      Assert.Equal(30, side.Width); Assert.Equal(20, side.Height);
      var shrink = ComposerGeometry.Resize(box, ComposerHandle.SouthEast, new Vector(-10, 0), true);
      Assert.Equal(30, shrink.Width); Assert.Equal(15, shrink.Height);
      var tiny = ComposerGeometry.Resize(box, ComposerHandle.SouthEast, new Vector(-100, -100), false);
      Assert.Equal(1, tiny.Width); Assert.Equal(1, tiny.Height);
   }

   [WpfFact]
   public void RotatedImageHitTestAndResizeKeepOppositeCornerFixed() {
      var vm = Vm(); var item = AddImage(vm);
      item.XMm = 20; item.YMm = 30; item.WidthMm = 40; item.HeightMm = 20; item.AngleDeg = 45;
      Assert.True(ComposerGeometry.Hit(item, ComposerGeometry.World(item, new Point(2, 2)), 1));
      Assert.False(ComposerGeometry.Hit(item, ComposerGeometry.World(item, new Point(-1, 10)), .1));
      var fixedCorner = ComposerGeometry.World(item, new Point());
      var handle = ComposerGeometry.World(item, new Point(40, 20));
      Assert.True(vm.BeginPointer(handle, 1));
      vm.MovePointer(handle + new Vector(10, 10), false); vm.EndPointer();
      var actual = ComposerGeometry.World(item, new Point());
      Assert.InRange((actual - fixedCorner).Length, 0, 1e-8);
      Assert.Equal(2, item.WidthMm / item.HeightMm, 8);
   }

   [WpfFact]
   public void PermanentCropUsesSourcePixelsAndPreservesRotatedRegionPosition() {
      var vm = Vm(); var item = AddImage(vm);
      item.WidthMm = 40; item.HeightMm = 20; item.XMm = 10; item.YMm = 20; item.AngleDeg = 30;
      var expected = ComposerGeometry.World(item, new Point(10, 4));
      Run(vm, "CropCommand"); vm.CropBounds = new Rect(10, 4, 20, 10);
      Assert.Equal(new Int32Rect(50, 20, 100, 50), ComposerGeometry.CropPixels(vm.CropBounds, item));
      Assert.All(vm.Commands.Where(c => c.Name is not "ApplyCropCommand" and not "CancelCropCommand"), c => Assert.False(c.CanExecute(null)));
      Run(vm, "ApplyCropCommand");
      Assert.Equal(100, item.PixelWidth); Assert.Equal(50, item.PixelHeight);
      Assert.Equal(20, item.WidthMm, 8); Assert.Equal(10, item.HeightMm, 8);
      Assert.InRange((expected - ComposerGeometry.World(item, new Point())).Length, 0, 1e-8);
      Assert.True(item.Source.IsFrozen); Assert.True(vm.HasChanges); Assert.False(vm.IsCropping);
      Assert.Equal(new Int32Rect(50, 20, 100, 50), Assert.IsType<CroppedBitmap>(item.Source).SourceRect);
   }

   [WpfFact]
   public void CancelCropLeavesImageAndDirtyFlagUnchanged() {
      var vm = Vm(); var item = AddImage(vm); var source = item.Source;
      vm.MarkSaved(); var position = new Point(item.XMm, item.YMm);
      Run(vm, "CropCommand"); vm.CropBounds = new Rect(1, 2, 10, 10); vm.Escape();
      Assert.Same(source, item.Source); Assert.Equal(position, new Point(item.XMm, item.YMm));
      Assert.False(vm.HasChanges); Assert.False(vm.IsCropping);
   }

   [WpfFact]
   public void ZOrderDeleteAndFitFollowSelectedItem() {
      var vm = Vm(); var first = AddImage(vm); var second = AddImage(vm);
      vm.SelectedItem = first; vm.MarkSaved(); Run(vm, "BringToFrontCommand");
      Assert.Same(first, vm.Items[^1]); Assert.True(vm.HasChanges);
      Run(vm, "SendToBackCommand"); Assert.Same(first, vm.Items[0]);
      first.AngleDeg = 45; Run(vm, "FitCommand");
      Assert.Equal(0, first.AngleDeg); Assert.Equal(100, first.WidthMm); Assert.Equal(50, first.HeightMm);
      Assert.Equal(0, first.XMm); Assert.Equal(25, first.YMm);
      Run(vm, "DeleteCommand"); Assert.Single(vm.Items); Assert.Same(second, vm.Items[0]);
      var arrow = new ComposerArrowItem(); vm.Items.Add(arrow); vm.SelectedItem = arrow;
      Assert.False(vm.Commands["FitCommand"]!.CanExecute(null));
   }

   [WpfFact]
   public void CanvasResizeKeepsPhysicalItemsAndInitialImageLocksEditingUntilClear() {
      var input = new Input(); var vm = new ImageComposerVm(input); var item = AddImage(vm);
      var box = new Rect(item.XMm, item.YMm, item.WidthMm, item.HeightMm);
      vm.MarkSaved(); vm.CanvasWidthMm = 50; vm.CanvasHeightMm = 30;
      Assert.Equal(box, new Rect(item.XMm, item.YMm, item.WidthMm, item.HeightMm)); Assert.True(vm.HasChanges);
      var png = vm.FlattenPng(); vm.LoadInitialImage(png);
      Assert.Empty(vm.Items); Assert.True(vm.HasInitialImage); Assert.False(vm.HasChanges); Assert.False(vm.CanEditCanvasSize);
      Assert.All(vm.Commands.Where(c => c.Name != "ClearCommand"), c => Assert.False(c.CanExecute(null)));
      vm.CanvasWidthMm = 60; Assert.Equal(60, vm.CanvasWidthMm); Assert.True(vm.HasChanges);
      vm.InsertImage(new MemoryStream(png)); Assert.Empty(vm.Items);
      input.Confirm = false; Run(vm, "ClearCommand"); Assert.True(vm.HasInitialImage);
      input.Confirm = true; Run(vm, "ClearCommand");
      Assert.False(vm.HasInitialImage); Assert.True(vm.IsEmpty); Assert.True(vm.HasChanges); Assert.True(vm.CanEditCanvasSize);
      vm.MarkSaved(); Run(vm, "ClearCommand"); Assert.True(vm.HasChanges);
   }

   [WpfFact]
   public void ReadOnlyDisablesEveryCommandAndGesture() {
      var input = new Input { Image = Image() }; var vm = new ImageComposerVm(input); AddImage(vm);
      vm.IsReadOnly = true; vm.MarkSaved();
      Assert.All(vm.Commands, c => { Assert.False(c.CanExecute(null)); c.Execute(null); });
      vm.MoveSelected(new Vector(1, 1)); Assert.False(vm.BeginPointer(new Point(50, 50), 1));
      vm.InsertImage(new MemoryStream([1, 2, 3]));
      Assert.False(vm.HasChanges); Assert.Single(vm.Items);
   }

   [WpfFact]
   public void DirtyFlagTracksItemChangesCollectionResetAndResolutionSettings() {
      var vm = Vm(); var item = AddImage(vm); vm.MarkSaved(); item.AngleDeg = 15; Assert.True(vm.HasChanges);
      vm.MarkSaved(); item.XMm += .1; Assert.True(vm.HasChanges);
      vm.MarkSaved(); vm.OutputDpi = 600; Assert.True(vm.HasChanges);
      vm.MarkSaved(); vm.Items.Clear(); Assert.True(vm.HasChanges);
      vm.MarkSaved(); item.XMm += 1; Assert.False(vm.HasChanges);
      var arrow = new ComposerArrowItem(); vm.Items.Add(arrow); vm.SelectedItem = arrow; vm.MarkSaved();
      vm.ArrowColor = ComposerArrowColor.Blue; Assert.Equal(ComposerArrowColor.Blue, arrow.Color); Assert.True(vm.HasChanges);
      vm.MarkSaved(); vm.ArrowThicknessMm = 2; Assert.Equal(2, arrow.ThicknessMm); Assert.True(vm.HasChanges);
      vm.SelectedItem = null;
      arrow.Color = ComposerArrowColor.Yellow; arrow.ThicknessMm = .5;
      vm.MarkSaved(); vm.SelectedItem = arrow;
      Assert.Equal(ComposerArrowColor.Yellow, vm.ArrowColor); Assert.Equal(.5, vm.ArrowThicknessMm); Assert.False(vm.HasChanges);
   }

   [WpfFact]
   public void EffectiveResolutionUsesTheLowerAxisAndThresholdIsStrict() {
      var vm = Vm(); var item = AddImage(vm);
      item.WidthMm = 200 / 150d * 25.4; item.HeightMm = 100 / 150d * 25.4;
      Assert.Equal(150, item.EffectiveDpi, 8); Assert.False(item.IsLowResolution); Assert.False(vm.HasLowResolution);
      item.WidthMm += .1; Assert.True(item.IsLowResolution); Assert.True(vm.HasLowResolution);
      Assert.Contains("printed image", item.ResolutionWarning);
   }

   [WpfFact]
   public void PhysicalSettingsClampAndRejectNonfiniteCoordinates() {
      var vm = Vm(); vm.CanvasWidthMm = -2; vm.CanvasHeightMm = 1001; vm.OutputDpi = 20;
      Assert.Equal(10, vm.CanvasWidthMm); Assert.Equal(1000, vm.CanvasHeightMm); Assert.Equal(72, vm.OutputDpi);
      vm.OutputDpi = 2000; Assert.Equal(1200, vm.OutputDpi);
      vm.CanvasWidthMm = double.NaN; Assert.Equal(100, vm.CanvasWidthMm);
      var item = AddImage(vm); item.WidthMm = -1; item.XMm = double.PositiveInfinity;
      Assert.Equal(1, item.WidthMm); Assert.Equal(0, item.XMm);
   }

   [WpfFact]
   public void ArrowGesturesMoveEndpointsAndCancelShortArrows() {
      var vm = Vm(); vm.MarkSaved(); Run(vm, "ArrowToolCommand");
      Assert.True(vm.BeginPointer(new Point(10, 10), .2)); vm.MovePointer(new Point(11, 10), false); vm.EndPointer();
      Assert.Empty(vm.Items); Assert.False(vm.HasChanges);
      Run(vm, "ArrowToolCommand"); vm.BeginPointer(new Point(10, 10), .2); vm.MovePointer(new Point(30, 10), false); vm.EndPointer();
      var arrow = Assert.IsType<ComposerArrowItem>(vm.SelectedItem);
      Assert.Equal(20, arrow.EndXMm - arrow.StartXMm);
      vm.BeginPointer(new Point(10, 10), .2); vm.MovePointer(new Point(8, 12), false); vm.EndPointer();
      Assert.Equal(8, arrow.StartXMm); Assert.Equal(12, arrow.StartYMm); Assert.Equal(30, arrow.EndXMm);
      vm.MoveSelected(new Vector(.1, 1)); Assert.Equal(8.1, arrow.StartXMm, 8); Assert.Equal(30.1, arrow.EndXMm, 8);
      var head = ComposerGeometry.ArrowHead(arrow); Assert.Equal(new Point(arrow.EndXMm, arrow.EndYMm), head[0]);
   }

   [WpfFact]
   public void PasteInsertsFrozenImageAndCanExecuteReflectsClipboard() {
      var input = new Input(); var vm = new ImageComposerVm(input);
      Assert.False(vm.Commands["PasteCommand"]!.CanExecute(null));
      input.Image = Image(200, 100, 100); Run(vm, "PasteCommand");
      var item = Assert.IsType<ComposerImageItem>(vm.SelectedItem);
      Assert.True(item.Source.IsFrozen); Assert.Equal(50.8, item.WidthMm, 8); Assert.Equal(25.4, item.HeightMm, 8);
      Assert.Equal((100 - 50.8) / 2, item.XMm, 8);
      vm.InsertImage(Image(1000, 500)); item = Assert.IsType<ComposerImageItem>(vm.SelectedItem);
      Assert.Equal(80, item.WidthMm, 8); Assert.Equal(40, item.HeightMm, 8);
   }

   [WpfFact]
   public void PointerRotationSnapsAndShiftAllowsFreeAngle() {
      var vm = Vm(); var item = AddImage(vm);
      item.XMm = 25; item.YMm = 25; item.WidthMm = 40; item.HeightMm = 20;
      var center = ComposerGeometry.Center(item);
      var handle = vm.Handles(1).Single(h => h.Handle == ComposerHandle.Rotate).Point;
      vm.BeginPointer(handle, 1); vm.MovePointer(ComposerGeometry.Rotate(handle, center, 23), false); vm.EndPointer();
      Assert.Equal(30, item.AngleDeg, 8);
      handle = vm.Handles(1).Single(h => h.Handle == ComposerHandle.Rotate).Point;
      vm.BeginPointer(handle, 1); vm.MovePointer(ComposerGeometry.Rotate(handle, center, 7), true); vm.EndPointer();
      Assert.Equal(37, item.AngleDeg, 8);
   }

   [WpfFact]
   public void CropHandleCannotLeaveImageOrMoveItsOppositeEdge() {
      var vm = Vm(); var item = AddImage(vm); item.WidthMm = 40; item.HeightMm = 20;
      Run(vm, "CropCommand"); vm.CropBounds = new Rect(10, 4, 20, 10);
      var handle = ComposerGeometry.World(item, new Point(30, 9));
      vm.BeginPointer(handle, .2); vm.MovePointer(handle + new Vector(100, 0), false); vm.EndPointer();
      Assert.Equal(new Rect(10, 4, 30, 10), vm.CropBounds);
      Assert.False(vm.HasInitialImage); Assert.True(vm.IsCropping);
   }

   [WpfFact]
   public void InvalidInsertContinuesToTheNextFileAndDisposesSourceStreams() {
      var dir = Path.Combine(Path.GetTempPath(), "Em.ImageComposerTests." + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      try {
         var bad = Path.Combine(dir, "invalid.png"); var good = Path.Combine(dir, "valid.png");
         File.WriteAllText(bad, "Invalid image pixels");
         var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Image()));
         using (var output = File.Create(good)) encoder.Save(output);
         var input = new Input { Files = [bad, good] }; var vm = new ImageComposerVm(input);
         Run(vm, "InsertCommand");
         Assert.Single(input.Errors); Assert.Contains("invalid.png", input.Errors[0]); Assert.Single(vm.Items);
         File.Delete(good);
         Assert.Equal(200, Assert.IsType<ComposerImageItem>(vm.Items[0]).PixelWidth);
         vm.MarkSaved();
         Assert.ThrowsAny<Exception>(() => vm.LoadInitialImage(new byte[] { 1, 2, 3 }));
         Assert.Single(vm.Items); Assert.False(vm.HasInitialImage); Assert.False(vm.HasChanges);
      }
      finally { Directory.Delete(dir, true); }
   }

   [WpfFact]
   public void ControlXamlAndDependencyPropertiesBindWithoutRendering() {
      var vm = Vm(); vm.CanvasWidthMm = 125; vm.CanvasHeightMm = 75; vm.MarkSaved();
      var control = new ImageComposer { Composer = vm };
      Assert.Equal(125, control.CanvasWidthMm); Assert.Equal(75, control.CanvasHeightMm);
      control.CanvasWidthMm = 150; Assert.Equal(150, vm.CanvasWidthMm); Assert.True(control.HasChanges);
      vm.MarkSaved(); Assert.False(control.HasChanges);
      vm.CanvasHeightMm = 80; Assert.Equal(80, control.CanvasHeightMm);
      control.IsReadOnly = true; Assert.True(vm.IsReadOnly);
      Assert.IsType<Style>(control.Resources["fieldNumericBoxStyle"]);
      foreach (var type in new[] { typeof(ComposerImageItem), typeof(ComposerArrowItem) }) {
         var template = Assert.IsType<DataTemplate>(control.Resources[new DataTemplateKey(type)]);
         Assert.IsType<ComposerItemPresenter>(template.LoadContent());
      }
      var other = Vm(); other.CanvasWidthMm = 222;
      control.Composer = other; Assert.Equal(150, other.CanvasWidthMm);
      var host = new Host();
      BindingOperations.SetBinding(control, ImageComposer.CanvasHeightMmProperty, new Binding(nameof(Host.Height)) { Source = host, Mode = BindingMode.TwoWay });
      Assert.Equal(60, other.CanvasHeightMm);
      other.CanvasHeightMm = 90; Assert.Equal(90, host.Height);
   }
   [WpfFact]
   public void ZoomAndPanClampWithoutMarkingChangesAndResetToFit() {
      var vm = Vm();
      Assert.False(vm.Commands["ResetZoomCommand"]!.CanExecute(null));
      vm.ZoomPercent = 5000; Assert.Equal(ImageComposerVm.MaxZoomPercent, vm.ZoomPercent);
      vm.ZoomPercent = 1; Assert.Equal(ImageComposerVm.MinZoomPercent, vm.ZoomPercent);
      vm.ZoomPercent = 250; vm.PanX = 40; vm.PanY = -12;
      Assert.Equal(2.5, vm.ZoomScale);
      Assert.False(vm.HasChanges);
      Run(vm, "ResetZoomCommand");
      Assert.Equal(100, vm.ZoomPercent); Assert.Equal(0, vm.PanX); Assert.Equal(0, vm.PanY);
      Assert.False(vm.Commands["ResetZoomCommand"]!.CanExecute(null));
   }
   private sealed class Host : MvvmModelBase
   {
      public double Height { get => Get(60d); set => Set(value); }
   }
}
