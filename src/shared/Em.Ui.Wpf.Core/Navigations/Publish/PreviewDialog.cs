using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Em.Ui.Wpf.Windows;
using FontAwesome6;
using FontAwesome6.Fonts;

namespace Em.Ui.Wpf.Navigations.Publish;

/// <summary>One entry of a <see cref="PreviewDialog"/>: a title, a detail line, and an optional muted line such as a path.</summary>
public sealed record PreviewRow(string Title,string Detail,string? Meta=null);

/// <summary>
/// Read-only information from the profile editor (project information, generated Dockerfile, file set): a title, an
/// intro, and either rows or a block of monospaced text in a card. Nothing in it is editable; Copy puts the whole
/// content on the clipboard.
/// </summary>
public sealed class PreviewDialog : EmWindow {
 private readonly string _copyText;

 private PreviewDialog(string title,string intro,FrameworkElement body,string copyText) {
  _copyText=copyText;
  Title=title;Width=760;SizeToContent=SizeToContent.Height;MinHeight=240;MaxHeight=680;ResizeMode=ResizeMode.CanResize;ShowMinimizeButton=false;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml",UriKind.Relative)});

  var root=new DockPanel {Margin=new Thickness(22,16,22,16)};Content=root;

  var heading=new TextBlock {Text=title,FontSize=15,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
  heading.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
  if(intro.Length>0) {var block=Muted(intro);block.Margin=new Thickness(0,4,0,0);DockPanel.SetDock(block,Dock.Top);root.Children.Add(block);}

  var actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
  var copy=new Button {Content=Labelled(EFontAwesomeIcon.Solid_Copy,"Copy"),ToolTip="Copy the content to the clipboard"};
  copy.SetResourceReference(StyleProperty,"textButtonStyle");AutomationProperties.SetName(copy,"Copy");copy.Click+=(_,_)=>Copy();actions.Children.Add(copy);
  var close=new Button {Content="Close",IsCancel=true,IsDefault=true,MinWidth=96,Margin=new Thickness(8,0,0,0)};
  close.SetResourceReference(StyleProperty,"outlinedButtonStyle");actions.Children.Add(close);
  DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);

  var card=new Border {Margin=new Thickness(0,12,0,0),ClipToBounds=true,Child=body};card.SetResourceReference(StyleProperty,"innerCardStyle");
  root.Children.Add(card);
 }

 /// <summary>Shows <paramref name="rows"/> as a list, one entry per row with a divider between them.</summary>
 public static PreviewDialog List(string title,string intro,IReadOnlyList<PreviewRow> rows) {
  var list=new StackPanel();
  for(var i=0;i<rows.Count;i++)list.Children.Add(Row(rows[i],i==rows.Count-1));
  if(rows.Count==0)list.Children.Add(new Border {Padding=new Thickness(18,14,18,14),Child=Muted("Nothing to show.")});
  var scroll=new ScrollViewer {FocusVisualStyle=null,Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  var text=string.Join("\n",rows.Select(r=>string.Join("\n",new[] {r.Title,r.Detail,r.Meta}.Where(x=>!string.IsNullOrEmpty(x)))));
  return new PreviewDialog(title,intro,scroll,text);
 }

 /// <summary>Shows <paramref name="text"/> as monospaced, unwrapped text, for generated files and file lists.</summary>
 public static PreviewDialog Code(string title,string intro,string text) {
  var block=new TextBlock {Text=text,FontFamily=new System.Windows.Media.FontFamily("Consolas"),FontSize=12.5,Margin=new Thickness(18,14,18,14)};
  block.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");AutomationProperties.SetName(block,title);
  var scroll=new ScrollViewer {FocusVisualStyle=null,Content=block,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
  return new PreviewDialog(title,intro,scroll,text);
 }

 private void Copy() {
  try {Clipboard.SetText(_copyText);}catch(System.Runtime.InteropServices.ExternalException) {}
 }

 private static FrameworkElement Row(PreviewRow row,bool last) {
  var text=new StackPanel {Margin=new Thickness(18,12,18,12)};
  var name=new TextBlock {Text=row.Title,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis};
  name.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");text.Children.Add(name);
  if(row.Detail.Length>0) {var detail=new TextBlock {Text=row.Detail,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)};detail.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");text.Children.Add(detail);}
  if(!string.IsNullOrEmpty(row.Meta)) {var meta=Muted(row.Meta);meta.Margin=new Thickness(0,2,0,0);text.Children.Add(meta);}
  if(last)return text;
  var divider=new Border {Height=1};divider.SetResourceReference(BackgroundProperty,"dividerBrush");
  return new StackPanel {Children={text,divider}};
 }

 private static StackPanel Labelled(EFontAwesomeIcon icon,string text) {
  var mark=new FontAwesome {Icon=icon};mark.SetResourceReference(StyleProperty,"buttonIconStyle");
  return new StackPanel {Orientation=Orientation.Horizontal,Children={mark,new TextBlock {Text=text,VerticalAlignment=VerticalAlignment.Center}}};
 }

 private static TextBlock Muted(string text) {var block=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap};block.SetResourceReference(StyleProperty,"fieldHelpStyle");return block;}
}
