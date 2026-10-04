using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Controls;

class Program
{
 [STAThread] static void Main() {
  var app = new Application();
  var applyTheme = typeof(StorageSettingsCard).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;
  foreach (var dark in new[] { false, true }) {
   applyTheme.Invoke(null, [app.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
   foreach (var cdn in new[] { false, true }) {
    var view = new StorageSettingsCard { Width = 1080 };
    typeof(StorageSettingsCard).GetField("_cdn", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, cdn);
    var panel = (Expander)view.FindName("panel");
    var directory = (TextBox)view.FindName("directory");
    var limit = (TextBox)view.FindName("limit");
    var toggle = (CheckBox)view.FindName("enabled");
    ((StackPanel)view.FindName("limitPanel")).Visibility = cdn ? Visibility.Visible : Visibility.Collapsed;
    var saved = new StorageFeatureSettings { Enabled = false, Directory = "./data/next", MaxUploadMb = 100 };
    var detail = new StorageSettingsDetail(8, saved with { Enabled = true }, saved, "E:/fixture/active", "E:/fixture/next", true, true);
    typeof(StorageSettingsCard).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [detail]);
    panel.IsExpanded = true;
    var root = new Border { Width = 1080, Height = 540, Background = (Brush)app.Resources["themeWindowBackgroundBrush"], Child = view };
    Layout(root);
    byte[] Render(string state) {
     Layout(root);
     var bitmap = new RenderTargetBitmap(1080, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
     var pixels = new byte[1080 * 540 * 4]; bitmap.CopyPixels(pixels, 1080 * 4, 0);
     var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
     using var file = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"settings-{(cdn ? "cdn" : "registry")}-{(dark ? "dark" : "light")}-{state}.png")); encoder.Save(file);
     return pixels;
    }
    var idle = Render("idle");
    directory.IsEnabled = limit.IsEnabled = toggle.IsEnabled = false;
    foreach (var name in new[] { "refresh", "validate", "save" }) ((Button)view.FindName(name)).IsEnabled = false;
    var busy = Render("busy");
    var point = directory.TransformToAncestor(root).Transform(new Point(directory.ActualWidth - 20, directory.ActualHeight / 2));
    var offset = ((int)point.Y * 1080 + (int)point.X) * 4;
    Console.WriteLine($"sample idle={string.Join(',',idle.Skip(offset).Take(4))}, busy={string.Join(',',busy.Skip(offset).Take(4))}");
    // The shared field style dims a disabled input (Opacity 0.4 over the card), so allow that small shift; what must never happen is the system white.
    if (Enumerable.Range(0,3).Any(i => Math.Abs(idle[offset+i]-busy[offset+i]) > 14) || Enumerable.Range(0,3).All(i => busy[offset+i] >= 253)) throw new Exception("Disabled input background changed theme");
    if (dark && busy[offset] > 100) throw new Exception("White disabled input");
    Console.WriteLine($"PASS {(cdn ? "CDN" : "registry")} {(dark ? "dark" : "light")} enabled/disabled background BGRA={string.Join(',', busy.Skip(offset).Take(4))}");
    directory.IsEnabled = limit.IsEnabled = toggle.IsEnabled = true;
    var restored = Render("restored");
    if (!idle.Skip(offset).Take(4).SequenceEqual(restored.Skip(offset).Take(4))) throw new Exception("Fast busy transition did not restore background");
    Console.WriteLine("PASS fast busy transition restored input theme");
    root.Child = null;
   }
  }
 }
 static void Layout(FrameworkElement root) {
  root.Measure(new Size(root.Width, root.Height)); root.Arrange(new Rect(0, 0, root.Width, root.Height)); root.UpdateLayout();
  Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
 }
}
