using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Em.Ui.Wpf.Controls;

/// <summary>A reusable WPF canvas for arranging images and arrow annotations in millimeters.</summary>
public partial class ImageComposer : UserControl
{
   private bool _syncing;
   private readonly HashSet<DependencyProperty> _explicit = [];
   private double _shownZoom = 1;
   private Point? _zoomAnchor;
   private bool _panning;
   private Point _panStart;
   private Vector _panOrigin;

   /// <summary>Identifies the view model supplied by the containing screen.</summary>
   public static readonly DependencyProperty ComposerProperty = DependencyProperty.Register(nameof(Composer), typeof(ImageComposerVm), typeof(ImageComposer),
      new PropertyMetadata(null, ComposerChanged));
   /// <summary>Identifies the toolbar visibility setting.</summary>
   public static readonly DependencyProperty ShowToolbarProperty = DependencyProperty.Register(nameof(ShowToolbar), typeof(bool), typeof(ImageComposer), new PropertyMetadata(true));
   private static readonly DependencyPropertyKey HasChangesPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasChanges), typeof(bool), typeof(ImageComposer), new PropertyMetadata(false));
   /// <summary>Identifies the read-only dirty flag mirrored from the view model.</summary>
   public static readonly DependencyProperty HasChangesProperty = HasChangesPropertyKey.DependencyProperty;
   /// <summary>Identifies <see cref="CanvasWidthMm"/>.</summary>
   public static readonly DependencyProperty CanvasWidthMmProperty = DependencyProperty.Register(nameof(CanvasWidthMm), typeof(double), typeof(ImageComposer),
      new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SettingChanged));
   /// <summary>Canvas width in millimeters, clamped to 10–1000.</summary>
   public double CanvasWidthMm { get => (double)GetValue(CanvasWidthMmProperty); set => SetValue(CanvasWidthMmProperty, value); }
   /// <summary>Identifies <see cref="CanvasHeightMm"/>.</summary>
   public static readonly DependencyProperty CanvasHeightMmProperty = DependencyProperty.Register(nameof(CanvasHeightMm), typeof(double), typeof(ImageComposer),
      new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SettingChanged));
   /// <summary>Canvas height in millimeters, clamped to 10–1000.</summary>
   public double CanvasHeightMm { get => (double)GetValue(CanvasHeightMmProperty); set => SetValue(CanvasHeightMmProperty, value); }
   /// <summary>Identifies <see cref="OutputDpi"/>.</summary>
   public static readonly DependencyProperty OutputDpiProperty = DependencyProperty.Register(nameof(OutputDpi), typeof(int), typeof(ImageComposer),
      new FrameworkPropertyMetadata(300, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SettingChanged));
   /// <summary>PNG output resolution, clamped to 72–1200 dpi.</summary>
   public int OutputDpi { get => (int)GetValue(OutputDpiProperty); set => SetValue(OutputDpiProperty, value); }
   /// <summary>Identifies <see cref="IsReadOnly"/>.</summary>
   public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(ImageComposer),
      new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SettingChanged));
   /// <summary>Whether editing is disabled.</summary>
   public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
   /// <summary>Identifies <see cref="IsCanvasSizeEditable"/>.</summary>
   public static readonly DependencyProperty IsCanvasSizeEditableProperty = DependencyProperty.Register(nameof(IsCanvasSizeEditable), typeof(bool), typeof(ImageComposer),
      new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SettingChanged));
   /// <summary>Whether built-in canvas size inputs are shown.</summary>
   public bool IsCanvasSizeEditable { get => (bool)GetValue(IsCanvasSizeEditableProperty); set => SetValue(IsCanvasSizeEditableProperty, value); }

   /// <summary>The composer view model. An instance is created when none is supplied.</summary>
   public ImageComposerVm Composer { get => (ImageComposerVm)GetValue(ComposerProperty); set => SetValue(ComposerProperty, value); }
   /// <summary>Whether the built-in toolbar is visible.</summary>
   public bool ShowToolbar { get => (bool)GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }
   /// <summary>Whether the composition has unsaved changes.</summary>
   public bool HasChanges => (bool)GetValue(HasChangesProperty);

   /// <summary>Creates the control and its default view model without replacing the host DataContext.</summary>
   public ImageComposer() {
      InitializeComponent();
      SetCurrentValue(ComposerProperty, new ImageComposerVm());
      Loaded += (_, _) => { Observe(); SyncFromVm(); Composer.MainWindow = Window.GetWindow(this); UpdateScale(); };
      Unloaded += (_, _) => { Composer.EndPointer(); ReleaseCapture(); PropertyChangedEventManager.RemoveHandler(Composer, VmChanged, ""); };
   }
   private void Observe() {
      PropertyChangedEventManager.RemoveHandler(Composer, VmChanged, "");
      PropertyChangedEventManager.AddHandler(Composer, VmChanged, "");
   }
   private static void ComposerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
      var view = (ImageComposer)d;
      if (e.OldValue is ImageComposerVm old) PropertyChangedEventManager.RemoveHandler(old, view.VmChanged, "");
      if (e.NewValue is not ImageComposerVm vm) { view.SetCurrentValue(ComposerProperty, new ImageComposerVm()); return; }
      view.Observe(); view._shownZoom = vm.ZoomScale;
      foreach (var property in view._explicit) view.ApplySetting(vm, property);
      view.SyncFromVm();
   }
   private static void SettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
      var view = (ImageComposer)d;
      if (view._syncing) return;
      view._explicit.Add(e.Property);
      if (view.GetValue(ComposerProperty) is ImageComposerVm vm) {
         view._syncing = true;
         try { view.ApplySetting(vm, e.Property); } finally { view._syncing = false; }
         view.SyncFromVm();
      }
   }
   private void ApplySetting(ImageComposerVm vm, DependencyProperty property) {
      if (property == CanvasWidthMmProperty) vm.CanvasWidthMm = (double)GetValue(property);
      if (property == CanvasHeightMmProperty) vm.CanvasHeightMm = (double)GetValue(property);
      if (property == OutputDpiProperty) vm.OutputDpi = (int)GetValue(property);
      if (property == IsReadOnlyProperty) vm.IsReadOnly = (bool)GetValue(property);
      if (property == IsCanvasSizeEditableProperty) vm.IsCanvasSizeEditable = (bool)GetValue(property);
   }
   private void VmChanged(object? sender, PropertyChangedEventArgs e) {
      if (e.PropertyName == nameof(ImageComposerVm.ZoomPercent)) ZoomChanged();
      SyncFromVm(); UpdateScale();
      // Transform bindings may update after this handler; measure the overlay scale once they have.
      if (e.PropertyName is nameof(ImageComposerVm.ZoomPercent) or nameof(ImageComposerVm.PanX) or nameof(ImageComposerVm.PanY))
         Dispatcher.InvokeAsync(UpdateScale, DispatcherPriority.Render);
   }
   private void SyncFromVm() {
      if (_syncing || GetValue(ComposerProperty) is not ImageComposerVm vm) return;
      _syncing = true;
      try {
         SetCurrentValue(CanvasWidthMmProperty, vm.CanvasWidthMm);
         SetCurrentValue(CanvasHeightMmProperty, vm.CanvasHeightMm);
         SetCurrentValue(OutputDpiProperty, vm.OutputDpi);
         SetCurrentValue(IsReadOnlyProperty, vm.IsReadOnly);
         SetCurrentValue(IsCanvasSizeEditableProperty, vm.IsCanvasSizeEditable);
         SetValue(HasChangesPropertyKey, vm.HasChanges);
      }
      finally { _syncing = false; }
   }
   private void OnFocus(object sender, KeyboardFocusChangedEventArgs e) {
      Composer.MainWindow = Window.GetWindow(this);
      Composer.RefreshState();
   }
   private double Scale() {
      if (CanvasHost == null || Workspace == null || !CanvasHost.IsArrangeValid) return 1;
      var transform = CanvasHost.TransformToAncestor(Workspace);
      return Math.Max(.001, (transform.Transform(new Point(1, 0)) - transform.Transform(new Point())).Length);
   }
   private void UpdateScale() {
      if (Overlay == null) return;
      Overlay.PreviewScale = Scale(); Overlay.InvalidateVisual();
   }
   private void OnWorkspaceSizeChanged(object sender, SizeChangedEventArgs e) => UpdateScale();
   // Keeps the canvas point under the anchor (in the unzoomed workspace frame) fixed while the zoom changes.
   private void ZoomChanged() {
      var scale = Composer.ZoomScale;
      if (scale == _shownZoom) return;
      var anchor = _zoomAnchor ?? new Point(Zoomer.ActualWidth / 2, Zoomer.ActualHeight / 2);
      var ratio = scale / _shownZoom;
      _shownZoom = scale; _zoomAnchor = null;
      Composer.PanX = anchor.X - ratio * (anchor.X - Composer.PanX);
      Composer.PanY = anchor.Y - ratio * (anchor.Y - Composer.PanY);
   }
   private void OnWorkspaceWheel(object sender, MouseWheelEventArgs e) {
      if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
      var local = e.GetPosition(Zoomer);
      _zoomAnchor = new Point(Composer.PanX + local.X * _shownZoom, Composer.PanY + local.Y * _shownZoom);
      Composer.ZoomPercent *= Math.Pow(1.1, e.Delta / 120d);
      _zoomAnchor = null; e.Handled = true;
   }
   private void OnPanDown(object sender, MouseButtonEventArgs e) {
      if (e.ChangedButton != MouseButton.Middle || CanvasHost.IsMouseCaptured) return;
      _panStart = e.GetPosition(Workspace); _panOrigin = new Vector(Composer.PanX, Composer.PanY);
      _panning = Workspace.CaptureMouse();
      if (_panning) Workspace.Cursor = Cursors.SizeAll;
      e.Handled = true;
   }
   private void OnPanMove(object sender, MouseEventArgs e) {
      if (!_panning) return;
      var offset = _panOrigin + (e.GetPosition(Workspace) - _panStart);
      Composer.PanX = offset.X; Composer.PanY = offset.Y; e.Handled = true;
   }
   private void OnPanUp(object sender, MouseButtonEventArgs e) {
      if (e.ChangedButton != MouseButton.Middle || !_panning) return;
      Workspace.ReleaseMouseCapture(); e.Handled = true;
   }
   private void OnPanLostCapture(object sender, MouseEventArgs e) {
      if (!_panning || Workspace.IsMouseCaptured) return;
      _panning = false; Workspace.Cursor = null;
   }
   private void OnPointerDown(object sender, MouseButtonEventArgs e) {
      Focus(); UpdateScale();
      if (!Composer.BeginPointer(e.GetPosition(CanvasHost), 6 / Scale())) return;
      CanvasHost.CaptureMouse(); e.Handled = true;
   }
   private void OnPointerMove(object sender, MouseEventArgs e) {
      var point = e.GetPosition(CanvasHost);
      if (CanvasHost.IsMouseCaptured) { Composer.MovePointer(point, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; }
      var handle = Composer.HandleAt(point, 6 / Scale());
      CanvasHost.Cursor = handle switch {
         ComposerHandle.Rotate => Cursors.Hand,
         ComposerHandle.North or ComposerHandle.South => Cursors.SizeNS,
         ComposerHandle.West or ComposerHandle.East => Cursors.SizeWE,
         ComposerHandle.NorthWest or ComposerHandle.SouthEast => Cursors.SizeNWSE,
         ComposerHandle.NorthEast or ComposerHandle.SouthWest => Cursors.SizeNESW,
         ComposerHandle.Start or ComposerHandle.End => Cursors.Cross,
         _ => Composer.IsArrowTool || Composer.IsCropping ? Cursors.Cross : Cursors.Arrow
      };
      CanvasHost.ToolTip = Composer.Items.OfType<ComposerImageItem>().Reverse()
         .FirstOrDefault(i => i.IsLowResolution && ComposerGeometry.Hit(i, point, 6 / Scale()))?.ResolutionWarning;
   }
   private void OnPointerUp(object sender, MouseButtonEventArgs e) {
      if (!CanvasHost.IsMouseCaptured) return;
      Composer.MovePointer(e.GetPosition(CanvasHost), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
      Composer.EndPointer(); ReleaseCapture(); e.Handled = true;
   }
   private void ReleaseCapture() { if (CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture(); }
   private void OnLostCapture(object sender, MouseEventArgs e) => Composer.EndPointer();
   private void OnKeyDown(object sender, KeyEventArgs e) {
      if (e.OriginalSource is TextBoxBase or ComboBox) return;
      string? command = e.Key == Key.Delete ? "DeleteCommand" : e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? "PasteCommand" : null;
      if (command != null) {
         var action = Composer.Commands[command];
         if (action?.CanExecute(null) == true) action.Execute(null);
         e.Handled = true; return;
      }
      if (e.Key is Key.D0 or Key.NumPad0 && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
         var reset = Composer.Commands["ResetZoomCommand"];
         if (reset?.CanExecute(null) == true) reset.Execute(null);
         e.Handled = true; return;
      }
      if (e.Key == Key.Escape) { Composer.Escape(); ReleaseCapture(); e.Handled = true; return; }
      var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? .1 : 1;
      var delta = e.Key switch { Key.Left => new Vector(-step, 0), Key.Right => new Vector(step, 0), Key.Up => new Vector(0, -step), Key.Down => new Vector(0, step), _ => new Vector() };
      if (delta.Length > 0 && Composer.CanEdit) { Composer.MoveSelected(delta); e.Handled = true; }
   }
}

