using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Em.Ui.Wpf.Publish;
using Em.Ui.Wpf.Windows;
using FontAwesome6;
using FontAwesome6.Fonts;

namespace Em.Ui.Wpf.Navigations.Publish;

/// <summary>
/// Read-only view of the local tools a publish profile needs (.NET SDK, Docker, Compose, Buildx): one row per tool with
/// its version or the repair hint. Checks once on open; the refresh button checks again.
/// </summary>
public sealed class ToolsDialog : EmWindow {
 private readonly PublishProfile _profile;
 private readonly StackPanel _rows=new();
 private readonly TextBlock _status=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};
 private readonly Button _refresh;
 private bool _busy;

 public ToolsDialog(PublishProfile profile) {
  _profile=profile;
  Title="Tools";Width=620;SizeToContent=SizeToContent.Height;MinHeight=240;ResizeMode=ResizeMode.NoResize;ShowMinimizeButton=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml",UriKind.Relative)});

  var root=new DockPanel {Margin=new Thickness(22,16,22,16)};Content=root;

  var header=new DockPanel {Margin=new Thickness(0,0,0,4)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
  _refresh=new Button {Content=new FontAwesome {Icon=EFontAwesomeIcon.Solid_ArrowsRotate},ToolTip="Check the tools again",VerticalAlignment=VerticalAlignment.Center};
  _refresh.SetResourceReference(StyleProperty,"rowActionButtonStyle");AutomationProperties.SetName(_refresh,"Check the tools again");_refresh.Click+=async(_,_)=>await Check();
  DockPanel.SetDock(_refresh,Dock.Right);header.Children.Add(_refresh);
  var title=new TextBlock {Text=profile.Name.Length>0?profile.Name:"Publish profile",FontSize=15,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
  title.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");header.Children.Add(title);

  var intro=Muted(profile.Kind==PublishKind.NuGet
   ?"Local tools this NuGet profile needs on this computer."
   :"Local tools this container profile (Mode "+profile.Container!.Mode+") needs on this computer.");
  DockPanel.SetDock(intro,Dock.Top);root.Children.Add(intro);

  var close=new Button {Content="Close",IsCancel=true,IsDefault=true,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
  close.SetResourceReference(StyleProperty,"outlinedButtonStyle");DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);
  DockPanel.SetDock(_status,Dock.Bottom);_status.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");root.Children.Add(_status);

  _rows.Margin=new Thickness(0,12,0,0);root.Children.Add(_rows);
  Loaded+=async(_,_)=>await Check();
 }

 private async Task Check() {
  if(_busy)return;_busy=true;_refresh.IsEnabled=false;_status.Text="Checking…";
  try {
   var results=await new PublishProcessRunner().CheckToolsDetailed(_profile,CancellationToken.None);
   _rows.Children.Clear();
   foreach(var result in results)_rows.Children.Add(Row(result));
   var failed=results.Count(r=>!r.Ok);
   _status.Text=results.Length==0?"This profile needs no local tools.":failed==0?"All tools are ready.":failed+" tool(s) need attention before Check and Prepare can run.";
  } catch(Exception ex) {_status.Text=ex.Message;}
  finally {_busy=false;_refresh.IsEnabled=true;}
 }

 /// <summary>One tool: a status mark, the name with its version or repair hint, and why the profile needs it.</summary>
 private static FrameworkElement Row(ToolCheck check) {
  var row=new DockPanel {Margin=new Thickness(0,0,0,12)};
  var mark=new FontAwesome {Icon=check.Ok?EFontAwesomeIcon.Solid_CircleCheck:EFontAwesomeIcon.Solid_CircleXmark,FontSize=16,Margin=new Thickness(0,1,12,0),VerticalAlignment=VerticalAlignment.Top};
  mark.SetResourceReference(ForegroundProperty,check.Ok?"themeAccentBrush":"themeErrorBrush");
  AutomationProperties.SetName(mark,check.Ok?"Ready":"Needs attention");DockPanel.SetDock(mark,Dock.Left);row.Children.Add(mark);
  var text=new StackPanel();row.Children.Add(text);
  var name=new TextBlock {Text=check.Name,FontWeight=FontWeights.SemiBold};name.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");text.Children.Add(name);
  var detail=new TextBlock {Text=check.Detail,TextWrapping=TextWrapping.Wrap};detail.SetResourceReference(ForegroundProperty,check.Ok?"themeWindowForegroundBrush":"themeErrorBrush");text.Children.Add(detail);
  text.Children.Add(Muted(check.Purpose));
  return row;
 }

 private static TextBlock Muted(string text) {var block=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap};block.SetResourceReference(StyleProperty,"fieldHelpStyle");return block;}
}
