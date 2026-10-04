using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using GiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Membuat sebuah elemen bisa diseret, cukup dengan memasang muatannya di XAML:
   /// <code>&lt;Grid shared:DragSource.Payload="{Binding}"&gt;</code>
   /// Elemennya sendiri yang menjadi bayangan yang ikut terseret, dan muatannya diserahkan apa
   /// adanya ke tempat ia dijatuhkan.
   /// <para>
   /// Kelas ini tidak tahu apa isi muatannya. Yang menyaring muatan mana yang boleh diterima
   /// adalah command di sisi penerima (<see cref="DropTarget"/>), bukan di sini - satu aturan di
   /// satu tempat.
   /// </para>
   /// </summary>
   public static class DragSource
   {
      /// <summary>
      /// Nama format tunggal yang dipakai seluruh drag di aplikasi ini. Satu nama untuk semua tipe
      /// muatan, supaya penerima cukup membuka satu pintu lalu memeriksa sendiri isinya.
      /// </summary>
      public const string PayloadFormat = "Em.Ui.Wpf.DragPayload";

      // Disimpan sekali, bukan dirangkai ulang setiap kali dipasang dan dicopot: AddHandler dan
      // RemoveHandler mencocokkan delegate-nya, dan menyerahkan method group dua kali menghasilkan
      // dua objek delegate berbeda - yang kedua tidak akan pernah mencopot yang pertama.
      private static readonly MouseButtonEventHandler ArmHandler = OnPreviewMouseLeftButtonDown;
      private static readonly MouseEventHandler MoveHandler = OnMouseMove;

      // Hanya ada satu drag pada satu saat, jadi keadaannya statis. Menyimpannya per elemen berarti
      // membayar satu attached property untuk sesuatu yang memang tidak bisa terjadi dua kali.
      private static FrameworkElement? _armed;
      private static Point _origin;
      private static Point _grip;
      private static bool _dragging;
      private static DragGhostAdorner? _ghost;
      private static FrameworkElement? _ghostHost;

      /// <summary>
      /// Muatan drag yang sedang berjalan dari aplikasi ini, atau <c>null</c> kalau tidak ada.
      /// <see cref="DropTarget"/> membacanya dari sini, bukan dari data drag-nya, karena muatan yang
      /// juga dibawa keluar aplikasi sebagai file (<see cref="IVirtualFileSource"/>) berjalan lewat
      /// data object COM yang tidak bisa membawa objek .NET apa adanya.
      /// </summary>
      public static object? ActivePayload { get; private set; }

      #region Payload

      /// <summary>
      /// Muatan yang dibawa elemen ini kalau ia diseret. Memasangnya sekaligus menyalakan seluruh
      /// plumbing drag pada elemen itu; melepasnya (<c>null</c>) mematikannya kembali.
      /// </summary>
      public static readonly DependencyProperty PayloadProperty = DependencyProperty.RegisterAttached(
         "Payload",
         typeof(object),
         typeof(DragSource),
         new PropertyMetadata(null, OnPayloadChanged));

      /// <summary>Membaca muatan yang terpasang pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dibaca.</param>
      /// <returns>Muatannya, atau <c>null</c> kalau elemen itu tidak bisa diseret.</returns>
      public static object? GetPayload(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return element.GetValue(PayloadProperty);
      }

      /// <summary>Memasang muatan pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dipasangi.</param>
      /// <param name="value">Muatannya; <c>null</c> berarti elemen ini tidak bisa diseret.</param>
      public static void SetPayload(DependencyObject element, object? value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(PayloadProperty, value);
      }

      #endregion

      #region IsDragActive

      /// <summary>
      /// Menyala di akar layar selama ada drag yang sedang berjalan, dan diwariskan ke seluruh
      /// isinya. Dibaca elemen yang perlu menyingkir selama drag - misalnya scrim sebuah side
      /// sheet, yang harus berhenti menangkap mouse supaya jatuhan bisa mendarat di belakangnya -
      /// tanpa elemen itu perlu kenal siapa yang sedang diseret.
      /// </summary>
      public static readonly DependencyProperty IsDragActiveProperty = DependencyProperty.RegisterAttached(
         "IsDragActive",
         typeof(bool),
         typeof(DragSource),
         new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

      /// <summary>Apakah ada drag yang sedang berjalan di atas elemen ini.</summary>
      /// <param name="element">Elemen yang dibaca.</param>
      /// <returns><c>true</c> selama drag berjalan.</returns>
      public static bool GetIsDragActive(DependencyObject element) {
         ArgumentNullException.ThrowIfNull(element);
         return (bool)element.GetValue(IsDragActiveProperty);
      }

      /// <summary>Menyalakan atau mematikan penanda drag pada sebuah elemen.</summary>
      /// <param name="element">Elemen yang dipasangi - biasanya akar layar.</param>
      /// <param name="value">Keadaannya.</param>
      public static void SetIsDragActive(DependencyObject element, bool value) {
         ArgumentNullException.ThrowIfNull(element);
         element.SetValue(IsDragActiveProperty, value);
      }

      #endregion

      private static void OnPayloadChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) {
         if (sender is not FrameworkElement element) return;

         // Dicopot lebih dulu tanpa syarat: muatan sebuah baris berganti setiap kali DataContext-nya
         // berganti, dan berlangganan dua kali berarti drag yang dimulai dua kali.
         element.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ArmHandler);
         element.RemoveHandler(UIElement.MouseMoveEvent, MoveHandler);

         if (e.NewValue == null) return;

         // handledEventsToo, bukan '+=' biasa. Kepala kartu yang bisa dilipat adalah ToggleButton,
         // dan ToggleButton menandai MouseMove sebagai sudah ditangani selama ia tertekan. Dengan
         // langganan biasa, kartu yang dipegang dari kepalanya tidak akan pernah berangkat - dan
         // kepalanya justru satu-satunya bagian kartu yang tidak tertutup baris claim.
         element.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, ArmHandler, true);
         element.AddHandler(UIElement.MouseMoveEvent, MoveHandler, true);
      }

      // Preview, bukan bubbling: kepala kartu yang bisa dilipat adalah ToggleButton, dan ToggleButton
      // menelan MouseLeftButtonDown yang biasa. Menerowong dari luar ke dalam berarti elemen terdalam
      // yang terakhir mencatat dirinya - dan yang terdalam itulah yang paling tepat dimaksud orangnya.
      private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
         if (_dragging || sender is not FrameworkElement element) return;

         _armed = element;
         _grip = e.GetPosition(element);
         _origin = e.GetPosition(null);
      }

      // Bubbling, bukan preview: baris claim ada di dalam kartu module dan keduanya membawa muatan,
      // dan naik dari dalam ke luar berarti barisnya yang menang. Yang memutuskan siapa berangkat
      // bukan e.Handled melainkan _armed - elemen terdalam yang tercatat saat tombol ditekan.
      // Karena langganannya menerima event yang sudah ditangani, e.Handled tidak lagi menghentikan
      // siapa pun di sini, dan _armed memang penjaga yang lebih tepat: ia menjawab "yang ini yang
      // dimaksud", bukan sekadar "sudah ada yang mengurus".
      private static void OnMouseMove(object sender, MouseEventArgs e) {
         if (_dragging || sender is not FrameworkElement element) return;
         if (!ReferenceEquals(_armed, element)) return;

         if (e.LeftButton != MouseButtonState.Pressed) {
            _armed = null;
            return;
         }

         var current = e.GetPosition(null);
         if (Math.Abs(current.X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
             Math.Abs(current.Y - _origin.Y) < SystemParameters.MinimumVerticalDragDistance) {
            return;
         }

         if (GetPayload(element) is not { } payload) {
            _armed = null;
            return;
         }

         StartDrag(element, payload);
      }

      private static void StartDrag(FrameworkElement source, object payload) {
         var host = FindAdornerHost(source);
         var layer = host == null ? null : AdornerLayer.GetAdornerLayer(host);

         _armed = null;
         _dragging = true;

         try {
            // ToggleButton memegang mouse capture selama ia ditekan, dan memulai drag selagi capture
            // masih dipegangnya membuat tombolnya tertinggal dalam keadaan tertekan. Melepaskannya di
            // sini sekaligus membuat seret-lalu-lepas tidak ikut memicu Click-nya, sementara klik
            // biasa - yang tidak pernah sampai ke sini - tetap bekerja seperti biasa.
            Mouse.Capture(null);

            if (host != null && layer != null) {
               _ghostHost = host;
               _ghost = new DragGhostAdorner(host, source, _grip);
               layer.Add(_ghost);
               SetIsDragActive(host, true);
            }

            ActivePayload = payload;

            if (payload is IVirtualFileSource virtualFiles) {
               // Copy for the outside world, Move for a target inside the application that relocates
               // what it receives: the target picks, and OLE only lets it pick from what is offered.
               NativeDragDrop.Run(new VirtualFileDataObject(virtualFiles, PayloadFormat),
                  NativeDragDrop.DropEffectCopy | NativeDragDrop.DropEffectMove, MoveGhost);
            }
            else {
               source.GiveFeedback += OnGiveFeedback;
               DragDrop.DoDragDrop(source, new DataObject(PayloadFormat, payload), DragDropEffects.Copy);
            }
         }
         finally {
            source.GiveFeedback -= OnGiveFeedback;
            ActivePayload = null;

            if (_ghost != null) layer?.Remove(_ghost);
            if (host != null) SetIsDragActive(host, false);

            _ghost = null;
            _ghostHost = null;
            _dragging = false;
         }
      }

      // DragOver hanya menyala di atas target yang sah, jadi ia tidak cukup untuk menggerakkan
      // bayangan ke mana pun kursornya pergi. GiveFeedback menyala di sumbernya, terus-menerus,
      // sepanjang drag - tapi ia tidak membawa posisi kursor, jadi posisinya ditanyakan ke Windows.
      private static void OnGiveFeedback(object sender, GiveFeedbackEventArgs e) {
         e.UseDefaultCursors = false;
         e.Handled = true;

         Mouse.SetCursor(e.Effects == DragDropEffects.None ? Cursors.No : Cursors.Arrow);
         MoveGhost();
      }

      // The ghost lives in the screen's adorner layer, so beyond the screen's edge it would only
      // be clipped against it; it is hidden there instead, which is also what a drag heading out to
      // another application looks like.
      private static void MoveGhost() {
         if (_ghost == null || _ghostHost == null) return;
         if (!GetCursorPos(out var cursor)) return;

         var point = _ghostHost.PointFromScreen(new Point(cursor.X, cursor.Y));
         var inside = point.X >= 0 && point.Y >= 0 &&
                      point.X <= _ghostHost.ActualWidth && point.Y <= _ghostHost.ActualHeight;
         _ghost.Visibility = inside ? Visibility.Visible : Visibility.Hidden;
         _ghost.Offset(point);
      }

      // Bayangannya digambar di adorner layer milik layar yang memuat elemennya, bukan milik jendela:
      // sumber dan tujuan sebuah drag ada di dalam satu layar, dan memilih layar membuat koordinatnya
      // tetap sederhana.
      private static FrameworkElement? FindAdornerHost(DependencyObject source) {
         for (var node = source; node != null; node = VisualTreeHelper.GetParent(node)) {
            if (node is UserControl or Window) return (FrameworkElement)node;
         }

         return null;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct NativePoint
      {
         public int X;
         public int Y;
      }

      [DllImport("user32.dll", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      private static extern bool GetCursorPos(out NativePoint point);
   }
}
