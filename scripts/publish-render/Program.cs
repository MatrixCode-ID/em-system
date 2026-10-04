using Em.Ui.Core.Shared;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Publish;
using Em.Ui.Wpf.Navigations.Publish;

internal static class Program {
 [STAThread] static void Main(string[] args) {
  var application=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};var output=Path.GetFullPath(args.FirstOrDefault()??"../.artefacts/em-system/publish-render");Directory.CreateDirectory(output);
  var apply=typeof(EmApp).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;var images=0;
  void Save(FrameworkElement view,string name) {
   var renderWidth=view.Width+view.Margin.Left+view.Margin.Right;var renderHeight=view.Height+view.Margin.Top+view.Margin.Bottom;
   var host=new Border {Width=renderWidth,Height=renderHeight,Background=(Brush)application.Resources["themeWindowBackgroundBrush"],Child=view};host.Measure(new Size(renderWidth,renderHeight));host.Arrange(new Rect(0,0,renderWidth,renderHeight));host.UpdateLayout();application.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
   var bitmap=new RenderTargetBitmap((int)renderWidth,(int)renderHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);host.Child=null;images++;
  }
  foreach(var dark in new[]{false,true}) {
   apply.Invoke(null,[application.Resources,dark?(ThemeBase)new DarkTheme():new LightTheme()]);var theme=dark?"dark":"light";
   foreach(var kind in Enum.GetValues<PublishKind>()) {
    var view=new PublishView {Width=1200,Height=1000};view.Attach(null,kind);Save(view,theme+"-"+kind+"-idle");view.Width=900;Save(view,theme+"-"+kind+"-narrow");view.Width=1200;
    ((FrameworkElement)view.FindName("operations")).IsEnabled=false;((FrameworkElement)view.FindName("profilePanel")).IsEnabled=false;((TextBlock)view.FindName("message")).Text="Preparing…";Save(view,theme+"-"+kind+"-busy");
    ((FrameworkElement)view.FindName("operations")).IsEnabled=true;((FrameworkElement)view.FindName("profilePanel")).IsEnabled=true;((TextBlock)view.FindName("message")).Text="Credential perlu diisi untuk host ini.";Save(view,theme+"-"+kind+"-error");
    ((RadioButton)view.FindName("pageHistory")).IsChecked=true;Save(view,theme+"-"+kind+"-history");((RadioButton)view.FindName("pagePublish")).IsChecked=true;
    var p=PublishProfile.Create(kind);p.Workspace=Environment.CurrentDirectory;
    var dialog=new PublishProfileDialog(p,new(null,new()),new(),output);var content=(FrameworkElement)dialog.Content;var tabs=(TabControl)dialog.FindName("tabs");dialog.Content=null;content.Resources=dialog.Resources;content.Width=840;content.Height=700;
    for(var index=0;index<tabs.Items.Count;index++){tabs.SelectedIndex=index;Save(content,theme+"-"+kind+"-form-"+index);}dialog.Close();
   }
   var settings=new PublisherSettingsDialog(new());var settingsContent=(FrameworkElement)settings.Content;settings.Content=null;settingsContent.Resources=settings.Resources;settingsContent.Width=860;settingsContent.Height=380;Save(settingsContent,theme+"-settings");settings.Close();
  }
  Console.WriteLine($"PASS rendered {images} screens: {output}");
 }

}
