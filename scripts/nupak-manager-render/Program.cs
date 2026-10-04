using Em.Api.Core.Models;
using Em.Ui.Wpf.Navigations;
// Offline WPF render and refresh verification. Run with --artifacts-path if the host is open.
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;

using Microsoft.Extensions.DependencyInjection;

class Program {
 [STAThread] static void Main() {
  var application = new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
  var app = (EmApp)Activator.CreateInstance(typeof(EmApp), BindingFlags.Instance | BindingFlags.NonPublic, null, [Array.Empty<string>()], null)!;
  typeof(EmApp).GetProperty("ApplicationName")!.SetValue(app,"Em-Publish-Render-Fixture");
  typeof(EmApp).GetProperty("Branding")!.SetValue(app, new BrandingInfo());
  typeof(EmApp).GetProperty("IsDebugMode")!.SetValue(app, true);
  typeof(EmApp).GetField("_allClaims",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app, new[]{Em.Shared.ClaimAction.Create<Em.Api.Core.NuPakService>(INuPakServices.SettingsClaim)});
  var navs = (ICollection<Navigation>)app.Navigations;
  navs.Add(new Navigation { Name="admin.container", BodyType=BodyType.Of<NuPakManager>(),Kind=NavigationKind.Manager,OrderIndex=0,RequireParameter=false });
  navs.Add(new Navigation { Name="admin.cdn", BodyType=BodyType.Of<NuPakManager>(),Kind=NavigationKind.Manager,OrderIndex=0,RequireParameter=false });
  navs.Add(new Navigation { Name="admin.nupak", BodyType=BodyType.Of<NuPakManager>(),Kind=NavigationKind.Manager,OrderIndex=0,RequireParameter=false });
  var nuget = DispatchProxy.Create<INuPakServices, FakeService>();
  var np = (FakeService)(object)nuget;
  var registry = DispatchProxy.Create<ICtnServices, FakeService>();
  var cdn = DispatchProxy.Create<ICdnServices, FakeService>();
  var rp=(FakeService)(object)registry; var cp=(FakeService)(object)cdn;
  using var services = new ServiceCollection().AddSingleton(registry).AddSingleton(cdn).AddSingleton(nuget).BuildServiceProvider();
  typeof(EmApp).GetField("_serviceProvider", BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,services);
  var apply=typeof(EmApp).Assembly.GetType("Em.Ui.Wpf.Shared.ThemeResources")!.GetMethod("Apply")!;
  foreach(var dark in new[]{false,true}) {
   typeof(EmApp).GetProperty("ActiveUser")!.SetValue(app, Em.Api.Core.Models.User.Build(app,new vi_User {cUserIsAdmin=true,cUserAccount="render-admin"}));
   apply.Invoke(null,[application.Resources,dark?(ThemeBase)new DarkTheme():new LightTheme()]);
   var manager=new NuPakManager {Width=1200,Height=900};manager.Attach(app,nuget);Pump(manager.RefreshAllAsync());
   ((ComboBox)manager.FindName("feeds")).SelectedIndex=0;Pump(manager.RefreshAllAsync());
   ((ListBox)manager.FindName("prefixes")).SelectedIndex=0;
   ((ListBox)manager.FindName("packages")).SelectedIndex=0;
   ((ListBox)manager.FindName("versions")).SelectedIndex=0;
   var packagesMethod=typeof(NuPakManager).GetMethod("RefreshPackages",BindingFlags.Instance|BindingFlags.NonPublic)!;
   np.PackagesPending=new TaskCompletionSource<NuPakPackageInfo[]>();
   var packageLoad=(Task)packagesMethod.Invoke(manager,null)!;
   var packageCalls=np.Calls; ((Task)packagesMethod.Invoke(manager,null)!).GetAwaiter().GetResult();
   if(np.Calls!=packageCalls || ((ListBox)manager.FindName("packages")).IsEnabled)throw new Exception("Manager duplicate/busy guard failed");
   np.PackagesPending.SetResult([new("pkg","MatrixCode.Library",2,10000000,"1.2.0")]);np.PackagesPending=null;Pump(packageLoad);
   var managerRoot=new Border{Child=manager,Width=1200,Height=900,Background=(Brush)application.Resources["themeWindowBackgroundBrush"]};Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-ready.png");
   np.NuPending=new TaskCompletionSource<NuPakStatus>();var managerLoad=manager.RefreshAllAsync();Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-busy.png");
   np.NuPending.SetResult(new(false,2,0,2,3,4,1));np.NuPending=null;Pump(managerLoad);Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-off.png");
   var tab=(TabControl)manager.FindName("feedWork");
   for(var selected=0;selected<tab.Items.Count;selected++) {tab.SelectedIndex=selected;Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-tab-{selected}.png");}
   tab.SelectedIndex=2;
   var picker=(ComboBox)manager.FindName("feeds");
   picker.SelectedIndex=-1;np.FeedPending=new TaskCompletionSource<NuPakFeedInfo>();picker.SelectedIndex=0;
   Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-feed-loading.png");
   if(((Border)manager.FindName("feedCard")).Visibility!=Visibility.Collapsed)throw new Exception("Feed detail displayed before metadata");
   picker.SelectedIndex=1;Pump(manager.RefreshAllAsync());
   np.FeedPending.SetResult(new("a","alpha","Alpha",null,true,false,true,1,1,3,4,1));np.FeedPending=null;
   Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);Layout(managerRoot);
   if(!((TextBlock)manager.FindName("endpoint")).Text.Contains("/beta/"))throw new Exception("Stale A metadata replaced B selection");
   Save(managerRoot,$"{(dark?"dark":"light")}-manager-feed-switched.png");
   np.ZeroFeeds=true;Pump(manager.RefreshAllAsync());Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-zero-feeds.png");
   if(picker.Items.Count!=0||((TextBlock)manager.FindName("endpoint")).Text!=""||((ListBox)manager.FindName("versions")).Items.Count!=0||((Border)manager.FindName("feedCard")).Visibility!=Visibility.Collapsed)throw new Exception("Zero feed selection/detail guard failed");
   if(!((CheckBox)manager.FindName("enabled")).IsEnabled)throw new Exception("Zero feed lost server toggle");
   np.ZeroFeeds=false;Pump(manager.RefreshAllAsync());picker.SelectedIndex=0;Pump(manager.RefreshAllAsync());
   typeof(EmApp).GetProperty("ActiveUser")!.SetValue(app,null);Pump(manager.RefreshAllAsync());Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-no-rights.png");
   np.Fail=true;Pump(manager.RefreshAllAsync());Layout(managerRoot);Save(managerRoot,$"{(dark?"dark":"light")}-manager-error.png");np.Fail=false;
   Console.WriteLine($"PASS {(dark?"dark":"light")}: NuGet Manager enabled/disabled, packages, permissions, independent loading, failure/retry");
  }
 }
 static void Pump(Task task) {while(!task.IsCompleted) Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);task.GetAwaiter().GetResult();}
 static void Layout(FrameworkElement root){root.Measure(new Size(root.Width,root.Height));root.Arrange(new Rect(0,0,root.Width,root.Height));root.UpdateLayout();Application.Current.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
 static IEnumerable<T> Descendants<T>(DependencyObject obj) where T:DependencyObject { if(obj is T found)yield return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(obj);i++)foreach(var child in Descendants<T>(VisualTreeHelper.GetChild(obj,i)))yield return child; }
 static void Save(FrameworkElement root,string name){var bmp=new RenderTargetBitmap((int)root.Width,(int)root.Height,96,96,PixelFormats.Pbgra32);bmp.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var file=System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory,name));encoder.Save(file);}
}
public class FakeService:DispatchProxy {
 public TaskCompletionSource<NuPakPackageInfo[]>? PackagesPending; public bool NuEnabled=true; public TaskCompletionSource<NuPakStatus>? NuPending;
 public bool ZeroFeeds; public TaskCompletionSource<NuPakFeedInfo>? FeedPending;
 public int Calls; public bool Fail; public TaskCompletionSource<CtnRootInfo[]>? Pending;
 public TaskCompletionSource<CdnFolderContent>? CdnPending;
 protected override object? Invoke(MethodInfo? method,object?[]? args) {
  Calls++;if(Fail)throw new InvalidOperationException("Synthetic unavailable");
  return method!.Name switch {
   "GetMeta_NuPakStorageStatus" => Task.FromResult(new StorageFeatureStatus(false,true,true,false)),
   "PostGetMeta_NuPakSetEnabled" => Task.FromResult(new NuPakStatus(NuEnabled=(bool)args![0]!,2,2,2,3,4,1)),
   "GetMeta_NuPakStatus" => NuPending?.Task ?? Task.FromResult(ZeroFeeds?new NuPakStatus(NuEnabled,0,0,0,0,0,0):new NuPakStatus(NuEnabled,2,2,2,3,4,1)),
   "GetMeta_NuPakFeedStorageSize" => Task.FromResult(new NuPakStorageInfo(10000000,2000000,3,5)),
   "GetMeta_NuPakFeeds" => Task.FromResult(ZeroFeeds?Array.Empty<NuPakFeedInfo>():new[]{new NuPakFeedInfo("a","alpha","Alpha",null,true,false,true,1,1,3,4,1),new NuPakFeedInfo("b","beta","Beta",null,false,true,false,0,0,0,0,0)}),
   "GetMeta_NuPakFeed" => (string)args![0]! == "a" && FeedPending is not null ? FeedPending.Task : Task.FromResult(new NuPakFeedInfo((string)args![0]!, (string)args[0]! == "a" ? "alpha" : "beta",(string)args[0]! == "a" ? "Alpha" : "Beta",null,true,false,true,1,1,3,4,1)),
   "GetMeta_NuPakServerAudit" => Task.FromResult(Array.Empty<ta_NuPakAudit>()),
   "GetMeta_NuPakStorageSize" => Task.FromResult(new NuPakStorageInfo(10000000,2000000,3,5)),
   "GetMeta_NuPakPrefixes" => Task.FromResult(new[]{new NuPakPrefixInfo("p","MatrixCode.","Internal packages",true,3,12000000)}),
   "GetMeta_NuPakRecycleBin" => Task.FromResult(new[]{new NuPakVersionInfo("bin","pkg","MatrixCode.Library","0.9.0","0.9.0",false,-2,2000000,"SHA512-test","build-robot",DateTime.UtcNow.AddDays(-3),DateTime.UtcNow)}),
   "GetMeta_NuPakAudit" => Task.FromResult(new[]{new ta_NuPakAudit{cNuPakAuditAt=DateTime.UtcNow,cNuPakAuditAction="Push",cNuPakAuditActorName="build-robot",cNuPakAuditResult="Success",cNuPakAuditPackage="MatrixCode.Library",cNuPakAuditVersion="1.2.0"}}),
   "GetMeta_NuPakPackages" => PackagesPending?.Task ?? Task.FromResult(new[]{new NuPakPackageInfo("pkg","MatrixCode.Library",2,10000000,"1.2.0")}),
   "GetMeta_NuPakVersions" => Task.FromResult(new[]{new NuPakVersionInfo("v","pkg","MatrixCode.Library","1.2.0","1.2.0",false,1,5000000,"SHA512-test","build-robot",DateTime.UtcNow,null)}),
   "GetMeta_NuPakPrefixAccess" => Task.FromResult(new[]{new NuPakAccessInfo("robot","build-robot","W")}),
   "GetMeta_CtnRoots" => Pending?.Task ?? Task.FromResult(new[]{new CtnRootInfo{ImageCount=3}}),
   "GetMeta_CtnStorageSize" => Task.FromResult(new CtnStorageInfo{BlobBytes=10_000_000,ManifestBytes=1000}),
   "GetMeta_CdnFolder" => CdnPending?.Task ?? Task.FromResult(new CdnFolderContent{MaxFileSize=200_000_000}),
   "GetMeta_CdnStorageSize" => Task.FromResult(new CdnStorageInfo{TotalBytes=15_000_000,FileCount=12}),
   _ => throw new NotSupportedException(method.Name)
  };
 }
}
