using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// The window where a detached navigation entry is shown by itself, e.g. so two documents can be
   /// compared side by side. Its content is one <see cref="SpaNavigationHost"/> without home, without the
   /// application menu, and without the account button, with its own <see cref="NavigationStack"/>; the
   /// detached entry becomes the root of that stack. Created by <see cref="EmApp.DetachAsync"/>, not by
   /// modules.
   /// </summary>
   public partial class DetachedWindow : EmWindow
   {
      // How far a new window is shifted from the one it was detached from, and from any detached
      // window already standing where it would land.
      private const double CascadeStep = 30;

      private readonly EmApp _app;
      private bool _closeAgreed;
      private bool _closed;
      private Task _release = Task.CompletedTask;

      internal DetachedWindow(EmApp app, NavigationStack stack) {
         _app = app;
         Stack = stack;
         InitializeComponent();

         Vm.EmApp = app;
         Vm.Stack = stack;
         Content = new SpaNavigationHost(app, stack);

         stack.Changed += StackChanged;
      }

      /// <summary>The stack shown by this window. It has no home.</summary>
      public NavigationStack Stack { get; }

      /// <summary>The view model of this window.</summary>
      public DetachedWindowVm Vm => (DetachedWindowVm)DataContext;

      // A detached window never stands empty: once its last entry has left - typically a root body
      // closing itself after a save - the window goes with it. The body already agreed to leave on
      // the way out, so it is not asked a second time.
      private void StackChanged(object? sender, EventArgs e) {
         if (_closed || Stack.Current != null || Stack.Entries.Count > 0) return;

         _closeAgreed = true;
         Close();
      }

      // Closing through the title bar asks the body being shown first, the same way leaving it by
      // navigation would. OnNavigatingAway is asynchronous and the close cannot wait for it, so the
      // first close is always cancelled and, once the body agrees, repeated with the answer attached.
      /// <inheritdoc />
      protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
         base.OnClosing(e);
         if (e.Cancel || _closeAgreed) return;

         e.Cancel = true;
         // Deferred rather than started here: a body that answers at once would otherwise have
         // Close called again while this window is still inside its own closing, which WPF refuses.
         Dispatcher.InvokeAsync(AskToCloseAsync);
      }

      private async Task AskToCloseAsync() {
         try {
            if (_closed || !await Stack.AskCurrentToLeave()) return;

            _closeAgreed = true;
            Close();
         }
         catch (Exception x) {
            this.ShowMboxError(x);
         }
      }

      /// <inheritdoc />
      protected override void OnClosed(EventArgs e) {
         _closed = true;
         Stack.Changed -= StackChanged;
         _app.UnregisterDetachedWindow(this);
         _release = ReleaseStackAsync();
         base.OnClosed(e);
      }

      // Every body left on the stack is released once the window is gone, the one that was on screen
      // included - nothing of it is mounted anywhere any more.
      private async Task ReleaseStackAsync() {
         try {
            await Stack.ReleaseAll();
         }
         catch (Exception x) {
            _app.MainWindow.ShowMboxError(x);
         }
      }

      // Closes this window without asking anyone and waits until every body on it has been released.
      // Used when the session is gone, and when the main window's closing has already been agreed to.
      internal async Task CloseWithoutAskingAsync() {
         if (!_closed) {
            _closeAgreed = true;
            Close();
         }

         await _release;
      }

      /// <summary>
      /// Places this window beside <paramref name="source"/>, the window the entry came from: its size equals
      /// the normal size of the source window (the size before maximizing when the source window is
      /// maximized), its position is shifted a little to the lower right - further still if that place is
      /// already taken by another detached window - and it stays inside the work area of the source window's
      /// screen.
      /// </summary>
      /// <param name="source">The window the detached entry came from.</param>
      /// <param name="others">The detached windows that are already open, so they are not stacked exactly.</param>
      internal void PlaceBeside(Window source, IEnumerable<Window> others) {
         var bounds = source.WindowState == WindowState.Normal
            ? new Rect(source.Left, source.Top, source.ActualWidth, source.ActualHeight)
            : source.RestoreBounds;
         if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) {
            bounds = new Rect(source.Left, source.Top, Width, Height);
         }

         var workArea = WorkAreaOf(source);
         var taken = others.Select(r => new System.Windows.Point(r.Left, r.Top)).ToList();

         var left = bounds.Left + CascadeStep;
         var top = bounds.Top + CascadeStep;
         while (taken.Any(r => Math.Abs(r.X - left) < 1 && Math.Abs(r.Y - top) < 1)) {
            left += CascadeStep;
            top += CascadeStep;
         }

         // The size is only given up where the shifted window would run off the work area, and never
         // below the window's own minimum; past that the position is pulled back instead.
         var width = Math.Max(MinWidth, Math.Min(bounds.Width, workArea.Right - left));
         var height = Math.Max(MinHeight, Math.Min(bounds.Height, workArea.Bottom - top));
         left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
         top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));

         WindowStartupLocation = WindowStartupLocation.Manual;
         WindowState = WindowState.Normal;
         Left = left;
         Top = top;
         Width = width;
         Height = height;
      }

      // The work area of the monitor the source window is on, in device-independent units. WPF only
      // knows the primary monitor's (SystemParameters.WorkArea), which is the fallback when the
      // window has no handle or source to ask through.
      private static Rect WorkAreaOf(Window window) {
         var handle = new WindowInteropHelper(window).Handle;
         var monitor = handle == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(handle, MonitorDefaultToNearest);
         var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
         if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;

         var work = new Rect(info.Work.Left, info.Work.Top,
            info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
         if (PresentationSource.FromVisual(window)?.CompositionTarget is not { } target) return SystemParameters.WorkArea;

         var topLeft = target.TransformFromDevice.Transform(work.TopLeft);
         var bottomRight = target.TransformFromDevice.Transform(work.BottomRight);
         return new Rect(topLeft, bottomRight);
      }

      private const uint MonitorDefaultToNearest = 2;

      [StructLayout(LayoutKind.Sequential)]
      private struct NativeRect
      {
         public int Left, Top, Right, Bottom;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct MonitorInfo
      {
         public int Size;
         public NativeRect Monitor;
         public NativeRect Work;
         public uint Flags;
      }

      [DllImport("user32.dll")]
      private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

      [DllImport("user32.dll")]
      [return: MarshalAs(UnmanagedType.Bool)]
      private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
   }

   /// <summary>
   /// The view model of <see cref="DetachedWindow"/>: it follows that window's stack so the window title is
   /// always the same as the title of the entry being shown - including after the entry changes its title -
   /// and two detached documents are easy to tell apart on the taskbar.
   /// </summary>
   public class DetachedWindowVm : MvvmModelBase
   {
      private NavigationEntry? _entry;

      /// <summary>The stack shown by this window.</summary>
      public NavigationStack? Stack {
         get;
         set {
            if (field != null) field.PropertyChanged -= StackPropertyChanged;
            field = value;
            if (field != null) field.PropertyChanged += StackPropertyChanged;
            Track(field?.Current);
         }
      }

      /// <summary>
      /// The window title: the title of the entry being shown, or the application name while there is no entry.
      /// </summary>
      public string WindowTitle => _entry?.Title ?? EmApp?.ApplicationName ?? string.Empty;

      private void StackPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationStack.Current)) Track(Stack?.Current);
      }

      private void Track(NavigationEntry? entry) {
         if (_entry != null) _entry.PropertyChanged -= EntryPropertyChanged;
         _entry = entry;
         if (_entry != null) _entry.PropertyChanged += EntryPropertyChanged;
         NotifyChanged(nameof(WindowTitle));
      }

      private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationEntry.Title)) NotifyChanged(nameof(WindowTitle));
      }
   }
}
