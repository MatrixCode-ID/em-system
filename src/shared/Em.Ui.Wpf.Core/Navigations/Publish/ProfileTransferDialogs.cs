using Em.Ui.Wpf.Windows;
using System.Windows;
using System.Windows.Controls;
using Em.Ui.Wpf.Publish;

namespace Em.Ui.Wpf.Navigations.Publish;

/// <summary>
/// The choice when exporting a profile: without secrets, or with secrets as an encrypted file
/// (passphrase) or as plain text. Built in code, following <see cref="PublisherSettingsDialog"/>.
/// </summary>
public sealed class ExportProfileDialog : EmWindow {
 private readonly CheckBox _include=new() {Content="Include sensitive data (passwords and tokens)"};
 private readonly RadioButton _encrypted=new() {Content="Encrypted with a passphrase (recommended)",IsChecked=true,GroupName="mode",Margin=new Thickness(0,8,0,0)};
 private readonly RadioButton _plain=new() {Content="Plain text - anyone who opens the file can read the secrets",GroupName="mode",Margin=new Thickness(0,8,0,0)};
 private readonly PasswordBox _passphrase=new();
 private readonly PasswordBox _confirm=new();
 private readonly TextBlock _error=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
 private readonly StackPanel _options=new() {Margin=new Thickness(24,0,0,0),Visibility=Visibility.Collapsed};
 private readonly StackPanel _passphraseRows=new();

 /// <summary>Whether the secrets are exported, and how the file is protected.</summary>
 public ExportSecrets Secrets { get; private set; }
 /// <summary>The passphrase for mode <see cref="ExportSecrets.Encrypted"/>.</summary>
 public string? Passphrase { get; private set; }

 /// <summary>Creates a new instance of <see cref="ExportProfileDialog"/>.</summary>
 public ExportProfileDialog(string profileName) {
  Title="Export profile";Width=560;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
  Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml",UriKind.Relative)});
  ShowMinimizeButton=false;
  var panel=new StackPanel {Margin=new Thickness(22)};Content=panel;
  panel.Children.Add(Label($"Export '{profileName}'. Without sensitive data the file has no passwords, and on the other computer they must be entered again.",new Thickness(0,0,0,12)));
  panel.Children.Add(_include);panel.Children.Add(_options);
  _options.Children.Add(_encrypted);_options.Children.Add(_plain);
  _passphraseRows.Margin=new Thickness(24,10,0,0);_options.Children.Add(_passphraseRows);
  _passphraseRows.Children.Add(Label("Passphrase (at least "+ProfileBundle.MinPassphraseLength+" characters)",new Thickness(0,0,0,4)));
  _passphrase.SetResourceReference(StyleProperty,"fieldPasswordStyle");_passphraseRows.Children.Add(_passphrase);
  _passphraseRows.Children.Add(Label("Confirm passphrase",new Thickness(0,10,0,4)));
  _confirm.SetResourceReference(StyleProperty,"fieldPasswordStyle");_passphraseRows.Children.Add(_confirm);
  _passphraseRows.Children.Add(Label("The passphrase is not stored and cannot be recovered. The encrypted file is only readable with it, on any computer.",new Thickness(0,8,0,0)));
  _error.SetResourceReference(ForegroundProperty,"dangerBrush");panel.Children.Add(_error);

  var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
  var export=new Button {Content="Export",IsDefault=true,Margin=new Thickness(8,16,0,0)};export.SetResourceReference(StyleProperty,"filledButtonStyle");buttons.Children.Add(export);
  var cancel=new Button {Content="Cancel",IsCancel=true,Margin=new Thickness(8,16,0,0)};cancel.SetResourceReference(StyleProperty,"outlinedButtonStyle");buttons.Children.Add(cancel);

  _include.Checked+=(_,_)=>Refresh();_include.Unchecked+=(_,_)=>Refresh();_encrypted.Checked+=(_,_)=>Refresh();_plain.Checked+=(_,_)=>Refresh();
  export.Click+=(_,_)=> {
   if(_include.IsChecked!=true) {Secrets=ExportSecrets.None;DialogResult=true;return;}
   if(_plain.IsChecked==true) {Secrets=ExportSecrets.PlainText;DialogResult=true;return;}
   if(_passphrase.Password.Length<ProfileBundle.MinPassphraseLength) {_error.Text=$"The passphrase must be at least {ProfileBundle.MinPassphraseLength} characters.";return;}
   if(_passphrase.Password!=_confirm.Password) {_error.Text="The passphrases do not match.";return;}
   Secrets=ExportSecrets.Encrypted;Passphrase=_passphrase.Password;DialogResult=true;
  };
 }

 private void Refresh() {
  _options.Visibility=_include.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
  _passphraseRows.Visibility=_encrypted.IsChecked==true?Visibility.Visible:Visibility.Collapsed;_error.Text="";
 }

 private TextBlock Label(string text,Thickness margin) {
  var label=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=margin};label.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");return label;
 }
}

/// <summary>Asks for a passphrase to open an encrypted profile file.</summary>
public sealed class PassphraseDialog : EmWindow {
 /// <summary>The passphrase typed by the user.</summary>
 public string Passphrase { get; private set; }="";

 /// <summary>Creates a new instance of <see cref="PassphraseDialog"/>.</summary>
 public PassphraseDialog(string fileName) {
  Title="Encrypted profile";Width=480;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;
  Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml",UriKind.Relative)});
  ShowMinimizeButton=false;
  var panel=new StackPanel {Margin=new Thickness(22)};Content=panel;
  var label=new TextBlock {Text=$"'{fileName}' is encrypted. Enter the passphrase used when it was exported.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)};
  label.SetResourceReference(ForegroundProperty,"themeWindowForegroundBrush");panel.Children.Add(label);
  var field=new PasswordBox();field.SetResourceReference(StyleProperty,"fieldPasswordStyle");panel.Children.Add(field);
  var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
  var ok=new Button {Content="Import",IsDefault=true,Margin=new Thickness(8,16,0,0)};ok.SetResourceReference(StyleProperty,"filledButtonStyle");buttons.Children.Add(ok);
  var cancel=new Button {Content="Cancel",IsCancel=true,Margin=new Thickness(8,16,0,0)};cancel.SetResourceReference(StyleProperty,"outlinedButtonStyle");buttons.Children.Add(cancel);
  ok.Click+=(_,_)=> {Passphrase=field.Password;DialogResult=true;};
  Loaded+=(_,_)=>field.Focus();
 }
}
