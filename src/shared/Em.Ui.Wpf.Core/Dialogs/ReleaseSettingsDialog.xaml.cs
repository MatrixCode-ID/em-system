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
   // Save/Cancel stay above the scrolling form, so they never scroll out of view.
   private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 12, 22, 0) };
   private readonly StackPanel _previews = new();
   private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
   // Shown above the form while an action runs: what is being done right now, since the form is
   // disabled meanwhile and reading a solution can take a while.
   private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap };
   private readonly ProgressBar _statusProgress = new() { IsIndeterminate = true };
   private readonly StackPanel _status = new() { Margin = new Thickness(22, 12, 22, 0), Visibility = Visibility.Collapsed };
   private bool _busy;
   private readonly ReleaseAndroidSettings _android = new();
   private readonly List<UIElement> _windows = [];

   /// <summary>Creates a product profile editor.</summary>
   public ReleaseSettingsDialog(EmApp app, ReleaseProfileStore store, ReleaseProfile profile, DateTime lastWriteUtc,
      IReadOnlyList<ReleaseProfile> others, string? sdkVersion, bool isSdkChecked, bool canUseCdn) {
      InitializeComponent(); _app = app; _store = store; _draft = profile.Clone(); _lastWrite = lastWriteUtc; _others = others;
      _preferences = new ReleaseManagerPreferences(app);
      _android.Vm.Profile = _draft; _android.Vm.Owner = this; _android.Vm.SelectedPlatform = _draft.Platform;
      _android.Vm.Sdk = _preferences.AndroidSdkDirectory; _android.Vm.Java = _preferences.JavaSdkDirectory;
      _android.Vm.PlatformChanged = () => { foreach (var element in _windows) element.IsEnabled = _draft.Platform == ReleasePlatform.Windows; _hosts.ItemsSource = null; _android.Vm.Refresh(); };
      _form.Children.Add(_android);
      Title = "Release settings · " + profile.Name; Width = 920; Height = 850; ShowMinimizeButton = false;
      Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Em.Ui.Wpf.Core;component/Styles/MaterialDesign.xaml") });
      _hosts.SetResourceReference(StyleProperty, "fieldComboStyle");
      Button(_toolbar, "Save", Save); Button(_toolbar, "Cancel", () => { DialogResult = false; return Task.CompletedTask; });
      Field("Solution", profile.SolutionPath, "The .slnx containing the host.", async () => {
         var picker = new OpenFileDialog { Filter = "Solution|*.slnx" }; if (picker.ShowDialog(this) != true) return;
         _fields["Solution"].Text = picker.FileName; await LoadSolution();
      });
      Button(_form, "Check solution", LoadSolution);
      Row("Host project", _hosts, "Select a host to read its evaluated MSBuild product identity. Check solution lists the hosts again.");
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
      Field("CDN address", profile.CdnAddress, "Public CDN base address only, e.g. https://host/cdn/. The release folder is added to it (https://host/cdn/<release folder>/) for launcher, Compare and Verify. Empty derives from the active host /cdn/. Sync uses the active connection.");
      Field("Product", profile.Product.AppName, "Display name only."); Field("Company", profile.Product.Publisher, "Publisher.");
      Field("app_id", profile.Product.AppId, "Two or more alphanumeric segments separated by dots. Use a different id per environment. MatrixCode.EmSystem is reserved.");
      var windowsStart = _form.Children.Count;
      Field("Application exe", profile.Product.AppExe, "AssemblyName plus .exe; no directory.");
      Row("Branding", _previews, "Application icon (ICO) and launcher logo (transparent PNG) of the product.", labelOnTop: true);
      Field("Launcher exe", profile.Launcher.FileName, "Filename shared by installer and root runner; must end in .exe.");
      Field("Launcher version", profile.Launcher.BaseVersion, "MAJOR.MINOR.PATCH, independent of the app version. Prerelease counter comes from the target manifest.");
      Choice("Channel", profile.Launcher.Channel, new[] { "prealpha", "alpha", "beta", "rc", "release" }, "Used release versions cannot be reused for changed inputs.");
      Choice("Update at start", profile.Launcher.UpdateBehavior, new[] { "automatic", "ask" }, "automatic installs verified updates; ask offers Update now / Later.");
      Field("Launcher source folder", _preferences.LauncherSourceFolder, "Machine-local Rust source with Cargo.toml and build.rs; required only when rebuilding.", () => Browse("Launcher source folder"));
      Field("Launcher output folder", _preferences.LauncherOutputFolder, "Machine-local build destination or IT share. Empty uses Documents/Em/ReleaseManager/Launchers/<app_id>/.", () => Browse("Launcher output folder"));
      for (var index = windowsStart; index < _form.Children.Count; index++) { _windows.Add(_form.Children[index]); _form.Children[index].IsEnabled = _draft.Platform == ReleasePlatform.Windows; }
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
      _statusProgress.SetResourceReference(StyleProperty, "hairlineProgressStyle");
      _status.Children.Add(_statusText); _status.Children.Add(_statusProgress);
      var root = new DockPanel(); DockPanel.SetDock(_toolbar, Dock.Top); root.Children.Add(_toolbar);
      DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
      root.Children.Add(new ScrollViewer { Content = _form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
      Content = root;
      // Reading the hosts runs MSBuild per project and takes long on a large solution, so opening the
      // dialog shows only the saved host; Browse or Check solution reads the solution again.
      if (profile.HostProject.Length > 0) {
         _busy = true; try { _hosts.ItemsSource = new[] { profile.HostProject }; _hosts.SelectedIndex = 0; } finally { _busy = false; }
      }
   }
   private ICdnReleaseServices Service => _app.ServiceProvider.GetRequiredService<ICdnReleaseServices>();
   private string Value(string name) => _fields[name].Text.Trim();
   private void Row(string label, FrameworkElement input, string help, bool labelOnTop = false) {
      var row = new Grid { Margin = new Thickness(0, 8, 0, 0), ToolTip = help }; ToolTipService.SetShowOnDisabled(row, true);
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) }); row.ColumnDefinitions.Add(new ColumnDefinition());
      row.Children.Add(new TextBlock { Text = label, VerticalAlignment = labelOnTop ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = labelOnTop ? new Thickness(0, 14, 0, 0) : new Thickness() }); Grid.SetColumn(input, 1); row.Children.Add(input); _form.Children.Add(row);
   }
   private void Field(string name, string text, string help, Func<Task>? browse = null) {
      var box = new TextBox { Text = text }; box.SetResourceReference(StyleProperty, "fieldBoxStyle"); _fields[name] = box;
      var panel = new DockPanel(); if (browse is not null) { var buttons = new StackPanel(); Button(buttons, "Browse", browse); DockPanel.SetDock(buttons, Dock.Right); panel.Children.Add(buttons); }
      panel.Children.Add(box); Row(name, panel, help);
   }
   private void Choice(string name, string value, string[] choices, string help) {
      var box = new TextBox { Text = value }; _fields[name] = box;
      var combo = new ComboBox { ItemsSource = choices, SelectedItem = value };
      combo.SetResourceReference(StyleProperty, "fieldComboStyle");
      combo.SelectionChanged += (_, _) => box.Text = combo.SelectedItem as string ?? ""; Row(name, combo, help);
   }
   private void Button(Panel panel, string text, Func<Task> action) {
      var content = new StackPanel { Orientation = Orientation.Horizontal };
      var icon = new FontAwesome6.Fonts.FontAwesome { Icon = text switch {
         "Save" => FontAwesome6.EFontAwesomeIcon.Solid_FloppyDisk,
         "Cancel" => FontAwesome6.EFontAwesomeIcon.Solid_Xmark,
         "Create signing PEM" => FontAwesome6.EFontAwesomeIcon.Solid_Key,
         "Release owned folder" => FontAwesome6.EFontAwesomeIcon.Solid_Unlock,
         "Check solution" => FontAwesome6.EFontAwesomeIcon.Solid_ListCheck,
         "Import ICO" or "Import PNG logo" => FontAwesome6.EFontAwesomeIcon.Solid_FileImport,
         "Clear logo" => FontAwesome6.EFontAwesomeIcon.Solid_Eraser,
         _ => FontAwesome6.EFontAwesomeIcon.Solid_FolderOpen
      } };
      icon.SetResourceReference(StyleProperty, "buttonIconStyle"); content.Children.Add(icon);
      content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
      var style = text switch { "Save" => "filledButtonStyle", "Import ICO" or "Import PNG logo" or "Clear logo" => "compactOutlinedButtonStyle", _ => "outlinedButtonStyle" };
      var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) }; button.SetResourceReference(StyleProperty, style);
      button.Click += async (_, _) => await Run(action); panel.Children.Add(button);
   }
   private async Task Run(Func<Task> action) {
      if (_busy) return; _busy = true; _form.IsEnabled = false; _toolbar.IsEnabled = false;
      // A long step announces itself through ShowStatus; quick buttons leave the line hidden.
      try { await action(); _status.Visibility = Visibility.Collapsed; }
      catch (Exception ex) { _message.Text = ex.Message; ShowStatus("Failed: " + ex.Message, false); }
      finally { _busy = false; _form.IsEnabled = true; _toolbar.IsEnabled = true; }
   }
   private void ShowStatus(string text, bool running = true) {
      _statusText.Text = text; _statusProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed; _status.Visibility = Visibility.Visible;
   }
   private Task<IReadOnlyList<string>> ReadHostProjects(string solution) {
      ShowStatus($"Reading host projects from {Path.GetFileName(solution)}…");
      // Progress reports are posted to the UI thread and may land after the read finished; drop those.
      var done = false;
      var progress = new Progress<string>(text => { if (!done) ShowStatus(text); });
      return Task.Run(() => ReleaseBuilder.ReadHostProjects(solution, progress, CancellationToken.None, _draft.Platform))
         .ContinueWith(t => { done = true; return t; }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
   }
   private Task Browse(string name) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) _fields[name].Text = dialog.FolderName; return Task.CompletedTask; }
   private async Task LoadSolution() {
      var hosts = await ReadHostProjects(Value("Solution")); _hosts.ItemsSource = hosts; _hosts.SelectedIndex = 0;
      if (hosts.Count == 0) throw new InvalidDataException($"No application project (OutputType Exe or WinExe) found in {Path.GetFileName(Value("Solution"))}.");
      await ReadIdentity();
   }
   private ReleaseProduct Product() {
      var product = _draft.Product;
      product.AppName = Value("Product"); product.Publisher = Value("Company"); product.AppId = Value("app_id"); product.AppExe = Value("Application exe");
      return product;
   }
   private async Task ReadIdentity() {
      if (_hosts.SelectedItem is not string host) return;
      ShowStatus($"Reading product identity of {Path.GetFileName(host)} with MSBuild…");
      var identity = await ReleaseProduct.ReadAsync(Path.GetFullPath(host, Path.GetDirectoryName(Value("Solution"))!), CancellationToken.None, _draft.Platform);
      var old = Product();
      string Hash(string value) => value.Length == 0 ? "(empty)" : Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(value)))[..16];
      var changes = new[] { ("Product", old.AppName, identity.AppName), ("Company", old.Publisher, identity.Publisher), ("app_id", old.AppId, identity.AppId), ("Application exe", old.AppExe, identity.AppExe), ("Icon hash", Hash(old.IconBase64), Hash(identity.IconBase64)), ("Logo hash", Hash(old.LogoBase64), Hash(identity.LogoBase64)) }
         .Where(v => v.Item2 != v.Item3).Select(v => $"{v.Item1}: profile '{v.Item2}' → solution '{v.Item3}'").ToArray();
      if (changes.Length == 0 || old.AppId.Length == 0 || this.ShowMboxDecideWarning(string.Join("\n", changes) + "\n\nUse solution identity? No keeps the profile.", Title) == MessageBoxResult.Yes) {
         _draft.Product = identity; _fields["Product"].Text = identity.AppName; _fields["Company"].Text = identity.Publisher; _fields["app_id"].Text = identity.AppId; _fields["Application exe"].Text = identity.AppExe;
      }
      _draft.Product.IsEmHost = identity.IsEmHost;
      if (_draft.Platform == ReleasePlatform.Android) {
         _draft.Product.Version = identity.Version; _draft.Product.ApplicationVersion = identity.ApplicationVersion;
         _draft.Product.AndroidTargetFramework = identity.AndroidTargetFramework;
         _draft.Product.MinimumSupportedJavaVersion = identity.MinimumSupportedJavaVersion; _draft.Product.LatestSupportedJavaVersion = identity.LatestSupportedJavaVersion;
         if (_draft.Android.ApplicationId.Length == 0) _draft.Android.ApplicationId = identity.ApplicationId;
         _draft.HostProject = host; _draft.SolutionPath = Value("Solution"); _android.Vm.Refresh();
      }
      Preview();
   }
   private Task ImportAsset(bool logo) {
      var picker = new OpenFileDialog { Filter = logo ? "PNG|*.png" : "ICO|*.ico" };
      if (picker.ShowDialog(this) == true) { var data = Convert.ToBase64String(File.ReadAllBytes(picker.FileName)); if (logo) { ReleaseProduct.ReadLogo(data); _draft.Product.LogoBase64 = data; } else _draft.Product.IconBase64 = data; Preview(); } return Task.CompletedTask;
   }
   // Two cards side by side - the application icon and the launcher logo - each with its own state chip, preview
   // and buttons, so an imported ICO stays visible even when a logo is set.
   private void Preview() {
      _previews.Children.Clear();
      var cards = new WrapPanel();
      cards.Children.Add(IconCard()); cards.Children.Add(LogoCard());
      _previews.Children.Add(cards);
      _previews.Children.Add(Help(_draft.Product.IsEmHost ? "Em host: full branding and in-app update support." : "Other .NET host: basic launcher support; in-app updates unavailable."));
   }
   private Border IconCard() {
      var body = new StackPanel();
      var frames = IconFrames();
      if (frames.Count > 0) {
         var images = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
         foreach (var size in new[] { 16, 32, 48 }) {
            var frame = frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).First();
            images.Children.Add(new Image { Source = frame, Width = size, Height = size, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Bottom });
         }
         body.Children.Add(images);
         body.Children.Add(Help("Sizes in file: " + string.Join(", ", frames.Select(f => f.PixelWidth).Distinct().Order()) + " px"));
      }
      else body.Children.Add(Help("No icon imported."));
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
      Button(buttons, "Import ICO", () => ImportAsset(false));
      body.Children.Add(buttons);
      return Card("Application icon", frames.Count > 0, body);
   }
   private Border LogoCard() {
      var body = new StackPanel();
      var hasLogo = _draft.Product.LogoBase64.Length > 0;
      if (hasLogo) {
         var image = ReleaseProduct.ReadLogo(_draft.Product.LogoBase64);
         var fit = Math.Min(240d / image.PixelWidth, 48d / image.PixelHeight);
         foreach (var scale in new[] { 1d, 2d }) {
            body.Children.Add(Help($"{scale * 100:0}%"));
            body.Children.Add(new Image { Source = image, Width = image.PixelWidth * fit * scale, Height = image.PixelHeight * fit * scale,
               Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 8) });
         }
         if (Math.Min(image.PixelWidth, image.PixelHeight) * fit < 24 || fit * 2 > 1)
            body.Children.Add(Help("Warning: logo is smaller than 24 px on one side or has insufficient resolution for 200%."));
      }
      else body.Children.Add(Help("No logo imported."));
      var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
      Button(buttons, "Import PNG logo", () => ImportAsset(true));
      if (hasLogo) Button(buttons, "Clear logo", () => { _draft.Product.LogoBase64 = ""; Preview(); return Task.CompletedTask; });
      body.Children.Add(buttons);
      return Card("Logo", hasLogo, body);
   }
   private List<BitmapFrame> IconFrames() {
      if (_draft.Product.IconBase64.Length == 0) return [];
      try {
         using var stream = new MemoryStream(Convert.FromBase64String(_draft.Product.IconBase64));
         return [.. BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames];
      }
      catch (Exception) { return []; }
   }
   private static Border Card(string title, bool isSet, UIElement body) {
      var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
      var chip = new Border { Child = new TextBlock { Text = isSet ? "Imported" : "Not set" }, VerticalAlignment = VerticalAlignment.Center };
      chip.SetResourceReference(StyleProperty, isSet ? "chipSuccessStyle" : "chipMutedStyle");
      DockPanel.SetDock(chip, Dock.Right); header.Children.Add(chip);
      var caption = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
      caption.SetResourceReference(StyleProperty, "typeLabelLargeStyle"); header.Children.Add(caption);
      var content = new StackPanel(); content.Children.Add(header); content.Children.Add(body);
      var card = new Border { Child = content, Width = 300, Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 12, 12) };
      card.SetResourceReference(StyleProperty, "innerCardStyle");
      return card;
   }
   private static TextBlock Help(string text) {
      var help = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
      help.SetResourceReference(StyleProperty, "fieldHelpStyle");
      return help;
   }
   private void KeySummary() => _message.Text = _draft.PrivateKeyPem.Length > 0 ? "Signing PEM present. Export a backup: losing this key prevents existing installations from accepting updates. Anyone holding this JSON can sign releases." : "Create or import the product's P-256 PKCS#8 private PEM.";
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
      _draft.Product = Product(); if (_draft.Platform == ReleasePlatform.Windows) _draft.Product.Validate();
      _draft.SolutionPath = Value("Solution"); _draft.HostProject = _hosts.SelectedItem as string ?? ""; _draft.PublishFolder = Value("Publish folder");
      _draft.TargetKind = Enum.Parse<ReleaseTargetKind>(Value("Target")); _draft.TargetFolder = Value("Target folder"); _draft.ReleaseFolder = Value("Release folder"); _draft.CdnAddress = Value("CDN address");
      if (_draft.CdnAddress.Length > 0 && (!Uri.TryCreate(_draft.CdnAddress, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) throw new InvalidDataException("CDN address must be an absolute HTTP(S) URL.");
      _draft.Launcher.FileName = Value("Launcher exe"); _draft.Launcher.BaseVersion = Value("Launcher version"); _draft.Launcher.Channel = Value("Channel"); _draft.Launcher.UpdateBehavior = Value("Update at start"); if (_draft.Platform == ReleasePlatform.Windows) LauncherBuild.ValidateSettings(_draft.Launcher, _draft.Product.AppExe);
      var conflicts = ReleaseProfileConflicts.Find(_draft, _others, true, true);
      if (conflicts.Count > 0 && this.ShowMboxDecideWarning(string.Join("\n", conflicts) + "\nContinue?", Title) != MessageBoxResult.Yes) return;
      if (_draft.TargetKind == ReleaseTargetKind.Cdn) await Service.PostGetMeta_CdnReleaseManage(_draft.ReleaseFolder, _draft.Product.AppId);
      try { _store.Save(_draft, _lastWrite); } catch (ReleaseProfileChangedException) { if (this.ShowMboxDecideWarning("Profile JSON changed outside this editor. Overwrite it? No keeps editing.", Title) != MessageBoxResult.Yes) return; _store.Save(_draft); }
      _preferences.LauncherSourceFolder = Value("Launcher source folder"); _preferences.LauncherOutputFolder = Value("Launcher output folder"); _preferences.AndroidSdkDirectory = _android.Vm.Sdk; _preferences.JavaSdkDirectory = _android.Vm.Java; DialogResult = true;
   }
}
