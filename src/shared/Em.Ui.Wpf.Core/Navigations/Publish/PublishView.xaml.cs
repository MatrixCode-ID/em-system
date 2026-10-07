using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Em.Ui.Wpf.Controls;
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
 private readonly DispatcherTimer _versionTimer=new() {Interval=TimeSpan.FromMilliseconds(400)};
 private bool _syncingVersion,_versionDirty,_resolvingVersion;
 private ProfileEntry? Entry=>profileList.SelectedItem as ProfileEntry;
 private PublishProfile? Profile=>Entry?.Profile;
 /// <summary>Creates a new instance of <see cref="PublishView"/>.</summary>
 public PublishView() {
  InitializeComponent();versionChannel.ItemsSource=ContainerVersion.Channels;_versionTimer.Tick+=VersionTick;
  foreach(var box in new[]{versionMajor,versionMinor,versionPatch})DependencyPropertyDescriptor.FromProperty(NumericBox.ValueProperty,typeof(NumericBox)).AddValueChanged(box,VersionEdited);
  Attach(null,PublishKind.NuGet);
 }
 /// <summary>Attaches the publish view to the application for a kind of publish (NuGet or container).</summary>
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
 // ===== version strip: A.B.C and the channel, shown for a Container profile that pushes one image =====
 private static bool UsesVersionStrip(PublishProfile p)=>p.Container is {Mode:not (ContainerMode.Set or ContainerMode.Compose)};
 private ContainerVersion VersionInput()=>new(versionMajor.Value,versionMinor.Value,versionPatch.Value,versionChannel.SelectedItem as string??ContainerVersion.First.Channel);
 private static bool IsManual(PublishProfile p)=>p.Container?.Target.Tagging==TaggingMode.Manual;
 // Standard tagging: spin edits + channel. Manual tagging (chosen in the profile): one box for the whole tag.
 private void ShowVersionMode(bool manual) {
  var standard=manual?Visibility.Collapsed:Visibility.Visible;
  versionNumbers.Visibility=standard;versionChannel.Visibility=standard;manualTag.Visibility=manual?Visibility.Visible:Visibility.Collapsed;
 }
 private static string FloatingText(string tag)=>ContainerVersion.FloatingTagsOf(tag) is {Length:>0} tags?"+ "+string.Join(", ",tags):"";
 private void LoadVersion(PublishProfile? p) {
  _versionTimer.Stop();_versionDirty=false;
  var show=p!=null&&UsesVersionStrip(p);versionGroup.Visibility=show?Visibility.Visible:Visibility.Collapsed;if(!show)return;
  var tag=p!.Container!.Target.VersionTag;var manual=IsManual(p);ContainerVersion.TryParse(tag,out var v);
  _syncingVersion=true;
  try {versionMajor.Value=v.Major;versionMinor.Value=v.Minor;versionPatch.Value=v.Patch;versionChannel.SelectedItem=v.Channel;manualTag.Text=manual?tag:"";}
  finally {_syncingVersion=false;}
  ShowVersionMode(manual);floatingText.Text=manual?"":FloatingText(tag);
 }
 private void VersionEdited(object? s,EventArgs e) {if(_syncingVersion)return;_versionDirty=true;_versionTimer.Stop();_versionTimer.Start();}
 private void VersionChannelChanged(object s,SelectionChangedEventArgs e)=>VersionEdited(s,e);
 private void ManualTagChanged(object s,TextChangedEventArgs e)=>VersionEdited(s,e);
 private async void VersionTick(object? s,EventArgs e) {
  _versionTimer.Stop();if(_cancel!=null)return;
  if(_resolvingVersion) {_versionTimer.Start();return;}
  await ResolveVersion(false,false);
 }
 private async Task<bool> ResolveVersion(bool strict,bool announce) {
  if(_cancel!=null||_resolvingVersion||Profile==null)return false;
  _resolvingVersion=true;
  try {
   if(announce&&versionGroup.Visibility==Visibility.Visible)message.Text="Reading the tags on the registry…";
   await ApplyVersion(strict);return true;
  }catch(Exception ex){message.Text=Mask(ex.Message);return false;}
  finally {_resolvingVersion=false;}
 }
 // Existing tags of the image: what this profile pushed before (local history) and what the registry lists now.
 private async Task<(HashSet<string> Local,HashSet<string> Remote,bool RemoteKnown,string? Reference)> TakenTags(PublishProfile probe) {
  var local=PublishLog.History(_settings.Logs).SelectMany(h=>h.Run.Artifacts).Where(a=>a.ProfileId==probe.Id&&a.Result==PublishResult.Success).Select(a=>a.Version).ToHashSet();
  var remote=new HashSet<string>();string? reference=null;
  try {
   reference=await _targets.ResolveContainer(probe,CancellationToken.None);
   foreach(var tag in await new OciClient(_targets).Tags(reference,await _targets.OciCredential(probe,reference.Split('/')[0],CancellationToken.None),CancellationToken.None))remote.Add(tag);
   return (local,remote,true,reference);
  }
  catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.NotFound) {return (local,remote,true,reference);}
  catch(Exception) {return (local,remote,false,reference);}
 }
 // Works out the tag the strip stands for and stores it in the profile and its file, so the choice survives
 // reselecting. strict: the tag is about to be pushed, so an unreadable registry or a taken release tag is an error.
 private async Task ApplyVersion(bool strict) {
  if(Entry is not {Profile: {} stored} entry||!UsesVersionStrip(stored))return;
  var current=stored.Container!.Target.VersionTag;
  if(IsManual(stored)) {
   var manual=manualTag.Text.Trim();
   ContainerVersion.ValidateManual(manual); // ResolveVersion shows the message and stops Build/Push
   if(current!=manual)SaveVersionTag(entry,stored,manual);
   floatingText.Text="";
   try {var reference=await _targets.ResolveContainer(stored,CancellationToken.None);if(Entry==entry)targetText.Text=reference;}catch(Exception){}
   return;
  }
  // A standard profile still holding a free tag (e.g. from before Tagging existed) is not silently renamed.
  if(!_versionDirty&&current.Length>0&&!ContainerVersion.TryParse(current,out _))
   throw new InvalidDataException($"The profile's tag '{current}' is not a standard version. Set Tagging to Manual in the profile to keep it, or change the version here.");
  var input=VersionInput();var probe=stored.Clone();probe.Container!.Target.VersionTag=input.Tag(1);
  var taken=await TakenTags(probe);
  if(Entry!=entry)return;
  var tag=input.Tag(input.NextNumber(taken.Local.Union(taken.Remote)));
  var exists=input.IsRelease&&taken.RemoteKnown&&taken.Remote.Contains(tag);
  if(strict&&!taken.RemoteKnown)throw new InvalidDataException("The tags on the registry could not be read, so the next build number cannot be chosen safely. Check the target and the credential, then try again.");
  if(strict&&exists)throw new InvalidDataException($"Version tag {tag} already exists on the registry. Version tags are never overwritten; raise the version number.");
  if(current!=tag)SaveVersionTag(entry,stored,tag);
  floatingText.Text=FloatingText(tag);
  if(taken.Reference!=null)targetText.Text=taken.Reference[..taken.Reference.LastIndexOf(':')]+":"+tag;
  if(exists)message.Text=$"Version tag {tag} already exists on the registry; raise the version number before pushing.";
 }
 // The strip owns the tag: store it in the profile and its file so the choice survives reselecting.
 private void SaveVersionTag(ProfileEntry entry,PublishProfile stored,string tag) {
  stored.Container!.Target.VersionTag=tag;
  try {var saved=_store.Save(stored,entry);ReplaceEntry(entry,saved);}
  catch(Exception ex) {message.Text="Version tag not saved to the profile file: "+Mask(ex.Message);}
 }
 private void ReplaceEntry(ProfileEntry old,ProfileEntry saved) {
  _reloadingProfiles=true;
  try {profileList.ItemsSource=((IReadOnlyList<ProfileEntry>)profileList.ItemsSource).Select(x=>x==old?saved:x).ToArray();profileList.SelectedItem=saved;}
  finally {_reloadingProfiles=false;}
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
  if(_publisherProfileId!=Profile?.Id)SetPublisher();BindArtifacts();LoadVersion(Profile);if(Profile is not {} p) {sourceText.Text=Entry?.Error??"Select a profile.";targetText.Text="";return;}
  _settings.LastProfile=p.Id;sourceText.Text=p.Name+"\n"+p.Workspace;targetText.Text=p.Kind==PublishKind.NuGet?p.NuGet!.Target.Type+" · "+p.NuGet.Target.Feed+" "+p.NuGet.Target.ServiceIndex:p.Container!.Mode+" · "+p.Container.Target.Host+"/"+p.Container.Target.Repository+":"+p.Container.Target.VersionTag;
  lastVersion.Text="";message.Text="Profile loaded. Check before preparing.";ReloadHistory();
 }
 private void OpenEditor(PublishProfile draft,ProfileEntry? previous=null) {
  var dialog=new PublishProfileDialog(draft,_targets,Secrets,_settings.Logs,isNew:previous==null) {Owner=Window.GetWindow(this)};
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
  var picker=new OpenFileDialog {Filter="Publish profile (*.json;*.ctnconfig;*.nugetconfig)|*.json;*.ctnconfig;*.nugetconfig|All files (*.*)|*.*"};if(picker.ShowDialog()!=true)return;
  string? passphrase=null;
  if(ProfileBundle.IsBundleFile(picker.FileName)) {var ask=new PassphraseDialog(Path.GetFileName(picker.FileName)) {Owner=Window.GetWindow(this)};if(ask.ShowDialog()!=true)return;passphrase=ask.Passphrase;}
  try {var incoming=_store.Read(picker.FileName,passphrase);if(incoming.Profile?.Kind!=_kind)throw new InvalidDataException(incoming.Error??"Profile kind does not match this manager.");ProfileEntry result;
   try {result=_store.Import(picker.FileName,false,passphrase,Secrets);}catch(ProfileConflictException){if(!Confirm("Profile ID already exists. Import with a new ID?"))return;result=_store.Import(picker.FileName,true,passphrase,Secrets);}ReloadProfiles(result.Profile!.Id);
   message.Text=incoming.Profile.Credentials.Any(c=>!string.IsNullOrEmpty(c.Secret))?"Profile imported with its secrets.":"Profile imported.";
  }catch(Exception ex){message.Text=Mask(ex.Message);}
 }
 private void DuplicateClick(object s,RoutedEventArgs e) {if(Profile==null)return;try {var copy=_store.Duplicate(Profile);ReloadProfiles(copy.Profile!.Id);}catch(Exception ex){message.Text=ex.Message;}}
 private void DeleteClick(object s,RoutedEventArgs e) {if(Entry==null||!Confirm("Delete this profile file? Publish history remains available."))return;try {_store.Delete(Entry);ReloadProfiles();}catch(Exception ex){message.Text=ex.Message;}}
 private void ExportClick(object s,RoutedEventArgs e) {
  if(Profile==null)return;
  var options=new ExportProfileDialog(Profile.Name) {Owner=Window.GetWindow(this)};if(options.ShowDialog()!=true)return;
  var encrypted=options.Secrets==ExportSecrets.Encrypted;
  var dialog=new SaveFileDialog {Filter=encrypted?(_kind==PublishKind.Container?"Container Manager profile (*.ctnconfig)|*.ctnconfig":"NuGet Manager profile (*.nugetconfig)|*.nugetconfig"):"Publish profile (*.json)|*.json",FileName=Profile.Name+(encrypted?ProfileBundle.Extension(_kind):".json")};
  if(dialog.ShowDialog()!=true)return;
  try {
   var missing=_store.Export(Profile,dialog.FileName,options.Secrets,Secrets,options.Passphrase);
   message.Text=options.Secrets==ExportSecrets.None?"Profile exported without sensitive data.":missing>0
    ?$"Profile exported. {missing} credential(s) have no stored secret (enter or remember it first) and were exported empty."
    :encrypted?"Profile exported, encrypted. Keep the passphrase: it cannot be recovered.":"Profile exported as plain text. Treat the file as a secret.";
  }catch(Exception ex){message.Text=Mask(ex.Message);}
 }
 private void PageChanged(object s,RoutedEventArgs e) {
  if(publishPage is null||historyPage is null||profilePanel is null||historyPanel is null)return;
  var history=pageHistory.IsChecked==true;
  publishPage.Visibility=profilePanel.Visibility=history?Visibility.Collapsed:Visibility.Visible;
  historyPage.Visibility=historyPanel.Visibility=history?Visibility.Visible:Visibility.Collapsed;
 }
 private void MoreClick(object s,RoutedEventArgs e) {if(s is Button {ContextMenu: {} menu} button) {foreach(var item in menu.Items.OfType<FrameworkElement>().Where(i=>Equals(i.Tag,"docker")))item.Visibility=_kind==PublishKind.Container?Visibility.Visible:Visibility.Collapsed;menu.PlacementTarget=button;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;}}
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
 private void ToolsClick(object s,RoutedEventArgs e) {
  if(Profile==null) {message.Text="Select a profile to check its tools.";return;}
  new ToolsDialog(Profile.Clone()) {Owner=Window.GetWindow(this)}.ShowDialog();
 }
 // Check's findings (tool versions, registry warnings, the resolved target) go to the live log, where longer output has room.
 private async void CheckClick(object s,RoutedEventArgs e)=>await Operation(async(p,ct)=> {
  var rows=await _publisher.Check(p,ct);
  liveLog.AppendText("Check "+p.Name+Environment.NewLine+string.Join(Environment.NewLine,rows.Select(r=>"  "+r))+Environment.NewLine);liveLog.ScrollToEnd();
 });
 private async void PrepareClick(object s,RoutedEventArgs e) {if(!await ResolveVersion(false,true))return;await Operation((p,ct)=>_publisher.Prepare(p,ct));}
 private async void PushClick(object s,RoutedEventArgs e) {if(Profile==null||!await ResolveVersion(true,true)||Profile==null||!Confirm(Summary(Profile)))return;var release=notes.Text;retryDeploy.Visibility=Visibility.Collapsed;await Operation((p,ct)=>_publisher.Push(p,release,ct));ShowDeployments();}
 private async void CombinedClick(object s,RoutedEventArgs e) {if(Profile==null||!await ResolveVersion(true,true)||Profile==null||!Confirm(Summary(Profile)))return;var release=notes.Text;retryDeploy.Visibility=Visibility.Collapsed;await Operation(async(p,ct)=> {_publisher.ValidateReleaseNotes(p,release);await _publisher.Check(p,ct);await _publisher.Prepare(p,ct);await _publisher.Push(p,release,ct);});ShowDeployments();}
 // After Push: one line on the deploys the server ran (Auto deploy), and Retry deploy while any of them failed.
 private void ShowDeployments() {
  if(_publisher.LastRun is not {Operation:"Push"} run||run.Deployments.Count==0) {retryDeploy.Visibility=Visibility.Collapsed;return;}
  int Count(Em.Api.Core.Models.CtnDeployResult result)=>run.Deployments.Count(d=>d.Result==result);
  var failed=Count(Em.Api.Core.Models.CtnDeployResult.Failed);
  message.Text=message.Text.TrimEnd()+$" Deployed {Count(Em.Api.Core.Models.CtnDeployResult.Success)}, failed {failed}, skipped {Count(Em.Api.Core.Models.CtnDeployResult.Skipped)}."+
   (failed>0?" "+string.Join(" ",run.Deployments.Where(d=>d.Result==Em.Api.Core.Models.CtnDeployResult.Failed).Select(d=>d.Repository+": "+d.Message))+" Fix the server, then Retry deploy.":"");
  retryDeploy.Visibility=failed>0?Visibility.Visible:Visibility.Collapsed;
 }
 private async void RetryDeployClick(object s,RoutedEventArgs e) {
  if(_cancel!=null||_publisher.LastRun is not {} run||_publisher.LastRunDirectory is not {} directory)return;
  retryDeploy.IsEnabled=operations.IsEnabled=false;message.Text="Deploying again…";
  try {var failed=await _publisher.RetryDeployments(run,directory);message.Text=failed==0?"Retry deploy succeeded.":$"Retry deploy: {failed} still failing.";ShowDeployments();ReloadHistory();}
  catch(Exception ex) {message.Text=Mask(ex.Message);}
  finally {retryDeploy.IsEnabled=operations.IsEnabled=true;}
 }
 private string Summary(PublishProfile p) {
  var tag=p.Container?.Target.VersionTag??"";
  var floating=p.Container!=null&&UsesVersionStrip(p)?ContainerVersion.FloatingTagsOf(tag):[];
  return $"Publish {p.Name}?\n{targetText.Text}\nVersion: {p.NuGet?.VersionOverride ?? tag}\n"+
   (floating.Length>0?"Floating tags moved to this build: "+string.Join(", ",floating)+"\n":"")+
   (floating.Contains("latest")?"Warning: latest will point to this build after the version tag succeeds.\n":"")+
   "Review the selected artifacts and target before continuing.";
 }
 private async void VerifyClick(object s,RoutedEventArgs e)=>await Operation((p,ct)=>_publisher.Verify(p,ct));
 private void CancelClick(object s,RoutedEventArgs e)=>_cancel?.Cancel();
 private void AddPackagesClick(object s,RoutedEventArgs e) {var picker=new OpenFileDialog {Filter="NuGet packages (*.nupkg)|*.nupkg",Multiselect=true};if(picker.ShowDialog()==true)AddPackages(picker.FileNames);}
 private void AddPackages(IEnumerable<string> files) {if(Profile==null||_cancel!=null)return;try {_publisher.AddPackages(Profile,files);BindArtifacts();}catch(Exception ex){message.Text=ex.Message;}}
 private void PackagesDragOver(object s,DragEventArgs e) {e.Effects=_kind==PublishKind.NuGet&&_cancel==null&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
 private void PackagesDrop(object s,DragEventArgs e) {if(_kind==PublishKind.NuGet&&e.Data.GetData(DataFormats.FileDrop) is string[] files)AddPackages(files);}
 private void CopyTarget(object s,RoutedEventArgs e) {try {Clipboard.SetText(targetText.Text);}catch(System.Runtime.InteropServices.COMException ex){message.Text=ex.Message;}}
 private static bool Confirm(string text)=>MessageBox.Show(text,"Publisher",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
 private void OpenFolder(string folder) {try {Directory.CreateDirectory(folder);Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){message.Text=ex.Message;}}
 private async void DockerInsecureClick(object s,RoutedEventArgs e) {
  if(Profile is not {Kind:PublishKind.Container} p||p.Container!.Mode==ContainerMode.Set) {message.Text="Select a container profile that pushes to one registry.";return;}
  try {
   var host=(await _targets.ResolveContainer(p,CancellationToken.None)).Split('/')[0];var path=DockerDaemonConfig.DefaultPath;
   if(DockerDaemonConfig.IsListed(path,host)) {message.Text=$"'{host}' is already in insecure-registries ({path}). If it was added just now, restart Docker Desktop.";return;}
   if(!Confirm($"Add '{host}' to insecure-registries in\n{path}?\n\nDocker will then accept plain HTTP for this host. The file is backed up first, and Docker Desktop has to be restarted for it to apply."))return;
   var backup=DockerDaemonConfig.AddInsecureRegistry(path,host);
   message.Text=$"Added '{host}' to {path}"+(string.IsNullOrEmpty(backup)?"":$" (backup: {backup})")+". Restart Docker Desktop (tray icon > Restart) for it to apply, then Check again.";
  }catch(Exception ex){message.Text=Mask(ex.Message);}
 }
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
