using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// The application's link with <c>launcher.exe</c>, a small program that installs, updates, and runs the
   /// application on the user's computer. This class answers three things: whether the application is
   /// installed through the launcher (<see cref="IsManaged"/>), where the launcher is
   /// (<see cref="LauncherPath"/>), and which taskbar identity is used (<see cref="AppUserModelId"/>).
   /// </summary>
   /// <remarks>
   /// The launcher is always run first, then it runs the application with two environment variables
   /// (<see cref="PathVariable"/> and <see cref="AppIdVariable"/>). An application that is installed
   /// through the launcher but opened directly from its installation folder (without those environment
   /// variables) hands itself over to the launcher and exits, so the update check is not skipped. An
   /// application that is not installed through the launcher (run from Visual Studio or a publish folder)
   /// is not affected at all.
   /// </remarks>
   public static class LauncherIntegration
   {
      /// <summary>
      /// The name of the environment variable holding the full path of the launcher, set by the launcher when
      /// it runs the application.
      /// </summary>
      public const string PathVariable = "LAUNCHER_PATH";

      /// <summary>
      /// The name of the environment variable holding the product's AppUserModelID, which is the identity
      /// Windows uses to group windows on the taskbar and to decide what a taskbar pin runs.
      /// </summary>
      public const string AppIdVariable = "LAUNCHER_APP_ID";

      /// <summary>
      /// The name of the file that marks a folder holding one version of the application installed by the
      /// launcher. Without this file the application is considered not installed through the launcher.
      /// </summary>
      public const string ReleaseMarkerFileName = "release.json";

      private const string LauncherFileName = "launcher.exe";

      // Tells the launcher it was started by an application that was opened directly, only so that the
      // launcher's log can say so. Everything after "--" is handed back to the application untouched.
      private const string FromAppArgument = "--from-app";

      private static bool _initialized;

      /// <summary>
      /// <c>true</c> when the application runs from a version folder installed by the launcher, whether run by
      /// the launcher or opened directly.
      /// </summary>
      public static bool IsManaged { get; private set; }

      /// <summary>
      /// The full path of the launcher that runs this application, or <c>null</c> when the application is not
      /// run through the launcher. Used as the command that a taskbar pin runs.
      /// </summary>
      public static string? LauncherPath { get; private set; }

      /// <summary>
      /// The AppUserModelID installed on the process and every main window, or <c>null</c> when the
      /// application is not run through the launcher (Windows then uses its own default identity).
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
      /// Installs the AppUserModelID and the relaunch command on <paramref name="window"/>, so that window
      /// joins the same taskbar group as the Start menu shortcut, and a taskbar pin from that window runs the
      /// launcher, not the application exe in its version folder. Does nothing when the application is not run
      /// through the launcher.
      /// </summary>
      /// <remarks>
      /// The properties are installed when the window handle is created, and removed again when the window is
      /// closed, because Windows does not release them by itself.
      /// </remarks>
      /// <param name="window">The window that is given the identity.</param>
      /// <param name="displayName">The name shown for the taskbar pin, usually the application name.</param>
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
