using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Publish;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Ui.Wpf.Navigations.Publish;

public partial class PublishProfileDialog : Window {
 public PublishProfile Profile { get; }
 public bool SaveAs { get; private set; }
 private readonly PublishTargets _targets;
 private readonly PublishSecretStore _secrets;
 private readonly string _logs;
 private readonly List<Action> _commit=[];
 private bool _busy;
 public PublishProfileDialog(PublishProfile profile,PublishTargets targets,PublishSecretStore secrets,string logs) {
  InitializeComponent();Profile=profile.Clone();foreach(var c in Profile.Credentials)c.Remember=secrets.IsRemembered(c);_targets=targets;_secrets=secrets;_logs=logs;
  var source=Tab("Source");Fields(source,Profile,["Name","Description","Workspace"]);
  if(Profile.NuGet is {} n)Fields(source,n,["Sources"]);else Fields(source,Profile.Container!.Template,["Project","PublishSource","PublishProfile"]);
  Button(source,"Re-read project information",async()=>await ReadProjects(source));
  var build=Tab("Build");
  if(Profile.NuGet is {} nuget)Fields(build,nuget,["Configuration","VersionOverride","MsbuildProperties","DuplicateHandling"]);
  else {
   Fields(build,Profile.Container!,["Mode"]);
   Group(build,"Dockerfile",Profile.Container!.Dockerfile);Fields(build,Profile.Container,["LocalImage"]);
   Button(build,"Select local image",async()=> {
    var result=await new PublishProcessRunner().RunAsync("docker",["image","ls","--format","json"],Profile.Workspace);
    if(result.ExitCode!=0)throw new IOException("Cannot list local images. Start Docker.");
    var list=new ComboBox();var images=result.Output.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=> {using var json=JsonDocument.Parse(line);return json.RootElement.GetProperty("ID").GetString()+" · "+json.RootElement.GetProperty("Repository").GetString()+":"+json.RootElement.GetProperty("Tag").GetString();}).ToArray();
    list.ItemsSource=images;list.SelectionChanged+=(_,_)=> {if(list.SelectedItem is string image)Profile.Container.LocalImage=image.Split(" · ")[0];};build.Children.Add(list);
   });
   Group(build,".NET template",Profile.Container.Template);
   Button(build,"Preview generated Dockerfile",()=> {ShowText("Dockerfile preview",TemplateBuilder.Dockerfile(Profile.Container.Template));return Task.CompletedTask;});
   Button(build,"Preview file set from folder",()=> {var picker=new OpenFolderDialog {Title="Choose an existing publish output for preview"};if(picker.ShowDialog()==true)ShowText("File set preview",string.Join("\n",TemplateBuilder.SelectFiles(picker.FolderName,Profile.Container.Template.FileSet,Profile.Container.Set.FileLists)));return Task.CompletedTask;});
   Group(build,"Compose (build only)",Profile.Container.Compose);
   Button(build,"Read Compose build services",async()=> {
    var compose=Profile.Container.Compose;
    var result=await new PublishProcessRunner().RunAsync("docker",["compose","-f",Profile.Resolve(compose.File),"--project-directory",Profile.Resolve(compose.ProjectDirectory),"config","--format","json"],Profile.Workspace);
    if(result.ExitCode!=0)throw new IOException("Compose config failed. Check Tools and file path.");
    using var json=JsonDocument.Parse(result.Output);
    foreach(var service in json.RootElement.GetProperty("services").EnumerateObject().Where(s=>s.Value.TryGetProperty("build",out _))) {
     var row=new CheckBox {Content=service.Name,Margin=new Thickness(4)};row.Checked+=(_,_)=> {if(!compose.Services.Any(x=>x.Service==service.Name))compose.Services.Add(new() {Service=service.Name});};row.Unchecked+=(_,_)=>compose.Services.RemoveAll(x=>x.Service==service.Name);build.Children.Add(row);
    }message.Text="Select services; edit their repository/tag in the Compose mapping above.";
   });
   Group(build,"Ordered Set / shared file lists",Profile.Container.Set);
  }
  var target=Tab("Target");if(Profile.NuGet!=null)Group(target,"NuGet target",Profile.NuGet.Target);else Group(target,"Container target",Profile.Container!.Target);
  Button(target,"Use active built-in server and select destination",async()=>await BuiltIn(target));
  var address=new DockPanel();var addressText=new TextBlock {Text=_targets.Connection,VerticalAlignment=VerticalAlignment.Center};var copy=new Button {Content="⧉",ToolTip="Copy server address"};System.Windows.Automation.AutomationProperties.SetName(copy,"Copy server address");copy.Click+=(_,_)=>Clipboard.SetText(_targets.Connection);DockPanel.SetDock(copy,Dock.Right);address.Children.Add(copy);address.Children.Add(addressText);target.Children.Add(address);
  Button(target,"Refresh latest versions",async()=> {
   var local=PublishLog.History(_logs).SelectMany(h=>h.Run.Artifacts).FirstOrDefault(a=>a.ProfileId==Profile.Id&&a.Result==PublishResult.Success)?.Version??"none";
   var remote="not available";
   if(Profile.Container!=null&&Profile.Container.Mode!=ContainerMode.Set) {try {var reference=await _targets.ResolveContainer(Profile,CancellationToken.None);remote=await new OciClient(_targets).LatestTag(reference,await _targets.OciCredential(Profile,reference.Split('/')[0],CancellationToken.None),CancellationToken.None);}catch(Exception){}}
   message.Text="Last local version: "+local+" · Latest remote tag: "+remote;
  });
  var credentials=Tab("Credentials");Fields(credentials,Profile,["SensitiveDataStorage"]);
  credentials.Children.Add(new TextBlock {Text="Separate: DPAPI CurrentUser with optional Remember; otherwise session only. Plaintext: secrets are saved as ordinary text in this JSON file.",Margin=new Thickness(4,8,4,8)});
  Group(credentials,"Credentials (robot token / API key)",Profile,"Credentials");
  var advanced=Tab("Advanced");Fields(advanced,Profile,["KeepWorkspace","RequireReleaseNotes"]);if(Profile.Container!=null)Fields(advanced,Profile.Container,["UseMyDockerLogin"]);
 }
 private StackPanel Tab(string name) {var panel=new StackPanel {Margin=new Thickness(12)};var tab=new TabItem {Header=name,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};tab.SetResourceReference(StyleProperty,"materialTabItemStyle");tabs.Items.Add(tab);return panel;}
 private static string Label(string name)=>Regex.Replace(name,"([a-z])([A-Z])","$1 $2");
 private void Button(Panel panel,string label,Func<Task> action) {
  var button=new Button {Content=label,HorizontalAlignment=HorizontalAlignment.Left};panel.Children.Add(button);
  button.Click+=async(_,_)=> {if(_busy)return;_busy=true;button.IsEnabled=false;try {await action();}catch(Exception ex){var masker=new SecretMasker();foreach(var c in Profile.Credentials)masker.Add(c.Secret);message.Text=masker.Mask(ex.Message);}finally {_busy=false;button.IsEnabled=true;}};
 }
 private void Group(Panel panel,string label,object obj,string? property=null) {
  var content=new StackPanel {Margin=new Thickness(10)};var expander=new Expander {Header=label,Content=content,IsExpanded=false,Margin=new Thickness(4)};expander.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");panel.Children.Add(expander);
  if(property==null)Fields(content,obj);else Fields(content,obj,[property]);
 }
 private void Fields(Panel panel,object obj,string[]? names=null) {
  foreach(var property in obj.GetType().GetProperties().Where(p=>p.CanWrite&&(names==null||names.Contains(p.Name)))) {
   if(property.Name is "NeedsSecret" or "SecretRef")continue;
   var type=property.PropertyType;var value=property.GetValue(obj);
   panel.Children.Add(new TextBlock {Text=Label(property.Name),Margin=new Thickness(4,8,4,2),FontWeight=FontWeights.SemiBold});
   if(type==typeof(string)||type==typeof(int)) {
    if(property.Name=="Secret"&&obj is PublishCredential credential) {
     if(string.IsNullOrEmpty(credential.Secret)&&_secrets.Get(credential,credential.ScopeHost)==null)panel.Children.Add(new TextBlock {Text="Credential needs to be filled for this host.",Margin=new Thickness(4)});
     var secret=new PasswordBox {Password=credential.Secret??_secrets.Get(credential,credential.ScopeHost)??"",Margin=new Thickness(4)};
     secret.PasswordChanged+=(_,_)=>credential.Secret=secret.Password;panel.Children.Add(secret);
     _commit.Add(()=> {if(secret.Password.Length>0)credential.Secret=secret.Password;});continue;
    }
    var field=new TextBox {Text=value?.ToString()??"",Margin=new Thickness(4),IsReadOnly=property.Name is "Id" or "Server"};
    field.TextChanged+=(_,_)=> {if(type==typeof(string))property.SetValue(obj,field.Text);else if(int.TryParse(field.Text,out var number))property.SetValue(obj,number);};panel.Children.Add(field);
    if(property.Name is "Path" or "Project" or "File" or "ExistingDockerfile" or "PublishProfile") {
     Button(panel,"Browse "+Label(property.Name),()=> {var picker=new OpenFileDialog {Filter=property.Name is "Path" or "Project"?"Project / solution|*.csproj;*.sln;*.slnx|All files|*.*":"All files|*.*"};if(picker.ShowDialog()==true)field.Text=Path.GetRelativePath(Path.GetFullPath(Profile.Workspace),picker.FileName);return Task.CompletedTask;});
    } else if(property.Name is "Workspace" or "Context" or "ProjectDirectory")Button(panel,"Browse folder",()=> {var picker=new OpenFolderDialog();if(picker.ShowDialog()==true)field.Text=property.Name=="Workspace"?picker.FolderName:Path.GetRelativePath(Path.GetFullPath(Profile.Workspace),picker.FolderName);return Task.CompletedTask;});
   } else if(type==typeof(bool)) {
    var check=new CheckBox {Content=Label(property.Name),IsChecked=value as bool?,Margin=new Thickness(4)};check.Checked+=(_,_)=>property.SetValue(obj,true);check.Unchecked+=(_,_)=>property.SetValue(obj,false);panel.Children.Add(check);
   } else if(type.IsEnum) {
    var combo=new ComboBox {ItemsSource=Enum.GetValues(type),SelectedItem=value,Margin=new Thickness(4)};combo.SelectionChanged+=(_,_)=> {if(combo.SelectedItem!=null)property.SetValue(obj,combo.SelectedItem);};panel.Children.Add(combo);
   } else if(type==typeof(List<string>)) {
    var field=new TextBox {Text=string.Join("\n",(List<string>)value!),Height=double.NaN,Padding=new Thickness(14,10,14,10),VerticalContentAlignment=VerticalAlignment.Top,AcceptsReturn=true,MinHeight=62,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(4)};
    field.TextChanged+=(_,_)=>property.SetValue(obj,field.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).ToList());panel.Children.Add(field);
    Button(panel,"Import lines from .txt",()=> {var picker=new OpenFileDialog {Filter="Text file|*.txt"};if(picker.ShowDialog()==true)field.Text=File.ReadAllText(picker.FileName);return Task.CompletedTask;});
   } else if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>)) {
    var list=(IList)value!;var entries=new StackPanel();panel.Children.Add(entries);
    void AddRow(object item) {var row=new StackPanel {Margin=new Thickness(6)};entries.Children.Add(row);Fields(row,item);Button(row,"Remove item",()=> {list.Remove(item);entries.Children.Remove(row);return Task.CompletedTask;});}
    foreach(var item in list)AddRow(item!);
    Button(panel,"Add "+Label(property.Name),()=> {var item=Activator.CreateInstance(type.GenericTypeArguments[0])!;list.Add(item);AddRow(item);return Task.CompletedTask;});
   } else if(value!=null)Group(panel,Label(property.Name),value);
  }
 }
 private async Task ReadProjects(Panel panel) {
  var reader=new ProjectReader(new PublishProcessRunner());var sources=Profile.NuGet?.Sources.Select(s=>Profile.Resolve(s.Path)).ToArray()??[Profile.Resolve(Profile.Container!.Template.Project)];
  var rows=new List<ProjectInformation>();foreach(var source in sources)foreach(var path in await reader.Projects(source))rows.Add(await reader.Read(path));
  ShowText("Project information (review changes; overrides and target are retained)",string.Join("\n",rows.Select(x=>$"{x.Path}\nPackage {x.PackageId} · version {x.Version} · frameworks {x.Frameworks} · {x.Reason}")));
  if(Profile.NuGet!=null)foreach(var source in Profile.NuGet.Sources) {
   var projects=await reader.Projects(Profile.Resolve(source.Path));
   if(source.SelectAllPackable&&source.Projects.Count==0)source.Projects=rows.Where(x=>x.IsPackable&&projects.Contains(x.Path)).Select(x=>Path.GetRelativePath(Profile.Workspace,x.Path)).ToList();
   source.SelectAllPackable=false;
   foreach(var info in rows.Where(x=>projects.Contains(x.Path))) {
    var path=Path.GetRelativePath(Profile.Workspace,info.Path);var check=new CheckBox {Content=info.PackageId+" · "+info.Reason,IsEnabled=info.IsPackable,IsChecked=source.Projects.Contains(path)&&info.IsPackable,Margin=new Thickness(4)};
    check.Checked+=(_,_)=> {if(!source.Projects.Contains(path))source.Projects.Add(path);};check.Unchecked+=(_,_)=>source.Projects.Remove(path);panel.Children.Add(check);
   }
  }
  if(Profile.Container!=null) {
   var hosts=new ComboBox {ItemsSource=rows.Where(x=>x.IsHost).ToArray(),DisplayMemberPath="Path",Margin=new Thickness(4)};
   hosts.SelectionChanged+=(_,_)=> {if(hosts.SelectedItem is ProjectInformation host) {Profile.Container.Template.Project=Path.GetRelativePath(Profile.Workspace,host.Path);message.Text="Host selected: "+host.PackageId+" · frameworks: "+host.Frameworks;}};
   panel.Children.Add(new TextBlock {Text="Choose container host",Margin=new Thickness(4)});panel.Children.Add(hosts);
  }
 }
 private async Task BuiltIn(Panel panel) {
  if(_targets.Connection.Length==0)throw new InvalidOperationException("Connect to the built-in server first.");
  if(Profile.NuGet is {} n) {
   var feeds=await _targets.Feeds();var picker=new ComboBox {ItemsSource=feeds,Margin=new Thickness(4)};
   picker.SelectionChanged+=(_,_)=> {if(picker.SelectedItem is NuPakFeedInfo feed) {n.Target.Type=TargetType.BuiltIn;n.Target.Server=_targets.Scope;n.Target.Feed=feed.Id;message.Text="Built-in feed selected: "+feed.ServiceIndex;}};panel.Children.Add(picker);
  } else {
   var roots=await _targets.Roots();var rootPicker=new ComboBox {ItemsSource=roots,DisplayMemberPath="Name",Margin=new Thickness(4)};var imagePicker=new ComboBox {DisplayMemberPath="Name",Margin=new Thickness(4)};panel.Children.Add(rootPicker);panel.Children.Add(imagePicker);
   rootPicker.SelectionChanged+=async(_,_)=> {if(rootPicker.SelectedItem is CtnRootInfo root) {try {imagePicker.ItemsSource=await _targets.Images(root.Id);Profile.Container!.Target.Root=root.Name;}catch(Exception ex){message.Text=ex.Message;}}};
   imagePicker.SelectionChanged+=(_,_)=> {if(imagePicker.SelectedItem is CtnImageInfo image) {var target=Profile.Container!.Target;target.Type=TargetType.BuiltIn;target.Server=_targets.Scope;target.Container=image.Name;message.Text="Built-in target: "+target.Root+"/"+image.Name;}};
  }
 }
 private void ShowText(string title,string text) {var view=new TextBox {Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};view.SetResourceReference(StyleProperty,"fieldBoxStyle");var window=new Window {Title=title,Owner=this,Width=750,Height=520,Content=view,WindowStartupLocation=WindowStartupLocation.CenterOwner};window.SetResourceReference(BackgroundProperty,"themeWindowBackgroundBrush");window.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");window.ShowDialog();}
 private void Save(bool asNew) {try {foreach(var commit in _commit)commit();Profile.Validate();if(!Directory.Exists(Profile.Workspace))throw new InvalidDataException("Workspace directory is required.");SaveAs=asNew;DialogResult=true;}catch(Exception ex){message.Text=ex.Message;}}
 private void SaveClick(object s,RoutedEventArgs e)=>Save(false);
 private void SaveAsClick(object s,RoutedEventArgs e)=>Save(true);
}
