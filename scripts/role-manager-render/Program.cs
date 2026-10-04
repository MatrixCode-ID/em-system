using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Navigations;

class Program {
 [STAThread] static void Main() {
  var app = new Application();
  var applyTheme = typeof(RoleManager).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;
  applyTheme.Invoke(null, [app.Resources, new LightTheme()]);
  var view = new RoleManager(null!) { DataContext = new Fixture {
   HasSelectedRole = true, HasNoRoles = false, IsLoaded = true, IsBusy = false, InWaiting = false,
   SelectedRole = new RoleFixture { cRoleName = "Fixture Admin", cRoleDescription = "Role theme regression fixture", cRoleState = "Active" },
   StateCaption = "IN USE", IdentifierCaption = "fixture-role", StateDetailCaption = "In use",
   MemberDetailCaption = "1 account", ClaimDetailCaption = "3 claims granted", LastChangedCaption = "03 Oct 2026, 12:00",
   Commands = new Dictionary<string, object>()
  }};
  var root = new Border { Width = 1320, Height = 800, Child = view };
  foreach (var dark in new[] { false, true, false }) {
   applyTheme.Invoke(null, [app.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
   applyTheme.Invoke(null, [root.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
   root.Background = (Brush)app.Resources["themeWindowBackgroundBrush"];
   Layout(root);
   var tabs = Descendants(view).OfType<TabControl>().First();
   tabs.ApplyTemplate();
   tabs.SelectedIndex = 2;
   foreach (var busy in new[] { false, true, false }) {
    view.IsEnabled = !busy;
    Layout(root);
    var expected = ((SolidColorBrush)app.Resources["themeWindowForegroundBrush"]).Color;
    var texts = Descendants(view).OfType<TextBlock>().ToArray();
    foreach (var caption in new[] { "Permissions", "Members", "RECORDED FACTS", "IDENTIFIER", "fixture-role", "STATE", "MEMBERS", "CLAIMS GRANTED", "LAST CHANGED", "LAST CHANGED BY", "Not recorded", "TAKING THIS ROLE OUT OF USE" }) {
     var text = texts.FirstOrDefault(t => t.Text == caption ) ?? throw new Exception("Missing visible caption: " + caption);
     if (text.Foreground is not SolidColorBrush brush || brush.Color != expected) {
      DependencyObject? ancestor = text;
      while (ancestor is not null) {
       Console.WriteLine(ancestor.GetType().Name + " foreground=" + ancestor.GetValue(TextElement.ForegroundProperty));
       ancestor = VisualTreeHelper.GetParent(ancestor);
      }
      throw new Exception($"Wrong foreground {caption}: {text.Foreground}, expected {expected}");
     }
    }
    var bitmap = new RenderTargetBitmap(1320, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var file = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, $"role-{(dark ? "dark" : "light")}-{(busy ? "disabled" : "enabled")}.png")); encoder.Save(file);
    Console.WriteLine($"PASS role labels and unselected tabs {(dark ? "dark" : "light")} {(busy ? "disabled" : "enabled")}");
   }
  }
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
  for (int i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++) {
   var child=VisualTreeHelper.GetChild(parent,i); yield return child;
   foreach(var nested in Descendants(child)) yield return nested;
  }
 }
 static void Layout(FrameworkElement root) {
  root.Measure(new Size(root.Width,root.Height)); root.Arrange(new Rect(0,0,root.Width,root.Height)); root.UpdateLayout();
  Application.Current.Dispatcher.Invoke(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
 }
}

public class RoleFixture {
 public string cRoleName { get; set; } = "";
 public string cRoleDescription { get; set; } = "";
 public string cRoleState { get; set; } = "Active";
}
public class Fixture {
 public bool HasSelectedRole { get; set; }
 public bool HasNoRoles { get; set; }
 public bool HasNoRole { get; set; }
 public bool IsEditingDetails { get; set; }
 public bool IsLoaded { get; set; }
 public bool IsBusy { get; set; }
 public bool InWaiting { get; set; }
 public RoleFixture? SelectedRole { get; set; }
 public string StateCaption { get; set; } = "";
 public string IdentifierCaption { get; set; } = "";
 public string StateDetailCaption { get; set; } = "";
 public string MemberDetailCaption { get; set; } = "";
 public string ClaimDetailCaption { get; set; } = "";
 public string LastChangedCaption { get; set; } = "";
 public Dictionary<string, object> Commands { get; set; } = [];
}
