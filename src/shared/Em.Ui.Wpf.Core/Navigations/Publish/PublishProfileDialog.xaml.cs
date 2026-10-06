using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Publish;
using Em.Ui.Wpf.Windows;
using FontAwesome6;
using FontAwesome6.Fonts;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Navigations.Publish;

public partial class PublishProfileDialog : EmWindow {
 private const double LabelWidth=170;
 private static readonly Dictionary<string,string> Captions=new() {["Credentials"]="Credentials (robot token / API key)",["MsbuildProperties"]="MSBuild properties"};
 private static readonly string[] SourceTemplateFields=["Project","PublishSource","PublishProfile"];
 public PublishProfile Profile { get; }
 public bool SaveAs { get; private set; }
 private readonly PublishTargets _targets;
 private readonly PublishSecretStore _secrets;
 private readonly string _logs;
 private readonly List<Action> _commit=[];
 private bool _busy;
 private Action? _modeChanged;
 private Action? _targetTypeChanged;
 private readonly bool _isNew;
 /// <param name="isNew">True for a profile that has never been saved: only then can its Tagging be chosen.</param>
 public PublishProfileDialog(PublishProfile profile,PublishTargets targets,PublishSecretStore secrets,string logs,bool isNew=false) {
  InitializeComponent();_isNew=isNew;Profile=profile.Clone();foreach(var c in Profile.Credentials)c.Remember=secrets.IsRemembered(c);_targets=targets;_secrets=secrets;_logs=logs;
  var source=Tab("Source");Fields(source,Profile,["Name","Description","Workspace"]);
  if(Profile.NuGet is {} n)Fields(source,n,["Sources"]);else Fields(source,Profile.Container!.Template,SourceTemplateFields);
  var projects=new StackPanel();Actions(source,("Re-read project information",EFontAwesomeIcon.Solid_ArrowsRotate,async()=>await ReadProjects(projects)));source.Children.Add(projects);
  var build=Tab("Build");
  if(Profile.NuGet is {} nuget)Fields(build,nuget,["Configuration","VersionOverride","MsbuildProperties","DuplicateHandling"]);
  else {
   var container=Profile.Container!;Fields(build,container,["Mode"]);
   var mode=new StackPanel();build.Children.Add(mode);
   _modeChanged=()=> {mode.Children.Clear();BuildMode(mode,container);};_modeChanged();
  }
  var target=Tab("Target");
  void BuildTarget() {target.Children.Clear();TargetTab(target);}
  _targetTypeChanged=()=>Dispatcher.BeginInvoke(BuildTarget);
  BuildTarget();
  var credentials=Tab("Credentials");Fields(credentials,Profile,["SensitiveDataStorage"]);
  Row(credentials,"",Help("Separate: DPAPI CurrentUser with optional Remember; otherwise session only. Plaintext: secrets are saved as ordinary text in this JSON file."));
  Fields(credentials,Profile,["Credentials"]);
  var advanced=Tab("Advanced");Fields(advanced,Profile,["KeepWorkspace","RequireReleaseNotes"]);if(Profile.Container!=null)Fields(advanced,Profile.Container,["UseMyDockerLogin"]);
 }
 /// <summary>
 /// Target tab, rebuilt when the target type changes. Built-in: the active server (read from the connection, never typed)
 /// plus the destination on it, picked with Select destination. Custom: any registry or feed, typed by hand.
 /// </summary>
 private void TargetTab(Panel tab) {
  object destination=Profile.NuGet!=null?Profile.NuGet.Target:Profile.Container!.Target;
  var container=destination is ContainerTarget;
  var section=Section(tab,"Destination");Fields(section,destination,["Type"]);
  if(destination is ContainerTarget {Type:TargetType.BuiltIn} or NuGetTarget {Type:TargetType.BuiltIn}) {
   var connected=_targets.Connection.Length>0;
   var addressText=new TextBlock {Text=connected?_targets.Connection:"Not connected",TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=480,VerticalAlignment=VerticalAlignment.Center,ToolTip=connected?_targets.Connection:null};
   var copy=new Button {Content=new FontAwesome {Icon=EFontAwesomeIcon.Regular_Copy},ToolTip="Copy server address",IsEnabled=connected,Margin=new Thickness(6,0,0,0)};copy.SetResourceReference(StyleProperty,"rowActionButtonStyle");AutomationProperties.SetName(copy,"Copy server address");copy.Click+=(_,_)=>Clipboard.SetText(_targets.Connection);
   var address=new StackPanel {Orientation=Orientation.Horizontal};address.Children.Add(addressText);address.Children.Add(copy);Row(section,"Server",address);
   var saved=destination is ContainerTarget c?c.Server:((NuGetTarget)destination).Server;
   if(!connected)Row(section,"",Help("Built-in publishes to the server this application is connected to. Connect first, or switch Type to Custom."));
   else if(saved.Length>0&&saved!=_targets.Scope)Row(section,"",Help("This destination was chosen on another server ("+saved+"). Select the destination again."));
   var fields=Sub(section);section.Children.Add(fields);
   void RefreshFields() {fields.Children.Clear();Fields(fields,destination,container?["Root","Container"]:["Feed"]);}
   RefreshFields();
   var pickers=Sub(section);
   Actions(section,("Select destination",EFontAwesomeIcon.Solid_Server,async()=>await BuiltIn(pickers,RefreshFields))).IsEnabled=connected;
   section.Children.Add(pickers);
  } else {
   Fields(section,destination,container?["Host","Repository"]:["ServiceIndex"]);
   Row(section,"",Help(container
    ?"Any OCI registry: Host such as registry.example.com:5000 (no http://), Repository such as team/app. Add a push credential for this host in Credentials; tick Allow Http there for a plain-HTTP registry."
    :"Any NuGet v3 feed: the service index URL, e.g. https://nuget.example.com/v3/index.json. Add a credential for this host in Credentials."));
  }
  // Tagging decides the publish strip; the tag itself is typed there, not here.
  var versions=section;
  if(container) {
   // Chosen once, when the profile is created: switching an image between versioned and free tags later would mix
   // both kinds of tag in one repository, and switching back would break the build-number sequence.
   versions=Section(tab,"Version");var tagging=((ContainerTarget)destination).Tagging;
   if(_isNew) {
    Fields(versions,destination,["Tagging"]);
    Row(versions,"",Help("Chosen once: it cannot be changed after the profile is saved. Standard: the Publish page shows A.B.C and the channel; the build number and the floating tags (alpha, beta, release, latest) follow the convention. Manual: the Publish page shows one box for a free tag such as dev; no floating tags are moved."));
   } else {
    Row(versions,"Tagging",new TextBlock {Text=tagging.ToString(),VerticalAlignment=VerticalAlignment.Center});
    Row(versions,"",Help(tagging==TaggingMode.Standard
     ?"Standard: A.B.C and the channel on the Publish page, with the build number and floating tags of the convention. Fixed when the profile was created; for free tags, create a new profile with Manual tagging."
     :"Manual: a free tag typed on the Publish page, no floating tags. Fixed when the profile was created; for versioned tags, create a new profile with Standard tagging."));
   }
  }
  Actions(versions,("Refresh latest versions",EFontAwesomeIcon.Solid_ArrowsRotate,async()=> {
   var local=PublishLog.History(_logs).SelectMany(h=>h.Run.Artifacts).FirstOrDefault(a=>a.ProfileId==Profile.Id&&a.Result==PublishResult.Success)?.Version??"none";
   var remote="not available";
   if(Profile.Container!=null&&Profile.Container.Mode!=ContainerMode.Set) {try {var reference=await _targets.ResolveContainer(Profile,CancellationToken.None);remote=await new OciClient(_targets).LatestTag(reference,await _targets.OciCredential(Profile,reference.Split('/')[0],CancellationToken.None),CancellationToken.None);}catch(Exception){}}
   message.Text="Last local version: "+local+" · Latest remote tag: "+remote;
  }));
 }
 private void BuildMode(Panel panel,ContainerProfile container) {
  switch(container.Mode) {
   case ContainerMode.Dockerfile:Group(panel,"Dockerfile",container.Dockerfile);break;
   case ContainerMode.LocalImage: {
    var section=Section(panel,"Local image");var fields=Sub(section);section.Children.Add(fields);Fields(fields,container,["LocalImage"]);
    var images=Sub(section);
    Actions(section,("Select local image",EFontAwesomeIcon.Brands_Docker,async()=> {
     var result=await new PublishProcessRunner().RunAsync("docker",["image","ls","--format","json"],Profile.Workspace);
     if(result.ExitCode!=0)throw new IOException("Cannot list local images. Start Docker.");
     var list=new ComboBox {ItemsSource=result.Output.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=> {using var json=JsonDocument.Parse(line);return json.RootElement.GetProperty("ID").GetString()+" · "+json.RootElement.GetProperty("Repository").GetString()+":"+json.RootElement.GetProperty("Tag").GetString();}).ToArray()};
     list.SelectionChanged+=(_,_)=> {if(list.SelectedItem is string image) {container.LocalImage=image.Split(" · ")[0];fields.Children.Clear();Fields(fields,container,["LocalImage"]);}};
     images.Children.Clear();Row(images,"Local images",list);
    }));
    section.Children.Add(images);break;
   }
   case ContainerMode.Template: {
    var section=Group(panel,".NET template",container.Template,SourceTemplateFields);
    Actions(section,("Preview generated Dockerfile",EFontAwesomeIcon.Solid_Eye,()=> {ShowText("Dockerfile preview",TemplateBuilder.Dockerfile(container.Template));return Task.CompletedTask;}),
     ("Preview file set from folder",EFontAwesomeIcon.Solid_FolderOpen,()=> {var picker=new OpenFolderDialog {Title="Choose an existing publish output for preview"};if(picker.ShowDialog()==true)ShowText("File set preview",string.Join("\n",TemplateBuilder.SelectFiles(picker.FolderName,container.Template.FileSet,container.Set.FileLists)));return Task.CompletedTask;}));
    break;
   }
   case ContainerMode.Compose: {
    var compose=container.Compose;var section=Section(panel,"Compose (build only)");var fields=Sub(section);section.Children.Add(fields);
    void RefreshCompose() {fields.Children.Clear();Fields(fields,compose);}
    RefreshCompose();
    var services=Sub(section);
    Actions(section,("Read Compose build services",EFontAwesomeIcon.Brands_Docker,async()=> {
     var result=await new PublishProcessRunner().RunAsync("docker",["compose","-f",Profile.Resolve(compose.File),"--project-directory",Profile.Resolve(compose.ProjectDirectory),"config","--format","json"],Profile.Workspace);
     if(result.ExitCode!=0)throw new IOException("Compose config failed. Check Tools and file path.");
     using var json=JsonDocument.Parse(result.Output);services.Children.Clear();var first=true;
     foreach(var service in json.RootElement.GetProperty("services").EnumerateObject().Where(s=>s.Value.TryGetProperty("build",out _))) {
      var name=service.Name;var row=new CheckBox {Content=name,IsChecked=compose.Services.Any(x=>x.Service==name)};
      row.Checked+=(_,_)=> {if(!compose.Services.Any(x=>x.Service==name)) {compose.Services.Add(new() {Service=name});RefreshCompose();}};
      row.Unchecked+=(_,_)=> {compose.Services.RemoveAll(x=>x.Service==name);RefreshCompose();};
      Row(services,first?"Build services":"",row);first=false;
     }
     message.Text="Select services; edit their repository/tag in the Services list above.";
    }));
    section.Children.Add(services);break;
   }
   case ContainerMode.Set:Group(panel,"Ordered set / shared file lists",container.Set);break;
  }
 }
 private StackPanel Tab(string name) {var panel=new StackPanel {Margin=new Thickness(6,16,16,4)};var tab=new TabItem {Header=name,Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};tab.SetResourceReference(StyleProperty,"materialTabItemStyle");tabs.Items.Add(tab);return panel;}
 private static string Label(string name)=>Captions.TryGetValue(name,out var caption)?caption:Regex.Replace(name,"([a-z])([A-Z])","$1 $2");
 private static TextBlock Help(string text) {var help=new TextBlock {Text=text,Margin=new Thickness(2,0,0,0)};help.SetResourceReference(StyleProperty,"fieldHelpStyle");return help;}
 /// <summary>One form row: label column on the left, the input filling the middle, an optional action (browse/import) right of the input.</summary>
 private static void Row(Panel panel,string label,FrameworkElement input,FrameworkElement? action=null,bool top=false) {
  var row=new Grid {Margin=new Thickness(0,0,0,12)};
  row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(LabelColumn(panel))});row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
  var caption=new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=top?VerticalAlignment.Top:VerticalAlignment.Center,Margin=new Thickness(2,top?13:0,12,0)};caption.SetResourceReference(StyleProperty,"fieldLabelStyle");
  row.Children.Add(caption);Grid.SetColumn(input,1);row.Children.Add(input);
  if(action!=null) {action.Margin=new Thickness(8,0,0,0);action.VerticalAlignment=top?VerticalAlignment.Top:VerticalAlignment.Center;Grid.SetColumn(action,2);row.Children.Add(action);}
  panel.Children.Add(row);
 }
 /// <summary>Horizontal offset of a panel from the tab edge (stored in Tag), so nested label columns shrink and inputs stay aligned.</summary>
 /// <summary>A plain child panel that keeps the parent's indent, so its rows stay on the same input column.</summary>
 private static StackPanel Sub(Panel parent)=>new() {Tag=parent.Tag};
 private static double Indent(Panel panel)=>panel.Tag is double indent?indent:0;
 private static double LabelColumn(Panel panel)=>Math.Max(100,LabelWidth-Indent(panel));
 /// <summary>A collapsible section in the home navigation-group look; returns the panel its rows go into. Top-level sections are accent-coloured and open, nested ones neutral and closed.</summary>
 private static StackPanel Section(Panel panel,string caption) {
  var top=Indent(panel)==0;var expander=new Expander {IsExpanded=top,Margin=new Thickness(0,0,0,top?10:4)};expander.SetResourceReference(StyleProperty,"sectionExpanderStyle");
  var title=new TextBlock {Text=caption,TextWrapping=TextWrapping.NoWrap,FontWeight=top?FontWeights.Bold:FontWeights.SemiBold,FontSize=top?13:12};if(top)title.SetResourceReference(TextBlock.ForegroundProperty,"themeAccentBrush");
  var content=new StackPanel {Tag=Indent(panel)+22};expander.Header=title;expander.Content=content;panel.Children.Add(expander);return content;
 }
 private StackPanel Group(Panel panel,string label,object obj,string[]? except=null) {var content=Section(panel,label);Fields(content,obj,null,except);return content;}
 /// <summary>A row of action buttons aligned with the input column.</summary>
 private StackPanel Actions(Panel panel,params (string Text,EFontAwesomeIcon Icon,Func<Task> Action)[] actions) {
  var row=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(LabelColumn(panel),0,0,12)};
  foreach(var (text,icon,action) in actions) {var button=TextButton(text,icon,action);if(row.Children.Count>0)button.Margin=new Thickness(8,0,0,0);row.Children.Add(button);}
  panel.Children.Add(row);return row;
 }
 private Button TextButton(string text,EFontAwesomeIcon icon,Func<Task> action) {
  var button=new Button {HorizontalAlignment=HorizontalAlignment.Left};var caption=new TextBlock {Text=text,TextWrapping=TextWrapping.NoWrap,VerticalAlignment=VerticalAlignment.Center};caption.SetBinding(TextBlock.ForegroundProperty,new Binding(nameof(Foreground)) {Source=button});
  var glyph=new FontAwesome {Icon=icon};glyph.SetResourceReference(StyleProperty,"buttonIconStyle");
  var content=new StackPanel {Orientation=Orientation.Horizontal};content.Children.Add(glyph);content.Children.Add(caption);button.Content=content;Wire(button,action);return button;
 }
 private Button IconButton(EFontAwesomeIcon icon,string tip,Func<Task> action) {
  var button=new Button {Content=new FontAwesome {Icon=icon,FontSize=15},ToolTip=tip,Width=42,Height=42,Padding=new Thickness(0)};AutomationProperties.SetName(button,tip);Wire(button,action);return button;
 }
 private void Wire(ButtonBase button,Func<Task> action) {
  button.Click+=async(_,_)=> {if(_busy)return;_busy=true;button.IsEnabled=false;try {await action();}catch(Exception ex){var masker=new SecretMasker();foreach(var c in Profile.Credentials)masker.Add(c.Secret);message.Text=masker.Mask(ex.Message);}finally {_busy=false;button.IsEnabled=true;}};
 }
 private void Fields(Panel panel,object obj,string[]? names=null,string[]? except=null) {
  foreach(var property in obj.GetType().GetProperties().Where(p=>p.CanWrite&&(names==null||names.Contains(p.Name))&&(except==null||!except.Contains(p.Name)))) {
   // Internal ids and the built-in server are never typed: ids are generated, the server comes from the connection.
   if(property.Name is "NeedsSecret" or "SecretRef" or "Id")continue;
   var type=property.PropertyType;var value=property.GetValue(obj);var label=Label(property.Name);
   if(type==typeof(string)||type==typeof(int)) {
    if(property.Name=="Secret"&&obj is PublishCredential credential) {
     var secret=new PasswordBox {Password=credential.Secret??_secrets.Get(credential,credential.ScopeHost)??""};
     secret.PasswordChanged+=(_,_)=>credential.Secret=secret.Password;
     var cell=new StackPanel();cell.Children.Add(secret);
     if(string.IsNullOrEmpty(credential.Secret)&&_secrets.Get(credential,credential.ScopeHost)==null)cell.Children.Add(Help("Credential needs to be filled for this host."));
     Row(panel,label,cell);
     _commit.Add(()=> {if(secret.Password.Length>0)credential.Secret=secret.Password;});continue;
    }
    var field=new TextBox {Text=value?.ToString()??""};
    field.TextChanged+=(_,_)=> {if(type==typeof(string))property.SetValue(obj,field.Text);else if(int.TryParse(field.Text,out var number))property.SetValue(obj,number);};
    Button? browse=null;
    if(property.Name is "Path" or "Project" or "File" or "ExistingDockerfile" or "PublishProfile")
     browse=IconButton(EFontAwesomeIcon.Solid_FolderOpen,"Browse "+label,()=> {var picker=new OpenFileDialog {Filter=property.Name is "Path" or "Project"?"Project / solution|*.csproj;*.sln;*.slnx|All files|*.*":"All files|*.*"};if(picker.ShowDialog()==true)field.Text=Path.GetRelativePath(Path.GetFullPath(Profile.Workspace),picker.FileName);return Task.CompletedTask;});
    else if(property.Name is "Workspace" or "Context" or "ProjectDirectory")
     browse=IconButton(EFontAwesomeIcon.Solid_FolderOpen,"Browse folder",()=> {var picker=new OpenFolderDialog();if(picker.ShowDialog()==true)field.Text=property.Name=="Workspace"?picker.FolderName:Path.GetRelativePath(Path.GetFullPath(Profile.Workspace),picker.FolderName);return Task.CompletedTask;});
    Row(panel,label,field,browse);
   } else if(type==typeof(bool)) {
    var check=new CheckBox {Content=label,IsChecked=value as bool?,HorizontalAlignment=HorizontalAlignment.Left};check.Checked+=(_,_)=>property.SetValue(obj,true);check.Unchecked+=(_,_)=>property.SetValue(obj,false);Row(panel,"",check);
   } else if(type.IsEnum) {
    var combo=new ComboBox {ItemsSource=Enum.GetValues(type),SelectedItem=value};combo.SelectionChanged+=(_,_)=> {if(combo.SelectedItem!=null)property.SetValue(obj,combo.SelectedItem);};
    if(obj is ContainerProfile&&property.Name=="Mode")combo.SelectionChanged+=(_,_)=>_modeChanged?.Invoke();
    if(obj is ContainerTarget or NuGetTarget&&property.Name=="Type")combo.SelectionChanged+=(_,_)=>_targetTypeChanged?.Invoke();
    Row(panel,label,combo);
   } else if(type==typeof(List<string>)) {
    var field=new TextBox {Text=string.Join("\n",(List<string>)value!),Height=double.NaN,Padding=new Thickness(14,10,14,10),VerticalContentAlignment=VerticalAlignment.Top,AcceptsReturn=true,MinHeight=62,MaxHeight=160,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    field.TextChanged+=(_,_)=>property.SetValue(obj,field.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).Where(s=>s.Length>0).ToList());
    var import=IconButton(EFontAwesomeIcon.Solid_FileImport,"Import lines from .txt",()=> {var picker=new OpenFileDialog {Filter="Text file|*.txt"};if(picker.ShowDialog()==true)field.Text=File.ReadAllText(picker.FileName);return Task.CompletedTask;});
    Row(panel,label,field,import,top:true);
   } else if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(List<>))ListEditor(panel,label,(IList)value!,type.GenericTypeArguments[0]);
   else if(value!=null)Group(panel,label,value);
  }
 }
 /// <summary>A list of objects: a caption, one card per item with a remove button, and an Add button.</summary>
 private void ListEditor(Panel panel,string label,IList list,Type itemType) {
  var cell=new StackPanel();var entries=new StackPanel();cell.Children.Add(entries);var indent=Indent(panel)+LabelColumn(panel)+15;
  void AddRow(object item) {
   var fields=new StackPanel {Tag=indent};var body=new DockPanel();var card=new Border {Child=body,Padding=new Thickness(14,12,8,0),Margin=new Thickness(0,0,0,10)};card.SetResourceReference(StyleProperty,"innerCardStyle");
   var remove=new Button {Content=new FontAwesome {Icon=EFontAwesomeIcon.Regular_TrashCan},ToolTip="Remove item",VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(8,0,0,0)};remove.SetResourceReference(StyleProperty,"rowActionButtonStyle");AutomationProperties.SetName(remove,"Remove item");
   Wire(remove,()=> {list.Remove(item);entries.Children.Remove(card);return Task.CompletedTask;});
   DockPanel.SetDock(remove,Dock.Right);body.Children.Add(remove);body.Children.Add(fields);entries.Children.Add(card);Fields(fields,item);
  }
  foreach(var item in list)AddRow(item!);
  var add=TextButton("Add",EFontAwesomeIcon.Solid_Plus,()=> {var item=Activator.CreateInstance(itemType)!;list.Add(item);AddRow(item);return Task.CompletedTask;});
  AutomationProperties.SetName(add,"Add to "+label);cell.Children.Add(add);Row(panel,label,cell,top:true);
 }
 private async Task ReadProjects(Panel panel) {
  var reader=new ProjectReader(new PublishProcessRunner());var sources=Profile.NuGet?.Sources.Select(s=>Profile.Resolve(s.Path)).ToArray()??[Profile.Resolve(Profile.Container!.Template.Project)];
  var rows=new List<ProjectInformation>();foreach(var source in sources)foreach(var path in await reader.Projects(source))rows.Add(await reader.Read(path));
  ShowText("Project information (review changes; overrides and target are retained)",string.Join("\n",rows.Select(x=>$"{x.Path}\nPackage {x.PackageId} · version {x.Version} · frameworks {x.Frameworks} · {x.Reason}")));
  panel.Children.Clear();
  if(Profile.NuGet!=null)foreach(var source in Profile.NuGet.Sources) {
   var projects=await reader.Projects(Profile.Resolve(source.Path));
   if(source.SelectAllPackable&&source.Projects.Count==0)source.Projects=rows.Where(x=>x.IsPackable&&projects.Contains(x.Path)).Select(x=>Path.GetRelativePath(Profile.Workspace,x.Path)).ToList();
   source.SelectAllPackable=false;var first=true;
   foreach(var info in rows.Where(x=>projects.Contains(x.Path))) {
    var path=Path.GetRelativePath(Profile.Workspace,info.Path);var check=new CheckBox {Content=info.PackageId+" · "+info.Reason,IsEnabled=info.IsPackable,IsChecked=source.Projects.Contains(path)&&info.IsPackable,HorizontalAlignment=HorizontalAlignment.Left};
    check.Checked+=(_,_)=> {if(!source.Projects.Contains(path))source.Projects.Add(path);};check.Unchecked+=(_,_)=>source.Projects.Remove(path);Row(panel,first?"Projects":"",check);first=false;
   }
  }
  if(Profile.Container!=null) {
   var hosts=new ComboBox {ItemsSource=rows.Where(x=>x.IsHost).ToArray(),DisplayMemberPath="Path"};
   hosts.SelectionChanged+=(_,_)=> {if(hosts.SelectedItem is ProjectInformation host) {Profile.Container.Template.Project=Path.GetRelativePath(Profile.Workspace,host.Path);message.Text="Host selected: "+host.PackageId+" · frameworks: "+host.Frameworks;}};
   Row(panel,"Container host",hosts);
  }
 }
 private async Task BuiltIn(Panel panel,Action refresh) {
  if(_targets.Connection.Length==0)throw new InvalidOperationException("Connect to the built-in server first.");
  panel.Children.Clear();
  if(Profile.NuGet is {} n) {
   var feeds=await _targets.Feeds();var picker=new ComboBox {ItemsSource=feeds};
   picker.SelectionChanged+=(_,_)=> {if(picker.SelectedItem is NuPakFeedInfo feed) {n.Target.Type=TargetType.BuiltIn;n.Target.Server=_targets.Scope;n.Target.Feed=feed.Id;refresh();message.Text="Built-in feed selected: "+feed.ServiceIndex;}};Row(panel,"Feed",picker);
  } else {
   var roots=await _targets.Roots();var rootPicker=new ComboBox {ItemsSource=roots,DisplayMemberPath="Name"};var imagePicker=new ComboBox {DisplayMemberPath="Name"};Row(panel,"Root",rootPicker);Row(panel,"Image",imagePicker);
   rootPicker.SelectionChanged+=async(_,_)=> {if(rootPicker.SelectedItem is CtnRootInfo root) {try {imagePicker.ItemsSource=await _targets.Images(root.Id);Profile.Container!.Target.Root=root.Name;refresh();}catch(Exception ex){message.Text=ex.Message;}}};
   imagePicker.SelectionChanged+=(_,_)=> {if(imagePicker.SelectedItem is CtnImageInfo image) {var target=Profile.Container!.Target;target.Type=TargetType.BuiltIn;target.Server=_targets.Scope;target.Container=image.Name;refresh();message.Text="Built-in target: "+target.Root+"/"+image.Name;}};
  }
 }
 private void ShowText(string title,string text) {var view=new TextBox {Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};view.SetResourceReference(StyleProperty,"fieldBoxStyle");view.Margin=new Thickness(16);var window=new EmWindow {Title=title,Owner=this,Width=750,Height=520,Content=view,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowMinimizeButton=false};window.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml",UriKind.Relative)});window.ShowDialog();}
 private void Save(bool asNew) {try {foreach(var commit in _commit)commit();Profile.Validate();if(!Directory.Exists(Profile.Workspace))throw new InvalidDataException("Workspace directory is required.");SaveAs=asNew;DialogResult=true;}catch(Exception ex){message.Text=ex.Message;}}
 private void SaveClick(object s,RoutedEventArgs e)=>Save(false);
 private void SaveAsClick(object s,RoutedEventArgs e)=>Save(true);
}
