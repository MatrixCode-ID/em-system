using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Dialogs;

class Program {
 [STAThread] static void Main() {
  var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  var apply = typeof(RobotDialog).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;
  var owners = Enumerable.Range(0,1500).Select(i => new RobotOwnerInfo { Id = $"fixture-{i}", Account = $"user-{i:0000}" }).Append(new RobotOwnerInfo { Id = "target", Account = "sample-target" }).ToArray();
  foreach(var dark in new[]{false,true}) {
   apply.Invoke(null,[app.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
   var dialog = new RobotDialog(RobotDialogMode.Create, owners:owners);
   var vm = dialog.Vm;
   var chosen = owners[1000]; vm.SelectedOwner = chosen;
   var combo = (ComboBox)dialog.FindName("ownerCombo"); combo.ApplyTemplate();
   var popup = (Popup)combo.Template.FindName("PART_Popup",combo);
   var search = (TextBox)combo.Template.FindName("ownerSearchBox",combo);
   var list = (ListBox)combo.Template.FindName("ownerResults",combo);
   var panel = (FrameworkElement)popup.Child;
   apply.Invoke(null,[panel.Resources, dark ? (ThemeBase)new DarkTheme() : new LightTheme()]);
   search.Text = "  DAHLI  "; Pump();
   Check("substring case-insensitive filter", vm.FilteredOwners.Cast<RobotOwnerInfo>().Select(o => o.Account).SequenceEqual(new[]{"No owner","sample-target"}));
   Check("query preserves owner", vm.OwnerUserId == chosen.Id && combo.SelectedItem == chosen);
   search.Text = "missing-account"; Pump();
   Check("no results retains No owner", vm.HasNoMatchingOwners && list.Items.Count == 1);
   Check("no results preserves owner", vm.SelectedOwner == chosen);
   search.Text = ""; Pump();
   Check("clear restores all accounts", list.Items.Count == 1502 && !vm.HasNoMatchingOwners);
   search.Text = "TARGET"; Pump();
   Color? background = null;
   foreach(var disabled in new[]{false,true,false}) {
    panel.IsEnabled = !disabled;
    panel.Measure(new Size(432,400)); panel.Arrange(new Rect(0,0,432,panel.DesiredSize.Height)); panel.UpdateLayout(); Pump();
    Check("search foreground follows theme", search.Foreground is SolidColorBrush fg && fg.Color == ((SolidColorBrush)app.Resources["themeWindowForegroundBrush"]).Color);
    var surface = Descendants(search).OfType<Border>().First(b=>b.Background is SolidColorBrush);
    var color = ((SolidColorBrush)surface.Background).Color;
    if(background is null) background=color;
    Check("search background stable", background==color);
    var bitmap = new RenderTargetBitmap(432,(int)Math.Ceiling(panel.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(panel);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var file=System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory,$"owner-{(dark ? "dark" : "light")}-{(disabled ? "disabled" : "enabled")}.png"));encoder.Save(file);
   }
   Console.WriteLine($"PASS {(dark ? "dark" : "light")} popup render, filtering and selection with 1501 accounts");
   dialog.Close();
  }
 }
 static void Check(string name,bool pass) { if(!pass) throw new Exception("FAIL "+name); Console.WriteLine("PASS "+name); }
 static void Pump()=>Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
 static IEnumerable<DependencyObject> Descendants(DependencyObject p) {
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++) { var c=VisualTreeHelper.GetChild(p,i);yield return c;foreach(var d in Descendants(c))yield return d; }
 }
}
