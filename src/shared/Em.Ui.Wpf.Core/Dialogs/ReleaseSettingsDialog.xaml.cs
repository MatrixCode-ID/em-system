using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Dialogs;

/// <summary>Edits the product JSON, PEM and machine-local build preferences.</summary>
public partial class ReleaseSettingsDialog : EmWindow {
   private readonly ReleaseProfile _draft;
   private readonly ReleaseProfileStore _store;
   private readonly ReleaseManagerPreferences _preferences;
   private readonly EmApp _app;
   private readonly DateTime _lastWrite;
   private readonly IReadOnlyList<ReleaseProfile> _others;
   private readonly Dictionary<string, TextBox> _fields = [];
   private readonly ComboBox _hosts = new();
   private readonly StackPanel _form = new() { Margin = new Thickness(22, 12, 22, 22) };
   private readonly StackPanel _previews = new();
   private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
   private bool _busy;

   /// <summary>Creates a product profile editor.</summary>
   public ReleaseSettingsDialog(EmApp app, ReleaseProfileStore store, ReleaseProfile profile, DateTime lastWriteUtc,
      IReadOnlyList<ReleaseProfile> others, ReleaseSigningSecrets secrets, string? sdkVersion, bool isSdkChecked, bool canUseCdn) {
      InitializeComponent(); _app = app; _store = store; _draft = profile.Clone(); _lastWrite = lastWriteUtc; _others = others;
      _preferences = new ReleaseManagerPreferences(app);
      Title = "Release settings · " + profile.Name; Width = 920; Height = 850; ShowMinimizeButton = false;
      Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml") });
      var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
      Button(toolbar, "Save", Save); Button(toolbar, "Cancel", () => { DialogResult = false; return Task.CompletedTask; }); _form.Children.Add(toolbar);
      Field("Solution", profile.SolutionPath, "The .slnx containing the host.", async () => {
         var picker = new OpenFileDialog { Filter = "Solution|*.slnx" }; if (picker.ShowDialog(this) != true) return;
         _fields["Solution"].Text = picker.FileName; await LoadSolution();
      });
      Button(_form, "Load solution / identity", LoadSolution);
      Row("Host project", _hosts, "Select a host to read its evaluated MSBuild product identity.");
      _hosts.SelectionChanged += async (_, _) => { if (!_busy) await Run(ReadIdentity); };
      Field("Publish folder", profile.PublishFolder, "Prepare empties this folder after confirmation.", () => Browse("Publish folder"));
      Choice("Target", profile.TargetKind.ToString(), new[] { "Cdn", "Folder" }, "CDN writes through the active connection; Folder writes directly.");
      Field("Target folder", profile.TargetFolder, "Parent directory for a Folder release.", () => Browse("Target folder"));
      Field("Release folder", profile.ReleaseFolder, "Empty CDN folders become owned by this app_id. Nested managed folders are forbidden.");
      var cdn = new StackPanel { Orientation = Orientation.Horizontal };
      Button(cdn, "Browse CDN / select managed folder", SelectManaged);
      Button(cdn, "Release owned folder", async () => {
         if (this.ShowMboxDecideWarning("Release this product's managed folder?", Title) == MessageBoxResult.Yes)
            await Service.PostMeta_CdnReleaseUnmanage(Value("Release folder"), Value("app_id"));
      }); _form.Children.Add(cdn);
      Field("CDN address", profile.CdnAddress, "Public release URL for launcher, Compare and Verify. Empty derives from the active host /cdn/<folder>/. Sync uses the active connection.");
      Field("Product", profile.Product.AppName, "Display name only."); Field("Company", profile.Product.Publisher, "Publisher.");
      Field("app_id", profile.Product.AppId, "Two or more alphanumeric segments separated by dots. Use a different id per environment. MatrixCode.EmSystem is reserved.");
      Field("Application exe", profile.Product.AppExe, "AssemblyName plus .exe; no directory.");
      var assets = new StackPanel { Orientation = Orientation.Horizontal };
      Button(assets, "Import ICO", () => ImportAsset(false)); Button(assets, "Import PNG logo", () => ImportAsset(true));
      Button(assets, "Clear logo", () => { _draft.Product.LogoBase64 = ""; Preview(); return Task.CompletedTask; }); _form.Children.Add(assets); _form.Children.Add(_previews);
      Field("Launcher exe", profile.Launcher.FileName, "Filename shared by installer and root runner; must end in .exe.");
      Field("Launcher version", profile.Launcher.BaseVersion, "MAJOR.MINOR.PATCH, independent of the app version. Prerelease counter comes from the target manifest.");
      Choice("Channel", profile.Launcher.Channel, new[] { "prealpha", "alpha", "beta", "rc", "release" }, "Used release versions cannot be reused for changed inputs.");
      Choice("Update at start", profile.Launcher.UpdateBehavior, new[] { "automatic", "ask" }, "automatic installs verified updates; ask offers Update now / Later.");
      Field("Launcher source folder", _preferences.LauncherSourceFolder, "Machine-local Rust source with Cargo.toml and build.rs; required only when rebuilding.", () => Browse("Launcher source folder"));
      Field("Launcher output folder", _preferences.LauncherOutputFolder, "Machine-local build destination or IT share. Empty uses Documents/Em/ReleaseManager/Launchers/<app_id>/.", () => Browse("Launcher output folder"));
      var keys = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
      Button(keys, "Create signing PEM", () => {
         if (_draft.PrivateKeyPem.Length == 0 || this.ShowMboxDecideWarning("Replace the product key? Existing installations reject a different key for the same app_id.", Title) == MessageBoxResult.Yes) _draft.PrivateKeyPem = ReleasePemKey.Create();
         KeySummary(); return Task.CompletedTask;
      });
      Button(keys, "Import private PEM", () => {
         var picker = new OpenFileDialog { Filter = "Private PEM|*.pem" }; if (picker.ShowDialog(this) == true) { using var key = ReleasePemKey.Open(File.ReadAllText(picker.FileName)); _draft.PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(); KeySummary(); } return Task.CompletedTask;
      });
      Button(keys, "Export public PEM", () => {
         var picker = new SaveFileDialog { Filter = "Public PEM|*.pem" }; if (picker.ShowDialog(this) == true) File.WriteAllText(picker.FileName, ReleasePemKey.Public(_draft.PrivateKeyPem)); return Task.CompletedTask;
      }); _form.Children.Add(keys); _form.Children.Add(_message); KeySummary(); Preview();
      Content = new ScrollViewer { Content = _form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
      if (File.Exists(profile.SolutionPath)) { _busy = true; try { _hosts.ItemsSource = ReleaseBuilder.ReadHostProjects(profile.SolutionPath); _hosts.SelectedItem = profile.HostProject; } finally { _busy = false; } }
   }
   private ICdnReleaseServices Service => _app.ServiceProvider.GetRequiredService<ICdnReleaseServices>();
   private string Value(string name) => _fields[name].Text.Trim();
   private void Row(string label, FrameworkElement input, string help) {
      var row = new Grid { Margin = new Thickness(0, 8, 0, 0), ToolTip = help }; ToolTipService.SetShowOnDisabled(row, true);
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) }); row.ColumnDefinitions.Add(new ColumnDefinition());
      row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(input, 1); row.Children.Add(input); _form.Children.Add(row);
   }
   private void Field(string name, string text, string help, Func<Task>? browse = null) {
      var box = new TextBox { Text = text }; box.SetResourceReference(StyleProperty, "fieldBoxStyle"); _fields[name] = box;
      var panel = new DockPanel(); if (browse is not null) { var buttons = new StackPanel(); Button(buttons, "Browse", browse); DockPanel.SetDock(buttons, Dock.Right); panel.Children.Add(buttons); }
      panel.Children.Add(box); Row(name, panel, help);
   }
   private void Choice(string name, string value, string[] choices, string help) {
      var box = new TextBox { Text = value }; _fields[name] = box;
      var combo = new ComboBox { ItemsSource = choices, SelectedItem = value };
      combo.SelectionChanged += (_, _) => box.Text = combo.SelectedItem as string ?? ""; Row(name, combo, help);
   }
   private void Button(Panel panel, string text, Func<Task> action) {
      var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) }; button.SetResourceReference(StyleProperty, "outlinedButtonStyle");
      button.Click += async (_, _) => await Run(action); panel.Children.Add(button);
   }
   private async Task Run(Func<Task> action) {
      if (_busy) return; _busy = true; _form.IsEnabled = false;
      try { await action(); } catch (Exception ex) { _message.Text = ex.Message; } finally { _busy = false; _form.IsEnabled = true; }
   }
   private Task Browse(string name) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) _fields[name].Text = dialog.FolderName; return Task.CompletedTask; }
   private async Task LoadSolution() { _hosts.ItemsSource = ReleaseBuilder.ReadHostProjects(Value("Solution")); _hosts.SelectedIndex = 0; await ReadIdentity(); }
   private ReleaseProduct Product() => new() { AppName = Value("Product"), Publisher = Value("Company"), AppId = Value("app_id"), AppExe = Value("Application exe"), IconBase64 = _draft.Product.IconBase64, LogoBase64 = _draft.Product.LogoBase64, IsEmHost = _draft.Product.IsEmHost };
   private async Task ReadIdentity() {
      if (_hosts.SelectedItem is not string host) return;
      var identity = await ReleaseProduct.ReadAsync(Path.GetFullPath(host, Path.GetDirectoryName(Value("Solution"))!), CancellationToken.None);
      var old = Product();
      string Hash(string value) => value.Length == 0 ? "(empty)" : Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(value)))[..16];
      var changes = new[] { ("Product", old.AppName, identity.AppName), ("Company", old.Publisher, identity.Publisher), ("app_id", old.AppId, identity.AppId), ("Application exe", old.AppExe, identity.AppExe), ("Icon hash", Hash(old.IconBase64), Hash(identity.IconBase64)), ("Logo hash", Hash(old.LogoBase64), Hash(identity.LogoBase64)) }
         .Where(v => v.Item2 != v.Item3).Select(v => $"{v.Item1}: profile '{v.Item2}' → solution '{v.Item3}'").ToArray();
      if (changes.Length == 0 || old.AppId.Length == 0 || this.ShowMboxDecideWarning(string.Join("\n", changes) + "\n\nUse solution identity? No keeps the profile.", Title) == MessageBoxResult.Yes) {
         _draft.Product = identity; _fields["Product"].Text = identity.AppName; _fields["Company"].Text = identity.Publisher; _fields["app_id"].Text = identity.AppId; _fields["Application exe"].Text = identity.AppExe;
      }
      _draft.Product.IsEmHost = identity.IsEmHost; Preview();
   }
   private Task ImportAsset(bool logo) {
      var picker = new OpenFileDialog { Filter = logo ? "PNG|*.png" : "ICO|*.ico" };
      if (picker.ShowDialog(this) == true) { var data = Convert.ToBase64String(File.ReadAllBytes(picker.FileName)); if (logo) { ReleaseProduct.ReadLogo(data); _draft.Product.LogoBase64 = data; } else _draft.Product.IconBase64 = data; Preview(); } return Task.CompletedTask;
   }
   private void Preview() {
      _previews.Children.Clear(); BitmapSource? image = null;
      if (_draft.Product.LogoBase64.Length > 0) image = ReleaseProduct.ReadLogo(_draft.Product.LogoBase64);
      else if (_draft.Product.IconBase64.Length > 0) { using var stream = new MemoryStream(Convert.FromBase64String(_draft.Product.IconBase64)); image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); }
      if (image is not null) {
         foreach (var scale in new[] { 1d, 2d }) { _previews.Children.Add(new TextBlock { Text = $"Logo {scale * 100:0}%" }); _previews.Children.Add(new Image { Source = image, Width = 240 * scale, Height = 48 * scale, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left }); }
         var factor = Math.Min(240d / image.PixelWidth, 48d / image.PixelHeight);
         if (Math.Min(image.PixelWidth, image.PixelHeight) * factor < 24 || factor * 2 > 1) _previews.Children.Add(new TextBlock { Text = "Warning: logo is smaller than 24 px on one side or has insufficient resolution for 200%.", TextWrapping = TextWrapping.Wrap });
      }
      _previews.Children.Add(new TextBlock { Text = _draft.Product.IsEmHost ? "Em host: full branding and in-app update support." : "Other .NET host: basic launcher support; in-app updates unavailable." });
   }
   private void KeySummary() => _message.Text = _draft.PrivateKeyPem.Length > 0 ? "Signing PEM present. Anyone holding this JSON can sign releases." : "Create or import the product's P-256 PKCS#8 private PEM.";
   private async Task SelectManaged() {
      var folder = "";
      while (true) {
         var content = await Service.GetMeta_CdnReleaseFolder(folder);
         var dialog = new TextInputDialog("Choose CDN folder", "Folders: " + string.Join(", ", content.Entries.Where(e => e.IsFolder).Select(e => e.Name)) + "\nType a full relative path to browse. Repeat that path to select it; a missing path creates a new folder.", "Folder path", "Browse / select", initialText: folder.Length == 0 ? Value("Release folder") : folder) { Owner = this };
         if (dialog.ShowDialog() != true) return;
         var path = dialog.Vm.Result;
         if (path == folder || !content.Entries.Any(e => e.IsFolder && e.Path == path)) { await Service.PostGetMeta_CdnReleaseManage(path, Value("app_id")); _fields["Release folder"].Text = path; return; }
         folder = path;
      }
   }
   private async Task Save() {
      _draft.Product = Product(); _draft.Product.Validate();
      _draft.SolutionPath = Value("Solution"); _draft.HostProject = _hosts.SelectedItem as string ?? ""; _draft.PublishFolder = Value("Publish folder");
      _draft.TargetKind = Enum.Parse<ReleaseTargetKind>(Value("Target")); _draft.TargetFolder = Value("Target folder"); _draft.ReleaseFolder = Value("Release folder"); _draft.CdnAddress = Value("CDN address");
      if (_draft.CdnAddress.Length > 0 && (!Uri.TryCreate(_draft.CdnAddress, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) throw new InvalidDataException("CDN address must be an absolute HTTP(S) URL.");
      _draft.Launcher.FileName = Value("Launcher exe"); _draft.Launcher.BaseVersion = Value("Launcher version"); _draft.Launcher.Channel = Value("Channel"); _draft.Launcher.UpdateBehavior = Value("Update at start"); LauncherBuild.ValidateSettings(_draft.Launcher);
      var conflicts = ReleaseProfileConflicts.Find(_draft, _others, true, true);
      if (conflicts.Count > 0 && this.ShowMboxDecideWarning(string.Join("\n", conflicts) + "\nContinue?", Title) != MessageBoxResult.Yes) return;
      if (_draft.TargetKind == ReleaseTargetKind.Cdn) await Service.PostGetMeta_CdnReleaseManage(_draft.ReleaseFolder, _draft.Product.AppId);
      try { _store.Save(_draft, _lastWrite); } catch (ReleaseProfileChangedException) { if (this.ShowMboxDecideWarning("Profile JSON changed outside this editor. Overwrite it? No keeps editing.", Title) != MessageBoxResult.Yes) return; _store.Save(_draft); }
      _preferences.LauncherSourceFolder = Value("Launcher source folder"); _preferences.LauncherOutputFolder = Value("Launcher output folder"); DialogResult = true;
   }
}
