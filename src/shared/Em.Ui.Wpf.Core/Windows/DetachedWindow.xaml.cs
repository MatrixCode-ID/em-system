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
   /// Window tempat sebuah entri navigasi yang di-detach tampil sendiri, mis. supaya dua dokumen bisa
   /// dibandingkan berdampingan. Isinya satu <see cref="SpaNavigationHost"/> tanpa home, tanpa menu
   /// aplikasi, dan tanpa tombol akun, dengan <see cref="NavigationStack"/>-nya sendiri; entri yang
   /// di-detach menjadi akar stack itu. Dibuat oleh <see cref="EmApp.DetachAsync"/>, bukan oleh
   /// module.
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

      /// <summary>Stack yang ditampilkan window ini. Tidak punya home.</summary>
      public NavigationStack Stack { get; }

      /// <summary>ViewModel window ini.</summary>
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
      /// Menempatkan window ini di samping <paramref name="source"/>, window asal entrinya: ukurannya
      /// sama dengan ukuran normal window asal (ukuran sebelum di-maximize kalau window asal sedang
      /// maximized), posisinya digeser sedikit ke kanan-bawah - lebih jauh lagi kalau tempat itu sudah
      /// ditempati window detach lain - dan tetap di dalam area kerja layar window asal.
      /// </summary>
      /// <param name="source">Window asal entri yang di-detach.</param>
      /// <param name="others">Window detach yang sudah terbuka, supaya tidak ditumpuk persis.</param>
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
   /// ViewModel <see cref="DetachedWindow"/>: mengikuti stack window itu supaya judul window selalu
   /// sama dengan judul entri yang sedang tampil - termasuk sesudah entrinya mengganti judul - dan
   /// dua dokumen yang di-detach mudah dibedakan di taskbar.
   /// </summary>
   public class DetachedWindowVm : MvvmModelBase
   {
      private NavigationEntry? _entry;

      /// <summary>Stack yang ditampilkan window ini.</summary>
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
      /// Judul window: judul entri yang sedang tampil, atau nama aplikasi selama belum ada entri.
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
