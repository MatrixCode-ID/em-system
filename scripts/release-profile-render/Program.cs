using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Publish;
using Microsoft.Win32;

internal static class Program
{
   [STAThread]
   private static void Main(string[] args) {
      var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
      var output = Path.GetFullPath(args.FirstOrDefault() ?? "../.artefacts/em-system/release-profile-render");
      Directory.CreateDirectory(output);
      var temporary = Path.Combine(Path.GetTempPath(), "em-release-profile-render-" + Guid.NewGuid().ToString("N"));
      var registry = "EmReleaseProfileRender-" + Guid.NewGuid().ToString("N");
      var preferences = new ReleaseManagerPreferences(() => Registry.CurrentUser.CreateSubKey(registry));
      var secrets = new ReleaseSigningSecrets(Path.Combine(temporary, "secrets"));
      var fake = (EmApp)RuntimeHelpers.GetUninitializedObject(typeof(EmApp));
      var apply = typeof(EmApp).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;
      var images = 0;
      void Save(FrameworkElement view, string name, double width, double height) {
         view.Width = width - view.Margin.Left - view.Margin.Right;
         view.Height = height - view.Margin.Top - view.Margin.Bottom;
         app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
         var host = new Border { Width = width, Height = height, Background = (Brush)app.Resources["themeWindowBackgroundBrush"], Child = view };
         host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
         app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
         var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
         void Invalidate(DependencyObject node) {
            if (node is UIElement element) { element.InvalidateMeasure(); element.InvalidateArrange(); }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Invalidate(VisualTreeHelper.GetChild(node, i));
         }
         Invalidate(host);
         host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
         bitmap.Render(host);
         var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
         using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
         host.Child = null; images++;
      }
      void Dialog(Window dialog, string name, double height) {
         var content = (FrameworkElement)dialog.Content;
         content.DataContext = dialog.DataContext;
         content.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "themeWindowForegroundBrush");
         dialog.Content = null; content.Resources = dialog.Resources;
         Save(content, name, dialog.Width, height);
         dialog.Close();
      }
      try {
         foreach (var dark in new[] { false, true }) {
            apply.Invoke(null, [app.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
            var theme = dark ? "dark" : "light";
            var store = new ReleaseProfileStore(Path.Combine(temporary, theme));
            var view = new ReleaseManager();
            view.SetResourceReference(Control.ForegroundProperty, "themeWindowForegroundBrush");
            view.Vm.Initialize(preferences, store, secrets);
            Save(view, theme + "-empty", 1280, 900);
            var profile = store.Create("Example desktop application");
            profile.SolutionPath = "<workspace>/Example.slnx";
            profile.HostProject = "src/Example/Example.csproj";
            profile.PublishFolder = "<publish>/Example";
            profile.Signing = new() { Source = ReleaseSigningSource.ProfileFile };
            using (var certificate = SigningCertificates.CreatePfx(store.KeyFilePath(profile.Id), store.CertificateFilePath(profile.Id), "render-only")) profile.Signing.Thumbprint = certificate.Thumbprint;
            store.Save(profile);
            store.Create("Second application");
            var invalid = Path.Combine(store.Root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(invalid);
            File.WriteAllText(Path.Combine(invalid, "profile.json"), "invalid");
            // Each capture gets a fresh visual tree: reparenting a rendered tree can retain stale
            // drawing positions after bindings change, despite another measure/arrange pass.
            view = new ReleaseManager();
            view.SetResourceReference(Control.ForegroundProperty, "themeWindowForegroundBrush");
            view.Vm.Initialize(preferences, store, secrets);
            view.Vm.LoadProfiles(profile.Id);
            Save(view, theme + "-idle", 1280, 900);
            view = new ReleaseManager();
            view.SetResourceReference(Control.ForegroundProperty, "themeWindowForegroundBrush");
            view.Vm.Initialize(preferences, store, secrets);
            view.Vm.LoadProfiles(profile.Id);
            typeof(ReleaseManagerVm).GetProperty(nameof(ReleaseManagerVm.RunningOperation))!.SetValue(view.Vm, "Sync");
            Save(view, theme + "-busy", 1280, 900);
            typeof(ReleaseManagerVm).GetProperty(nameof(ReleaseManagerVm.RunningOperation))!.SetValue(view.Vm, "");
            var menu = ((Button)view.FindName("profileActions")).ContextMenu;
            menu.DataContext = view.Vm;
            menu.IsOpen = true;
            menu.Width = 300;
            menu.Measure(new Size(300, 430)); menu.Arrange(new Rect(0, 0, 300, 430)); menu.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var menuBitmap = new RenderTargetBitmap(300, 430, 96, 96, PixelFormats.Pbgra32);
            menuBitmap.Render(menu);
            var menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap));
            using (var file = File.Create(Path.Combine(output, theme + "-menu.png"))) menuEncoder.Save(file);
            menu.IsOpen = false; images++;
            foreach (var source in new[] { ReleaseSigningSource.Store, ReleaseSigningSource.ProfileFile }) {
               foreach (var mode in source == ReleaseSigningSource.Store ? new[] { ReleasePasswordStorage.Separate } : Enum.GetValues<ReleasePasswordStorage>()) {
                  var draft = profile.Clone(); draft.Signing.Source = source; draft.Signing.PasswordStorage = mode;
                  if (mode == ReleasePasswordStorage.Plaintext) draft.Signing.Password = "render-only";
                  var dialog = new ReleaseSettingsDialog(fake, store, draft, store.List().First(e => e.Profile?.Id == profile.Id).LastWriteUtc, [], secrets, "10.0.100", true, false);
                  Dialog(dialog, $"{theme}-settings-{source}-{mode}", source == ReleaseSigningSource.Store ? 700 : 850);
               }
            }
            Dialog(new ReleaseProfilesFolderDialog("Documents\\Em\\ReleaseManager\\Profiles\\Release"), theme + "-folder", 300);
            Dialog(new SigningKeyDestinationDialog("Create Signing Key", ReleaseSigningSource.ProfileFile), theme + "-destination", 350);
            Dialog(new PasswordInputDialog("Signing Key Password", "Password of the key file in profile 'Example'.", showRemember: true), theme + "-password", 320);
         }
         Console.WriteLine($"PASS rendered {images} screens: {output}");
      }
      finally {
         typeof(ReleaseSigningSecrets).GetMethod("ClearSessionForTesting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
         var validated = PublishPaths.Inside(Path.GetTempPath(), Path.GetFileName(temporary));
         if (Directory.Exists(validated)) { PublishPaths.ValidateTree(validated); Directory.Delete(validated, true); }
         Registry.CurrentUser.DeleteSubKeyTree(registry, false);
      }
   }
}
