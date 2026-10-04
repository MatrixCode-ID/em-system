using System.Windows;
using System.Windows.Controls;
using Em;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Api.Core.Models;
using Microsoft.Extensions.DependencyInjection;
namespace Em.Ui.Wpf.Navigations;

public partial class NuPakManager : UserControl, INavigationBody
{
   private EmApp? _app;
   private INuPakServices? _service;
   private readonly HashSet<string> _busy = [];
   private NuPakStatus? _status;
   private NuPakFeedInfo? _feed;
   private string? FeedId => _feed?.Id;
   private int _generation;
   private bool _settingFeeds;
   private int _packageSkip, _binSkip, _auditSkip;
   private string? _binPrefix;
   public NuPakManager() { InitializeComponent(); }
   public NuPakManager(EmApp app) : this() { Attach(app, app.ServiceProvider.GetRequiredService<INuPakServices>()); }
   public void Attach(EmApp app, INuPakServices service) { _app = app; _service = service; settingsCard.AttachNuGet(app); publisher.Attach(app,Em.Ui.Wpf.Publish.PublishKind.NuGet); ApplyPermissions(); }
   private bool CanManage => _app is not null && _app.AllClaims.Any(c => c.ModuleName == Defaults.AdministrativeToolsModuleName && c.Name == INuPakServices.SettingsClaim)
      && new ClaimCollection(Defaults.AdministrativeToolsModuleName, _app.AllClaims, _app.ActiveUser)[INuPakServices.SettingsClaim];
   private void ApplyPermissions() {
      enabled.IsEnabled = CanManage && _status is not null && !_busy.Contains("settings");
      anonymous.IsEnabled = feedEnabled.IsEnabled = CanManage && _feed is not null && !_busy.Overlaps(new[]{"settings","feed","mutation"});
      feedCreate.IsEnabled=CanManage&&!_busy.Contains("mutation");
      feedUpdate.IsEnabled=feedDelete.IsEnabled=CanManage&&_feed is not null&&!_busy.Overlaps(new[]{"mutation","feed"});
      foreach(System.Windows.Controls.TabItem tab in feedWork.Items) tab.IsEnabled=tab.Header?.ToString() is "Audit" or "Settings" or "Publish"||(_feed is not null&&!_busy.Contains("mutation"));
      packageWork.IsEnabled=binWork.IsEnabled=prefixWork.IsEnabled=_feed is not null&&!_busy.Contains("mutation");
      feeds.IsEnabled=!_busy.Contains("mutation");
      feedCard.Visibility=_feed is null?Visibility.Collapsed:Visibility.Visible;
      noFeeds.Visibility=_feed is null?Visibility.Visible:Visibility.Collapsed;
      feedConfig.IsEnabled=_feed is not null;
      feedMore.IsEnabled=feedUpdate.IsEnabled||feedDelete.IsEnabled||feedConfig.IsEnabled;
      feedRefresh.IsEnabled=!_busy.Contains("feed");
      feedListRefresh.IsEnabled=!_busy.Contains("feeds");
      purge.IsEnabled = emptyBin.IsEnabled = CanManage && _feed is not null && !_busy.Contains("mutation");
      foreach (var pair in new[] { ("prefixes", prefixRefresh), ("packages", packageRefresh), ("versions", versionRefresh), ("bin", binRefresh), ("access", accessRefresh), ("audit", auditRefresh) }) {
         pair.Item2.IsEnabled = !_busy.Contains(pair.Item1);
      }
      settingsRefresh.IsEnabled = !_busy.Contains("settings");
      prefixes.IsEnabled = !_busy.Overlaps(new[] { "prefixes", "packages", "access", "bin", "mutation" });
      packages.IsEnabled = !_busy.Overlaps(new[] { "packages", "versions", "mutation" });
      versions.IsEnabled = !_busy.Overlaps(new[] { "versions", "mutation" });
      bin.IsEnabled = !_busy.Overlaps(new[] { "bin", "mutation" });
   }
   private async Task Run(string key, Func<Task> operation) {
      if (_service is null || !_busy.Add(key)) return;
      ApplyPermissions();
      var generation=_generation;
      try { await operation(); }
      catch (Exception ex) { if(generation==_generation)message.Text = ex.Message; }
      finally {
         _busy.Remove(key); ApplyPermissions();
         if(generation!=_generation && key is not ("settings" or "feeds" or "mutation")) _=RefreshFeedContents();
      }
   }
   public async Task RefreshAllAsync() {
      await Task.WhenAll(RefreshSettings(),RefreshFeeds());
      await RefreshFeedContents();
   }
   private Task RefreshFeedContents() => Task.WhenAll(RefreshFeed(),RefreshPrefixes(),RefreshBin(),RefreshAudit());
   private Task RefreshSettings() => Run("settings", async () => {
      _status=await _service!.GetMeta_NuPakStatus();enabled.IsChecked=_status.Enabled;
      status.Text=$"Server {(_status.Enabled?"on":"off")} · {_status.Feeds:N0} feeds · {_status.EffectiveFeeds:N0} effective active · {_status.Packages:N0} packages";
   });
   private Task RefreshFeeds() => Run("feeds",async()=> {
      var rows=await _service!.GetMeta_NuPakFeeds();var id=(feeds.SelectedItem as NuPakFeedInfo)?.Id;
      _settingFeeds=true;
      try {feeds.ItemsSource=rows;feeds.SelectedItem=rows.FirstOrDefault(f=>f.Id==id);}
      finally {_settingFeeds=false;}
      if(id is not null&&!rows.Any(f=>f.Id==id)) ClearFeed();
      noFeeds.Text=rows.Length==0?"No feeds. Create a feed explicitly to start publishing packages.":"Select a feed to view its packages and settings.";
   });
   private void ClearFeed() {
      _generation++;_feed=null;endpoint.Text=storage.Text="";feedEnabled.IsChecked=anonymous.IsChecked=false;
      prefixes.ItemsSource=packages.ItemsSource=versions.ItemsSource=bin.ItemsSource=access.ItemsSource=audit.ItemsSource=null;
      _packageSkip=_binSkip=_auditSkip=0;_binPrefix=null;search.Text="";versionDetail.Text="";
      prefixName.Text=prefixDescription.Text="";
      auditPackage.Text=auditVersion.Text=auditActor.Text=auditAction.Text=auditResult.Text="";auditFrom.SelectedDate=auditTo.SelectedDate=null;
      ApplyPermissions();
   }
   private async void FeedSelected(object sender,SelectionChangedEventArgs e) {
      if(_settingFeeds)return;
      var selected=feeds.SelectedItem as NuPakFeedInfo;ClearFeed();
      if(selected is not null) {
         var generation=_generation;
         await Run("feed-selection-"+generation,async()=> {
            var detail=await _service!.GetMeta_NuPakFeed(selected.Id);
            if(generation!=_generation)return;
            _feed=detail;
         });
      }
      await RefreshFeedContents();ApplyPermissions();
   }
   private Task RefreshFeed() => Run("feed",async()=> {
      var id=FeedId;var generation=_generation;if(id is null)return;
      var f=await _service!.GetMeta_NuPakFeed(id);
      var size=await _service.GetMeta_NuPakFeedStorageSize(id);
      if(generation!=_generation||id!=FeedId)return;
      _feed=f;feedEnabled.IsChecked=f.Enabled;anonymous.IsChecked=f.AnonymousRead;
      endpoint.Text=(_app?.ActiveConnection?.Host??"").TrimEnd('/')+f.ServiceIndex;
      storage.Text=$"{(f.EffectiveEnabled?"Ready":"Off")} · {f.Packages:N0} packages · {f.Versions:N0} active versions · Total {NuPakDisplay.Size(size.TotalBytes)} · Recycle {NuPakDisplay.Size(size.RecycleBytes)}";
   });
   private void FeedMoreClick(object s,RoutedEventArgs e) {if(s is Button {ContextMenu: {} menu} button) {menu.PlacementTarget=button;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;}}
   private async void FeedListRefresh(object s,RoutedEventArgs e) => await RefreshFeeds();
   private async void FeedDetailsRefresh(object s,RoutedEventArgs e) => await RefreshFeed();
   private async void CreateFeed(object s,RoutedEventArgs e) {
      if(!CanManage)return;
      var dialog=new NuPakFeedDialog(null,"",null){Owner=Window.GetWindow(this)};
      if(dialog.ShowDialog()!=true)return;
      var slug=dialog.Slug;var name=dialog.FeedName;var description=dialog.Description;
      await Run("mutation",async()=>{await _service!.PostGetMeta_NuPakFeedCreate(slug,name,description);});
      await RefreshAllAsync();
   }
   private async void UpdateFeed(object s,RoutedEventArgs e) {
      if(!CanManage||_feed is not {} f)return;
      var dialog=new NuPakFeedDialog(f.Slug,f.Name,f.Description){Owner=Window.GetWindow(this)};
      if(dialog.ShowDialog()!=true)return;
      var name=dialog.FeedName;var description=dialog.Description;
      await Run("mutation",async()=>{await _service!.PostGetMeta_NuPakFeedUpdate(f.Id,name,description,f.Enabled,f.AnonymousRead);});await RefreshAllAsync();
   }
   private async void DeleteFeed(object s,RoutedEventArgs e) {
      if(!CanManage||_feed is not {} f||!Confirm($"Delete feed {f.Name} ({f.Slug})? It must have no packages or recycled versions. Its {f.Prefixes:N0} empty prefixes and {f.Grants:N0} robot grants will also be deleted; audit history is retained."))return;
      await Run("mutation",async()=>{await _service!.PostMeta_NuPakFeedDelete(f.Id);ClearFeed();});await RefreshAllAsync();
   }
   private async void FeedEnabledClick(object s,RoutedEventArgs e) {
      if(!CanManage||_feed is not {} f)return;var desired=feedEnabled.IsChecked==true;feedEnabled.IsChecked=f.Enabled;
      await Run("mutation",async()=>{await _service!.PostGetMeta_NuPakFeedUpdate(f.Id,f.Name,f.Description,desired,f.AnonymousRead);});await RefreshAllAsync();
   }
   private bool Confirm(string text) => MessageBox.Show(text, "NuGet Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
   private async void EnabledClick(object sender, RoutedEventArgs e) {
      if (!CanManage || _status is null) return;
      var desired = enabled.IsChecked == true; enabled.IsChecked = _status.Enabled;
      if (!desired && !Confirm("Switch off the NuGet server? Clients will receive 404 until it is enabled again.")) return;
      await Run("settings", async () => { _status = await _service!.PostGetMeta_NuPakSetEnabled(desired); }); await Task.WhenAll(RefreshSettings(),RefreshFeed());
   }
   private async void AnonymousClick(object sender,RoutedEventArgs e) {
      if(!CanManage||_feed is not {} f)return;var desired=anonymous.IsChecked==true;anonymous.IsChecked=f.AnonymousRead;
      await Run("mutation",async()=>{await _service!.PostGetMeta_NuPakFeedUpdate(f.Id,f.Name,f.Description,f.Enabled,desired);});await RefreshFeed();
   }
   private static void Copy(string text) { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.COMException) { } }
   private void CopyAddress(object s, RoutedEventArgs e) { if(FeedId is not null && endpoint.Text.Length>0)Copy(endpoint.Text); }
   private void CopyConfig(object s, RoutedEventArgs e) { if(FeedId is null||endpoint.Text.Length==0)return; Copy($"<configuration>\n  <packageSources><clear /><add key=\"Em\" value=\"{System.Security.SecurityElement.Escape(endpoint.Text)}\" allowInsecureConnections=\"true\" /></packageSources>\n  <packageSourceMapping><packageSource key=\"Em\"><package pattern=\"MatrixCode.*\" /></packageSource></packageSourceMapping>\n</configuration>"); }
   private async void SettingsRefresh(object s, RoutedEventArgs e) => await Task.WhenAll(RefreshSettings(),RefreshFeeds());
   public Task OnNavigatingIn(INavigation s, NavigatingEventArgs e) { ApplyPermissions(); return Task.CompletedTask; }
   public Task OnNavigatingAway(INavigation s, NavigatingEventArgs e) { if(!settingsCard.ConfirmLeave()) e.Cancel=true; return Task.CompletedTask; }
   public Task OnReloadRequested(INavigation s, NavigationEventArgs e) => RefreshAllAsync();
   public Task OnRelease(INavigation s) => Task.CompletedTask;
}
