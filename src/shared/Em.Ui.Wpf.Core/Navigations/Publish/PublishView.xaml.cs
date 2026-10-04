using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Publish;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Navigations.Publish;

public partial class PublishView : UserControl {
 private static readonly PublishSecretStore Secrets=new();
 private EmApp? _app;
 private PublishKind _kind;
 private PublisherSettings _settings=new();
 private ProfileStore _store=null!;
 private PublishTargets _targets=null!;
 private Publisher _publisher=null!;
 private string? _publisherProfileId;
 private bool _reloadingProfiles;
 private CancellationTokenSource? _cancel;
 private readonly HashSet<string> _cards=[];
 private ProfileEntry? Entry=>profileList.SelectedItem as ProfileEntry;
 private PublishProfile? Profile=>Entry?.Profile;
 public PublishView() {InitializeComponent();Attach(null,PublishKind.NuGet);}
 public void Attach(EmApp? app,PublishKind kind) {
  _app=app;_kind=kind;_settings=new(app);_store=new(_settings.Profiles);_targets=new(app,Secrets);SetPublisher();
  prepareText.Text=kind==PublishKind.NuGet?"Prepare":"Build";combinedText.Text=kind==PublishKind.NuGet?"Prepare & Push":"Build & Push";
  addPackages.Visibility=kind==PublishKind.NuGet?Visibility.Visible:Visibility.Collapsed;ReloadProfiles();ReloadHistory();
 }
 private void SetPublisher() { _publisherProfileId=Profile?.Id;_publisher=new(_settings,_store,Secrets,_targets);_publisher.Output+=line=>Dispatcher.BeginInvoke(()=> {liveLog.AppendText(line+Environment.NewLine);liveLog.ScrollToEnd();}); }
 private void ReloadProfiles(string? id=null) {
  id??=Profile?.Id??_settings.LastProfile;_reloadingProfiles=true;
  try {profileList.ItemsSource=_store.List(_kind);profileList.SelectedItem=((IReadOnlyList<ProfileEntry>)profileList.ItemsSource).FirstOrDefault(x=>x.Profile?.Id==id);}
  finally {_reloadingProfiles=false;}
  ProfileSelected(profileList,new SelectionChangedEventArgs(Selector.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
 }
 private void ReloadHistory() {history.ItemsSource=PublishLog.History(_settings.Logs,allHistory.IsChecked==true?null:Profile?.Id);}
 private async Task Operation(Func<PublishProfile,CancellationToken,Task> action) {
  if(_cancel!=null||Profile==null||_cards.Count>0)return;var p=Profile.Clone();_cancel=new();profilePanel.IsEnabled=profileList.IsEnabled=operations.IsEnabled=notes.IsEnabled=artifacts.IsEnabled=false;cancel.IsEnabled=true;message.Text="Operation in progress…";
  try {await action(p,_cancel.Token);message.Text="Operation completed. Review item results and history.";}
  catch(OperationCanceledException) {message.Text="Cancelled. Check partial results before retrying.";}
  catch(Exception ex) {message.Text=Mask(ex.Message);}
  finally {_cancel.Dispose();_cancel=null;profilePanel.IsEnabled=profileList.IsEnabled=operations.IsEnabled=notes.IsEnabled=artifacts.IsEnabled=true;cancel.IsEnabled=false;BindArtifacts();ReloadHistory();}
 }
 private string Mask(string text) {var mask=new SecretMasker();if(Profile is {} p)foreach(var c in p.Credentials)mask.Add(Secrets.Get(c,c.ScopeHost));return mask.Mask(text);}
 private void BindArtifacts() {artifacts.ItemsSource=null;artifacts.ItemsSource=_publisher.Prepared?.Artifacts;}
 private async Task Card(string key,Button button,Func<PublishProfile,Task> action) {
  if(Profile==null||_cancel!=null||!_cards.Add(key))return;var p=Profile.Clone();button.IsEnabled=false;
  try {await action(p);}catch(Exception ex){message.Text=Mask(ex.Message);}finally {_cards.Remove(key);button.IsEnabled=true;}
 }
 private void ProfileSelected(object sender,SelectionChangedEventArgs e) {
  if(_reloadingProfiles)return;
  if(_publisherProfileId!=Profile?.Id)SetPublisher();BindArtifacts();if(Profile is not {} p) {sourceText.Text=Entry?.Error??"Select a profile.";targetText.Text="";return;}
  _settings.LastProfile=p.Id;sourceText.Text=p.Name+"\n"+p.Workspace;targetText.Text=p.Kind==PublishKind.NuGet?p.NuGet!.Target.Type+" · "+p.NuGet.Target.Feed+" "+p.NuGet.Target.ServiceIndex:p.Container!.Mode+" · "+p.Container.Target.Host+"/"+p.Container.Target.Repository+":"+p.Container.Target.VersionTag;
  toolsText.Text="Refresh Tools or Check.";lastVersion.Text="";message.Text="Profile loaded. Check before preparing.";ReloadHistory();
 }
 private void OpenEditor(PublishProfile draft,ProfileEntry? previous=null) {
  var dialog=new PublishProfileDialog(draft,_targets,Secrets,_settings.Logs) {Owner=Window.GetWindow(this)};
  if(dialog.ShowDialog()!=true)return;
  try {
   var result=dialog.Profile;
   if(dialog.SaveAs) {result.Id=Guid.NewGuid().ToString("N");previous=null;}
   Secrets.ConvertMode(result,result.SensitiveDataStorage);
   ProfileEntry saved;
   try {saved=_store.Save(result,previous);}
   catch(ProfileConflictException) {
    var choice=MessageBox.Show("The profile changed outside this application. Yes: explicitly overwrite. No: reload the current file. Cancel: keep the existing file.","Profile conflict",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
    if(choice==MessageBoxResult.Yes)saved=_store.Save(result,previous,true);else {if(choice==MessageBoxResult.No)ReloadProfiles(result.Id);return;}
   }
   ReloadProfiles(saved.Profile!.Id);
  }catch(Exception ex){message.Text=Mask(ex.Message);}
 }
 private void CreateClick(object s,RoutedEventArgs e) {var p=PublishProfile.Create(_kind);p.Workspace=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);OpenEditor(p);}
 private async void CreateSolution(object s,RoutedEventArgs e) {
  var picker=new OpenFileDialog {Filter="Solution XML (*.slnx)|*.slnx"};if(picker.ShowDialog()!=true)return;
  var p=PublishProfile.Create(_kind);p.Workspace=Path.GetDirectoryName(picker.FileName)!;p.Name=Path.GetFileNameWithoutExtension(picker.FileName);
  var reader=new ProjectReader(new PublishProcessRunner());
  try {var paths=await reader.Projects(picker.FileName);var info=new List<ProjectInformation>();foreach(var path in paths)info.Add(await reader.Read(path));
   if(_kind==PublishKind.NuGet)p.NuGet!.Sources.Add(new() {Path=Path.GetFileName(picker.FileName),SelectAllPackable=false,Projects=info.Where(x=>x.IsPackable).Select(x=>Path.GetRelativePath(p.Workspace,x.Path)).ToList()});
   else {var host=info.FirstOrDefault(x=>x.IsHost);if(host!=null){p.Container!.Mode=ContainerMode.Template;p.Container.Template.Project=Path.GetRelativePath(p.Workspace,host.Path);p.Container.Template.Framework=host.Frameworks.Split(';')[0];if(host.Dockerfile!=null)p.Container.Template.ExistingDockerfile=Path.GetRelativePath(p.Workspace,host.Dockerfile);}}
   OpenEditor(p);
  }catch(Exception ex){message.Text=ex.Message;}
 }
 private void EditClick(object s,RoutedEventArgs e) {if(Profile!=null)OpenEditor(Profile.Clone(),Entry);}
 private void ImportClick(object s,RoutedEventArgs e) {
  var picker=new OpenFileDialog {Filter="Publish profile (*.json)|*.json"};if(picker.ShowDialog()!=true)return;
  try {var incoming=_store.Read(picker.FileName);if(incoming.Profile?.Kind!=_kind)throw new InvalidDataException(incoming.Error??"Profile kind does not match this manager.");ProfileEntry result;
   try {result=_store.Import(picker.FileName);}catch(ProfileConflictException){if(!Confirm("Profile ID already exists. Import with a new ID?"))return;result=_store.Import(picker.FileName,true);}ReloadProfiles(result.Profile!.Id);
  }catch(Exception ex){message.Text=Mask(ex.Message);}
 }
 private void DuplicateClick(object s,RoutedEventArgs e) {if(Profile==null)return;try {var copy=_store.Duplicate(Profile);ReloadProfiles(copy.Profile!.Id);}catch(Exception ex){message.Text=ex.Message;}}
 private void DeleteClick(object s,RoutedEventArgs e) {if(Entry==null||!Confirm("Delete this profile file? Publish history remains available."))return;try {_store.Delete(Entry);ReloadProfiles();}catch(Exception ex){message.Text=ex.Message;}}
 private void ExportClick(object s,RoutedEventArgs e) {
  if(Profile==null)return;var dialog=new SaveFileDialog {Filter="Publish profile (*.json)|*.json",FileName=Profile.Name+".json"};if(dialog.ShowDialog()!=true)return;
  var sensitive=MessageBox.Show("Include sensitive data? Default: No. Yes includes only inline plaintext secrets; Separate secrets are never inserted.","Export profile",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes;
  try {_store.Export(Profile,dialog.FileName,sensitive);}catch(Exception ex){message.Text=ex.Message;}
 }
 private void PageChanged(object s,RoutedEventArgs e) {
  if(publishPage is null||historyPage is null||profilePanel is null||historyPanel is null)return;
  var history=pageHistory.IsChecked==true;
  publishPage.Visibility=profilePanel.Visibility=history?Visibility.Collapsed:Visibility.Visible;
  historyPage.Visibility=historyPanel.Visibility=history?Visibility.Visible:Visibility.Collapsed;
 }
 private void MoreClick(object s,RoutedEventArgs e) {if(s is Button {ContextMenu: {} menu} button) {menu.PlacementTarget=button;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;}}
 private void RefreshClick(object s,RoutedEventArgs e)=>ReloadProfiles();
 private async void RefreshSource(object s,RoutedEventArgs e)=>await Card("source",sourceRefresh,async p=> {
  var reader=new ProjectReader(new PublishProcessRunner());var rows=new List<string>();var sources=p.NuGet?.Sources.Select(x=>p.Resolve(x.Path)).ToArray()??(p.Container?.Mode==ContainerMode.Template?[p.Resolve(p.Container.Template.Project)]:Array.Empty<string>());
  foreach(var source in sources)foreach(var project in await reader.Projects(source)) {var info=await reader.Read(project);rows.Add($"{Path.GetFileName(project)} · {info.PackageId} {info.Version} · {info.Frameworks} · {info.Reason}");}
  if(Profile?.Id==p.Id)sourceText.Text=p.Workspace+"\n"+string.Join("\n",rows);
 });
 private async void RefreshTarget(object s,RoutedEventArgs e)=>await Card("target",targetRefresh,async p=> {
  var target=p.Kind==PublishKind.NuGet?await _targets.ResolveNuGet(p,CancellationToken.None):p.Container!.Mode==ContainerMode.Set?"Ordered Set":await _targets.ResolveContainer(p,CancellationToken.None);
  var latest=PublishLog.History(_settings.Logs).SelectMany(x=>x.Run.Artifacts).FirstOrDefault(x=>x.ProfileId==p.Id&&x.Result==PublishResult.Success)?.Version??"none";
  var remote="";
  if(p.Kind==PublishKind.Container&&p.Container!.Mode!=ContainerMode.Set) {try {remote=await new OciClient(_targets).LatestTag(target,await _targets.OciCredential(p,target.Split('/')[0],CancellationToken.None),CancellationToken.None);}catch(Exception){remote="not available";}}
  if(Profile?.Id==p.Id) {targetText.Text=target;lastVersion.Text="Last local version: "+latest+(remote.Length>0?" · Latest remote tag: "+remote:"");}
 });
 private async void RefreshTools(object s,RoutedEventArgs e)=>await Card("tools",toolsRefresh,async p=> {var rows=await new PublishProcessRunner().CheckTools(p,CancellationToken.None);if(Profile?.Id==p.Id)toolsText.Text=string.Join("\n",rows);});
 private async void CheckClick(object s,RoutedEventArgs e)=>await Operation(async(p,ct)=> {var rows=await _publisher.Check(p,ct);toolsText.Text=string.Join("\n",rows);});
 private async void PrepareClick(object s,RoutedEventArgs e)=>await Operation((p,ct)=>_publisher.Prepare(p,ct));
 private async void PushClick(object s,RoutedEventArgs e) {if(Profile==null||!Confirm(Summary(Profile)))return;var release=notes.Text;await Operation((p,ct)=>_publisher.Push(p,release,ct));}
 private async void CombinedClick(object s,RoutedEventArgs e) {if(Profile==null||!Confirm(Summary(Profile)))return;var release=notes.Text;await Operation(async(p,ct)=> {_publisher.ValidateReleaseNotes(p,release);await _publisher.Check(p,ct);await _publisher.Prepare(p,ct);await _publisher.Push(p,release,ct);});}
 private string Summary(PublishProfile p)=>$"Publish {p.Name}?\n{targetText.Text}\nVersion: {p.NuGet?.VersionOverride ?? p.Container?.Target.VersionTag}\n"+(p.Container?.Target.ExtraTags.Contains("latest")==true?"Warning: latest will be overwritten after the version tag succeeds.\n":"")+"Review the selected artifacts and target before continuing.";
 private async void VerifyClick(object s,RoutedEventArgs e)=>await Operation((p,ct)=>_publisher.Verify(p,ct));
 private void CancelClick(object s,RoutedEventArgs e)=>_cancel?.Cancel();
 private void AddPackagesClick(object s,RoutedEventArgs e) {var picker=new OpenFileDialog {Filter="NuGet packages (*.nupkg)|*.nupkg",Multiselect=true};if(picker.ShowDialog()==true)AddPackages(picker.FileNames);}
 private void AddPackages(IEnumerable<string> files) {if(Profile==null||_cancel!=null)return;try {_publisher.AddPackages(Profile,files);BindArtifacts();}catch(Exception ex){message.Text=ex.Message;}}
 private void PackagesDragOver(object s,DragEventArgs e) {e.Effects=_kind==PublishKind.NuGet&&_cancel==null&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
 private void PackagesDrop(object s,DragEventArgs e) {if(_kind==PublishKind.NuGet&&e.Data.GetData(DataFormats.FileDrop) is string[] files)AddPackages(files);}
 private void CopyTarget(object s,RoutedEventArgs e) {try {Clipboard.SetText(targetText.Text);}catch(System.Runtime.InteropServices.COMException ex){message.Text=ex.Message;}}
 private async void OpenManager(object s,RoutedEventArgs e) {if(_app!=null)await _app.NavigateTo(_kind==PublishKind.NuGet?"admin.nupak":"admin.container");}
 private static bool Confirm(string text)=>MessageBox.Show(text,"Publisher",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 private void OpenFolder(string folder) {try {Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){message.Text=ex.Message;}}
 private void OpenProfiles(object s,RoutedEventArgs e)=>OpenFolder(Path.Combine(_settings.Profiles,_kind.ToString()));
 private void OpenLogs(object s,RoutedEventArgs e)=>OpenFolder(_settings.Logs);
 private void SettingsClick(object s,RoutedEventArgs e) {var dialog=new PublisherSettingsDialog(_settings){Owner=Window.GetWindow(this)};if(dialog.ShowDialog()==true)Attach(_app,_kind);}
 private void HistoryRefresh(object s,RoutedEventArgs e)=>ReloadHistory();
 private void HistorySelected(object s,SelectionChangedEventArgs e) {historyDetail.Text=history.SelectedItem is HistoryEntry entry?ProfileJson.Write(entry.Run):"";}
 private void OpenLog(object s,RoutedEventArgs e) {if(history.SelectedItem is HistoryEntry entry) {try {Process.Start(new ProcessStartInfo(Path.Combine(entry.Directory,"output.log")){UseShellExecute=true});}catch(Exception ex){message.Text=ex.Message;}}}
 private void ExportLog(object s,RoutedEventArgs e) {if(history.SelectedItem is not HistoryEntry entry)return;var picker=new SaveFileDialog {Filter="Publish log (*.zip)|*.zip",FileName=entry.Run.Id+".zip"};if(picker.ShowDialog()!=true)return;try {if(File.Exists(picker.FileName))File.Delete(picker.FileName);using var zip=ZipFile.Open(picker.FileName,ZipArchiveMode.Create);foreach(var name in new[]{"result.json","output.log"}) {var file=Path.Combine(entry.Directory,name);if(File.Exists(file))zip.CreateEntryFromFile(file,name);}}catch(Exception ex){message.Text=ex.Message;}}
 private void DeleteRun(object s,RoutedEventArgs e) {if(history.SelectedItem is HistoryEntry entry&&Confirm("Delete this publish run and its stored artifacts permanently?")) {try {PublishLog.Delete(_settings.Logs,entry);ReloadHistory();}catch(Exception ex){message.Text=ex.Message;}}}
 private void DeleteRuns(object s,RoutedEventArgs e) {var id=(history.SelectedItem as HistoryEntry)?.Run.ProfileId??Profile?.Id;if(id==null||!Confirm("Delete ALL publish runs for this profile permanently?"))return;try {foreach(var entry in PublishLog.History(_settings.Logs,id))PublishLog.Delete(_settings.Logs,entry);ReloadHistory();}catch(Exception ex){message.Text=ex.Message;}}
}
