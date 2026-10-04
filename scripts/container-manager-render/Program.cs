using Em.Api.Core.Models;
using Em.Ui.Wpf.Navigations;
// Offline WPF render check of the Container Manager: toolbar, tabs, three panels, busy and disabled states.
// Writes PNGs to ..\.artefacts\em-system\container-manager-render (outside the repo).
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;

class Program {
 static string Output = "";
 [STAThread] static void Main() {
  Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", ".artefacts", "em-system", "container-manager-render"));
  Directory.CreateDirectory(Output);
  var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic, null, [Array.Empty<string>()], null)!;
  typeof(EmApp).GetProperty("ApplicationName")!.SetValue(app, "Em-Container-Render-Fixture");
  typeof(EmApp).GetProperty("Branding")!.SetValue(app, new BrandingInfo());
  typeof(EmApp).GetProperty("IsDebugMode")!.SetValue(app, true);
  typeof(EmApp).GetField("_allClaims", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, Array.Empty<Em.Shared.ClaimAction>());
  var registry = DispatchProxy.Create<ICtnServices, FakeService>();
  var fake = (FakeService)(object)registry;
  using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
  typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, services);
  var apply = typeof(EmApp).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;
  foreach (var dark in new[] { false, true }) {
   var theme = dark ? "dark" : "light";
   typeof(EmApp).GetProperty("ActiveUser")!.SetValue(app, Em.Api.Core.Models.User.Build(app, new vi_User { cUserIsAdmin = true, cUserAccount = "render-admin" }));
   apply.Invoke(null, [application.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
   var manager = new ContainerManager(app) { Width = 1200, Height = 800 };
   var root = new Border { Child = manager, Width = 1200, Height = 800, Background = (Brush)application.Resources["themeWindowBackgroundBrush"] };
   Pump(manager.Vm.ReloadAsync()); Layout(root); Save(root, theme + "-ready");
   var tabs = (TabControl)manager.Content;
   for (var i = 0; i < tabs.Items.Count; i++) { tabs.SelectedIndex = i; Layout(root); Save(root, $"{theme}-tab-{i}"); }
   tabs.SelectedIndex = 0;
   // select a root, then a folder, then a container: the same toolbar cell must switch Edit/Delete without moving
   manager.Vm.SelectedRoot = manager.Vm.Roots.First(); Pump(manager.Vm.ReloadAsync()); Layout(root); Save(root, theme + "-root");
   foreach (var node in Flatten(manager.Vm.TreeNodes)) { typeof(ContainerManagerVm).GetProperty("SelectedNode")!.SetValue(manager.Vm, node); Layout(root); Save(root, $"{theme}-node-{(node.IsFolder ? "folder" : "container")}"); }
   root.Width = 900; Layout(root); Save(root, theme + "-narrow"); root.Width = 1200;
   // busy: the whole screen disabled while a request is in flight must keep the themed surface
   manager.IsEnabled = false; Layout(root); Save(root, theme + "-disabled"); manager.IsEnabled = true;
   fake.Disabled = true; Pump(manager.Vm.ReloadAsync()); Layout(root); Save(root, theme + "-registry-disabled"); fake.Disabled = false;
   Console.WriteLine($"PASS {theme}: Container Manager ready/tabs/selection/narrow/disabled rendered to {Output}");
  }
  Console.WriteLine("Render done");
 }
 static IEnumerable<CtnTreeNode> Flatten(IEnumerable<CtnTreeNode> nodes) { foreach (var n in nodes) { yield return n; foreach (var c in Flatten(n.Children)) yield return c; } }
 static void Pump(Task task) { while (!task.IsCompleted) Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); task.GetAwaiter().GetResult(); }
 static void Layout(FrameworkElement root) { root.Measure(new Size(root.Width, root.Height)); root.Arrange(new Rect(0, 0, root.Width, root.Height)); root.UpdateLayout(); Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
 static void Save(FrameworkElement root, string name) {
  var bmp = new RenderTargetBitmap((int)root.Width, (int)root.Height, 96, 96, PixelFormats.Pbgra32); bmp.Render(root);
  var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
  using var file = File.Create(Path.Combine(Output, name + ".png")); encoder.Save(file);
 }
}
public class FakeService : DispatchProxy {
 public bool Disabled;
 protected override object? Invoke(MethodInfo? method, object?[]? args) {
  object? result = method!.Name switch {
   "GetMeta_CtnStatus" => new StorageFeatureStatus(false, !Disabled, !Disabled, false),
   "GetMeta_CtnRoots" => new[] { new CtnRootInfo { Id = "r1", Name = "server", Description = "Server images", IsActive = true, FolderCount = 1, ImageCount = 2, CreatedAt = DateTime.UtcNow.AddDays(-30) }, new CtnRootInfo { Id = "r2", Name = "tools", IsActive = false, ImageCount = 0, CreatedAt = DateTime.UtcNow.AddDays(-3) } },
   "GetMeta_CtnStorageSize" => new CtnStorageInfo { BlobBytes = 120_000_000, ManifestBytes = 4_000 },
   "GetMeta_CtnTree" => new CtnTree {
    Folders = [new CtnFolderInfo { Id = "f1", RootId = "r1", Name = "backend" }],
    Images = [new CtnImageInfo { Id = "i1", RootId = "r1", RootName = "server", FolderId = "f1", Name = "api", FullName = "server/api", Description = "Em API host", IsActive = true, TagCount = 2, ManifestCount = 1, CreatedAt = DateTime.UtcNow.AddDays(-10) }, new CtnImageInfo { Id = "i2", RootId = "r1", RootName = "server", Name = "worker", FullName = "server/worker", IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-9) }]
   },
   "GetMeta_CtnImageManifests" => new[] { new CtnManifestInfo { Id = "m1", Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", MediaType = "application/vnd.oci.image.manifest.v1+json", Size = 1200, PushedAt = DateTime.UtcNow.AddDays(-1), PushedBy = "build-robot", Tags = ["1.0.0", "latest"] } },
   _ => null
  };
  var type = method.ReturnType;
  if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Task<>)) return null;
  var inner = type.GetGenericArguments()[0];
  if (result is null) { try { result = Activator.CreateInstance(inner); } catch { result = null; } }
  return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [result]);
 }
}
