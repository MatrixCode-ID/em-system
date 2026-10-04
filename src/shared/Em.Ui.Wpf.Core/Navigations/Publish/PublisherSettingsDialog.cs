using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Em.Ui.Wpf.Publish;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Navigations.Publish;

public sealed class PublisherSettingsDialog : Window {
 public PublisherSettingsDialog(PublisherSettings settings) {
  Title="Publisher settings";Width=900;Height=430;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml",UriKind.Relative)});
  SetResourceReference(BackgroundProperty,"themeWindowBackgroundBrush");SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");
  var panel=new StackPanel {Margin=new Thickness(22)};Content=panel;var inputs=new List<TextBox>();var error=new TextBlock {TextWrapping=TextWrapping.Wrap};error.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");
  foreach(var item in new[]{("Profiles",settings.Profiles,PublisherSettings.DefaultProfiles),("Logs",settings.Logs,PublisherSettings.DefaultLogs),("Work",settings.Work,PublisherSettings.DefaultWork)}) {
   var label=new TextBlock {Text=item.Item1,Margin=new Thickness(0,12,0,6)};label.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");panel.Children.Add(label);
   var row=new DockPanel();panel.Children.Add(row);var field=new TextBox {Text=item.Item2,Margin=new Thickness(0,0,12,0)};field.SetResourceReference(StyleProperty,"fieldBoxStyle");inputs.Add(field);
   foreach(var action in new[]{"Browse","Open folder","Use default"}) {
    var button=new Button {Content=action,Margin=new Thickness(8,0,0,0)};button.SetResourceReference(StyleProperty,"outlinedButtonStyle");DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);
    button.Click+=(_,_)=> {try {if(action=="Browse") {var picker=new OpenFolderDialog();if(picker.ShowDialog()==true)field.Text=picker.FolderName;}else if(action=="Use default")field.Text=item.Item3;else {Directory.CreateDirectory(field.Text);Process.Start(new ProcessStartInfo(field.Text){UseShellExecute=true});}}catch(Exception ex){error.Text=ex.Message;}};
   }row.Children.Add(field);
  }
  panel.Children.Add(error);var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
  var save=new Button {Content="Save",IsDefault=true,Margin=new Thickness(8,16,0,0)};save.SetResourceReference(StyleProperty,"filledButtonStyle");buttons.Children.Add(save);
  save.Click+=(_,_)=> {try {var paths=inputs.Select(i=>Path.GetFullPath(i.Text)).ToArray();foreach(var path in paths)PublishPaths.RejectLinks(path);for(var i=0;i<paths.Length;i++)for(var j=i+1;j<paths.Length;j++) {if(paths[i].Equals(paths[j],StringComparison.OrdinalIgnoreCase)||paths[i].StartsWith(paths[j]+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||paths[j].StartsWith(paths[i]+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Profiles, Logs and Work must be separate directories.");}settings.Profiles=paths[0];settings.Logs=paths[1];settings.Work=paths[2];DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}};
  var cancel=new Button {Content="Cancel",IsCancel=true,Margin=new Thickness(8,16,0,0)};cancel.SetResourceReference(StyleProperty,"outlinedButtonStyle");buttons.Children.Add(cancel);
 }
}
