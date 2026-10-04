using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Penghubung aplikasi dengan <c>launcher.exe</c>, program kecil yang memasang, memperbarui, dan
   /// menjalankan aplikasi di komputer user. Kelas ini menjawab tiga hal: apakah aplikasi terpasang
   /// lewat launcher (<see cref="IsManaged"/>), di mana launcher-nya (<see cref="LauncherPath"/>), dan
   /// identitas taskbar mana yang dipakai (<see cref="AppUserModelId"/>).
   /// </summary>
   /// <remarks>
   /// Launcher selalu dijalankan lebih dulu, lalu ia menjalankan aplikasi dengan dua environment
   /// variable (<see cref="PathVariable"/> dan <see cref="AppIdVariable"/>). Aplikasi yang terpasang
   /// lewat launcher tetapi dibuka langsung dari folder instalasinya (tanpa environment variable itu)
   /// menyerahkan dirinya ke launcher lalu keluar, supaya pengecekan update tidak terlewat. Aplikasi yang
   /// tidak terpasang lewat launcher (dijalankan dari Visual Studio atau folder publish) tidak terpengaruh
   /// sama sekali.
   /// </remarks>
   public static class LauncherIntegration
   {
      /// <summary>
      /// Nama environment variable berisi path lengkap launcher, dipasang launcher saat menjalankan aplikasi.
      /// </summary>
      public const string PathVariable = "LAUNCHER_PATH";

      /// <summary>
      /// Nama environment variable berisi AppUserModelID produk, yaitu identitas yang dipakai Windows untuk
      /// mengelompokkan jendela di taskbar dan menentukan apa yang dijalankan pin taskbar.
      /// </summary>
      public const string AppIdVariable = "LAUNCHER_APP_ID";

      /// <summary>
      /// Nama file yang menandai folder berisi satu versi aplikasi yang dipasang launcher. Tanpa file ini
      /// aplikasi dianggap tidak terpasang lewat launcher.
      /// </summary>
      public const string ReleaseMarkerFileName = "release.json";

      private const string LauncherFileName = "launcher.exe";

      // Tells the launcher it was started by an application that was opened directly, only so that the
      // launcher's log can say so. Everything after "--" is handed back to the application untouched.
      private const string FromAppArgument = "--from-app";

      private static bool _initialized;

      /// <summary>
      /// <c>true</c> kalau aplikasi berjalan dari folder versi yang dipasang launcher, baik dijalankan
      /// launcher maupun dibuka langsung.
      /// </summary>
      public static bool IsManaged { get; private set; }

      /// <summary>
      /// Path lengkap launcher yang menjalankan aplikasi ini, atau <c>null</c> kalau aplikasi tidak
      /// dijalankan lewat launcher. Dipakai sebagai perintah yang dijalankan pin taskbar.
      /// </summary>
      public static string? LauncherPath { get; private set; }

      /// <summary>
      /// AppUserModelID yang dipasang di proses dan setiap jendela utama, atau <c>null</c> kalau aplikasi
      /// tidak dijalankan lewat launcher (Windows lalu memakai identitas bawaannya sendiri).
      /// </summary>
      public static string? AppUserModelId { get; private set; }

      // Called first thing in EmApp.BuildApp, before any window exists: a process AppUserModelID only
      // counts when it is set before the application shows any UI. An application that is managed but was
      // opened directly hands its arguments to the launcher and ends here, never returning to the caller;
      // when the launcher is missing or fails to start, the application simply runs on its own.
      internal static void Initialize(string[] args) {
         if (_initialized) return;
         _initialized = true;

         var baseDirectory = AppContext.BaseDirectory;
         IsManaged = File.Exists(Path.Combine(baseDirectory, ReleaseMarkerFileName));

         var launcherPath = Environment.GetEnvironmentVariable(PathVariable);
         var appId = Environment.GetEnvironmentVariable(AppIdVariable);
         if (!string.IsNullOrEmpty(launcherPath) && !string.IsNullOrEmpty(appId)) {
            LauncherPath = launcherPath;
            AppUserModelId = appId;
            // A failure only costs the taskbar grouping, never the application itself.
            _ = SetCurrentProcessExplicitAppUserModelID(appId);
            return;
         }

         if (!IsManaged) return;
         // The installation root sits one level above the version folder (<install>\app-<id>\). The copy
         // of launcher.exe inside the version folder itself is only the one the next update installs.
         var rootLauncher = Path.GetFullPath(Path.Combine(baseDirectory, "..", LauncherFileName));
         if (!File.Exists(rootLauncher)) return;
         if (StartLauncher(rootLauncher, args)) Environment.Exit(0);
      }

      private static bool StartLauncher(string launcherPath, string[] args) {
         var startInfo = new ProcessStartInfo(launcherPath) {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!
         };
         startInfo.ArgumentList.Add(FromAppArgument);
         startInfo.ArgumentList.Add("--");
         foreach (var arg in args) startInfo.ArgumentList.Add(arg);

         try {
            using var process = Process.Start(startInfo);
            return process != null;
         }
         catch (Win32Exception) {
            return false;
         }
      }

      /// <summary>
      /// Memasang AppUserModelID dan perintah relaunch di <paramref name="window"/>, supaya jendela itu
      /// masuk grup taskbar yang sama dengan shortcut Start menu, dan pin taskbar dari jendela itu
      /// menjalankan launcher, bukan exe aplikasi di folder versinya. Tidak melakukan apa-apa kalau
      /// aplikasi tidak dijalankan lewat launcher.
      /// </summary>
      /// <remarks>
      /// Properti dipasang saat handle jendela dibuat, dan dibuang lagi saat jendela ditutup, karena
      /// Windows tidak membebaskannya sendiri.
      /// </remarks>
      /// <param name="window">Jendela yang diberi identitas.</param>
      /// <param name="displayName">Nama yang ditampilkan untuk pin taskbar, biasanya nama aplikasi.</param>
      public static void AttachToWindow(Window window, string displayName) {
         ArgumentNullException.ThrowIfNull(window);
         if (LauncherPath == null || AppUserModelId == null) return;

         var launcherPath = LauncherPath;
         var appId = AppUserModelId;
         window.SourceInitialized += (_, _) => {
            if (PresentationSource.FromVisual(window) is not HwndSource source) return;
            // Relaunch properties first: the taskbar reads them when the ID is set, and ignores the
            // command unless its display name is set as well.
            SetWindowProperties(source.Handle,
               (RelaunchCommandKey, $"\"{launcherPath}\""),
               (RelaunchDisplayNameResourceKey, displayName),
               (RelaunchIconResourceKey, $"{launcherPath},0"),
               (AppUserModelIdKey, appId));
            source.AddHook(RemoveOnDestroy);
         };
      }

      // Window properties must be gone before the window is: WM_DESTROY still has a live handle.
      private static IntPtr RemoveOnDestroy(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
         const int WM_DESTROY = 0x0002;
         if (msg == WM_DESTROY)
            SetWindowProperties(hwnd,
               (AppUserModelIdKey, null),
               (RelaunchCommandKey, null),
               (RelaunchDisplayNameResourceKey, null),
               (RelaunchIconResourceKey, null));
         return IntPtr.Zero;
      }

      // A null value removes the property (VT_EMPTY). Failures are swallowed: the properties only
      // decide taskbar grouping and pinning, and are not worth breaking a window over.
      private static void SetWindowProperties(IntPtr hwnd, params (PropertyKey Key, string? Value)[] properties) {
         var iid = typeof(IPropertyStore).GUID;
         if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return;
         try {
            foreach (var (key, value) in properties) {
               var variant = new PropVariant();
               if (value != null) {
                  variant.ValueType = VT_LPWSTR;
                  variant.Pointer = Marshal.StringToCoTaskMemUni(value);
               }
               try {
                  var k = key;
                  _ = store.SetValue(ref k, ref variant);
               }
               finally {
                  _ = PropVariantClear(ref variant);
               }
            }
         }
         finally {
            Marshal.ReleaseComObject(store);
         }
      }

      #region Native

      private const ushort VT_LPWSTR = 31;

      // PKEYs from propkey.h: the System.AppUserModel format ID, with the property IDs of ID (5),
      // RelaunchCommand (2), RelaunchIconResource (3) and RelaunchDisplayNameResource (4).
      private static readonly Guid AppUserModelFormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
      private static readonly PropertyKey AppUserModelIdKey = new(AppUserModelFormatId, 5);
      private static readonly PropertyKey RelaunchCommandKey = new(AppUserModelFormatId, 2);
      private static readonly PropertyKey RelaunchIconResourceKey = new(AppUserModelFormatId, 3);
      private static readonly PropertyKey RelaunchDisplayNameResourceKey = new(AppUserModelFormatId, 4);

      [StructLayout(LayoutKind.Sequential)]
      private readonly struct PropertyKey(Guid formatId, uint propertyId)
      {
         public readonly Guid FormatId = formatId;
         public readonly uint PropertyId = propertyId;
      }

      // Only the VT_LPWSTR / VT_EMPTY shapes are used; the trailing field pads the struct to the size of
      // a native PROPVARIANT (16 bytes on x86, 24 on x64).
      [StructLayout(LayoutKind.Sequential)]
      private struct PropVariant
      {
         public ushort ValueType;
         public ushort Reserved1;
         public ushort Reserved2;
         public ushort Reserved3;
         public IntPtr Pointer;
         public IntPtr Padding;
      }

      [ComImport]
      [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
      [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
      private interface IPropertyStore
      {
         [PreserveSig] int GetCount(out uint count);
         [PreserveSig] int GetAt(uint index, out PropertyKey key);
         [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
         [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
         [PreserveSig] int Commit();
      }

      [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
      private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

      [DllImport("shell32.dll")]
      private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid,
         [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);

      [DllImport("ole32.dll")]
      private static extern int PropVariantClear(ref PropVariant value);

      #endregion
   }
}
