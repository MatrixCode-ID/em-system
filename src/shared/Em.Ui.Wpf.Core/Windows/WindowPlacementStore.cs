using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// Mengingat posisi, ukuran, status maximize, dan monitor window utama antar sesi. Posisinya diambil
   /// dengan <c>GetWindowPlacement</c> saat window ditutup, disimpan sebagai satu nilai biner di
   /// <c>HKCU\{ApplicationName}</c> (jadi per user Windows), lalu dipulihkan dengan
   /// <c>SetWindowPlacement</c> sebelum window tampil.
   /// </summary>
   /// <remarks>
   /// Yang dipakai Windows sendiri, bukan koordinat hitungan WPF: pada multimonitor, DPI berbeda antar
   /// monitor, atau monitor yang sudah dicabut, <c>SetWindowPlacement</c> menggeser window ke layar yang
   /// masih ada. Hanya window utama yang diingat; window hasil tab yang ditarik keluar dan window detach
   /// sengaja tidak.
   /// </remarks>
   internal static class WindowPlacementStore
   {
      private const string ValueName = "MainWindowPlacement";

      private const int SW_SHOWNORMAL = 1;
      private const int SW_SHOWMINIMIZED = 2;
      private const int SW_SHOWMAXIMIZED = 3;
      private const int WPF_RESTORETOMAXIMIZED = 0x2;

      // Mirrors the Win32 WINDOWPLACEMENT struct: eleven 32-bit integers, 44 bytes, so it can be
      // stored byte for byte.
      [StructLayout(LayoutKind.Sequential)]
      internal struct Placement
      {
         public int Length;
         public int Flags;
         public int ShowCmd;
         public int MinPositionX;
         public int MinPositionY;
         public int MaxPositionX;
         public int MaxPositionY;
         public int NormalLeft;
         public int NormalTop;
         public int NormalRight;
         public int NormalBottom;

         public readonly bool IsMaximized => ShowCmd == SW_SHOWMAXIMIZED;
      }

      /// <summary>
      /// Membaca posisi yang tersimpan, atau <c>null</c> kalau belum ada atau isinya tidak masuk akal.
      /// Window yang tertutup dalam keadaan minimize dikembalikan ke keadaan sebelum diminimize.
      /// </summary>
      internal static Placement? Load(EmApp app) {
         // A window position is a convenience: nothing here may stop the application from opening.
         try {
            using var baseKey = app.BaseRegKey;
            if (baseKey.GetValue(ValueName) is not byte[] bytes || bytes.Length != Marshal.SizeOf<Placement>()) return null;

            var placement = MemoryMarshal.Read<Placement>(bytes);
            if (placement.NormalRight - placement.NormalLeft < 1 || placement.NormalBottom - placement.NormalTop < 1) return null;

            // Never reopen minimized: go back to what the window was before it was minimized.
            if (placement.ShowCmd == SW_SHOWMINIMIZED)
               placement.ShowCmd = (placement.Flags & WPF_RESTORETOMAXIMIZED) != 0 ? SW_SHOWMAXIMIZED : SW_SHOWNORMAL;
            else if (placement.ShowCmd != SW_SHOWMAXIMIZED)
               placement.ShowCmd = SW_SHOWNORMAL;
            placement.Length = Marshal.SizeOf<Placement>();
            return placement;
         }
         catch (Exception x) when (x is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) {
            return null;
         }
      }

      /// <summary>
      /// Memasang <paramref name="placement"/> ke window. Harus dipanggil setelah handle window ada
      /// (<c>SourceInitialized</c>) dan sebelum window tampil.
      /// </summary>
      internal static void Apply(Window window, Placement placement) {
         if (new WindowInteropHelper(window).Handle is var handle && handle == IntPtr.Zero) return;
         _ = SetWindowPlacement(handle, ref placement);
      }

      /// <summary>
      /// Menyimpan posisi window saat ini. Dipanggil saat window utama benar-benar akan ditutup.
      /// </summary>
      internal static void Save(Window window, EmApp app) {
         try {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
            if (!GetWindowPlacement(handle, ref placement)) return;

            var bytes = new byte[Marshal.SizeOf<Placement>()];
            MemoryMarshal.Write(bytes, in placement);
            using var baseKey = app.BaseRegKey;
            baseKey.SetValue(ValueName, bytes, RegistryValueKind.Binary);
         }
         catch (Exception x) when (x is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) {
            // Not remembering the position is not worth a failed close.
         }
      }

      [DllImport("user32.dll")]
      [return: MarshalAs(UnmanagedType.Bool)]
      private static extern bool GetWindowPlacement(IntPtr hWnd, ref Placement lpwndpl);

      [DllImport("user32.dll")]
      [return: MarshalAs(UnmanagedType.Bool)]
      private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref Placement lpwndpl);
   }
}
