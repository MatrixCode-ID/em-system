using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Api.Core.Models;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Dialogs;

public partial class ReleaseSettingsDialog : EmWindow {
   public ReleaseSettingsDialog() {
      InitializeComponent(); Vm.MainWindow = this;
      Vm.RequestClose += result => DialogResult = result;
      // Loading and closing are view lifecycle events; business actions stay in the view model.
      Loaded += async (_, _) => await Vm.InitializeAsync();
      Closing += (_, args) => args.Cancel = Vm.IsBusy;
   }
   public ReleaseSettingsDialog(EmApp app, ReleaseProfileStore store, ReleaseProfile profile, DateTime lastWriteUtc,
      IReadOnlyList<ReleaseProfile> others, string? sdkVersion, bool isSdkChecked, bool canUseCdn) : this() =>
      Vm.Configure(app, store, profile, lastWriteUtc, others, new ReleaseManagerPreferences(app), canUseCdn);
   public ReleaseSettingsDialogVm Vm => (ReleaseSettingsDialogVm)DataContext;
}

public sealed class ReleaseSettingsDialogVm : MvvmModelBase {
   private ReleaseProfile _draft = new();
   private ReleaseProfileStore? _store;
   private ReleaseManagerPreferences? _preferences;
   private DateTime _lastWrite;
   private IReadOnlyList<ReleaseProfile> _others = [];
   private bool _loadingHosts;
   private bool _initialized;
   private bool _closed;

   public ReleaseSettingsDialogVm() {
      RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      RegisterCommand(nameof(BrowseReleaseNotesCommand), BrowseReleaseNotesCommand, () => CanEdit);
      RegisterCommand(nameof(BrowseSolutionCommand), BrowseSolutionCommand, BrowseSolutionCommandAllowed);
      RegisterCommand(nameof(CheckSolutionCommand), CheckSolutionCommand, CheckSolutionCommandAllowed);
      RegisterCommand(nameof(BrowsePublishCommand), BrowsePublishCommand, BrowsePublishCommandAllowed);
      RegisterCommand(nameof(BrowseTargetCommand), BrowseTargetCommand, BrowseTargetCommandAllowed);
      RegisterCommand(nameof(BrowseLauncherSourceCommand), BrowseLauncherSourceCommand, BrowseLauncherSourceCommandAllowed);
      RegisterCommand(nameof(BrowseLauncherOutputCommand), BrowseLauncherOutputCommand, BrowseLauncherOutputCommandAllowed);
      RegisterCommand(nameof(SelectManagedCommand), SelectManagedCommand, SelectManagedCommandAllowed);
      RegisterCommand(nameof(ReleaseManagedCommand), ReleaseManagedCommand, ReleaseManagedCommandAllowed);
      RegisterCommand(nameof(CreatePemCommand), CreatePemCommand, CreatePemCommandAllowed);
      RegisterCommand(nameof(ImportPemCommand), ImportPemCommand, ImportPemCommandAllowed);
      RegisterCommand(nameof(ExportPemCommand), ExportPemCommand, ExportPemCommandAllowed);
      RegisterCommand(nameof(ImportIconCommand), ImportIconCommand, ImportIconCommandAllowed);
      RegisterCommand(nameof(ImportLogoCommand), ImportLogoCommand, ImportLogoCommandAllowed);
      RegisterCommand(nameof(ClearLogoCommand), ClearLogoCommand, ClearLogoCommandAllowed);
      Android.PlatformChanged = ApplyPlatformAsync;
      Android.PropertyChanged += (_, args) => {
         if (args.PropertyName is nameof(Android.Busy) or nameof(Android.BusyText)) NotifyBusy();
         if (args.PropertyName == nameof(Android.IsAndroid)) { NotifyChanged(nameof(IsAndroid)); NotifyChanged(nameof(IsWindows)); }
         if (args.PropertyName == nameof(Android.Status)) NotifyChanged(nameof(AndroidStatus));
         if (args.PropertyName == nameof(Android.SelectedPlatform)) RaiseCommands();
      };
   }
   public event Action<bool>? RequestClose;
   public ReleaseAndroidSettingsVm Android { get; } = new();
   public ReleaseProfile Draft => _draft;
   public bool IsAndroid => _draft.Platform == ReleasePlatform.Android;
   public bool IsWindows => !IsAndroid;
   public bool IsBusy => Busy || Android.Busy;
   public bool CanEdit => !IsBusy && !_closed;
   public bool Busy { get => Get<bool>(); private set { Set(value); Android.ParentBusy = value; NotifyBusy(); } }
   public string BusyText => Android.Busy ? Android.BusyText : Status;
   public string AndroidStatus => Android.Status;
   public string Status { get => Get<string>() ?? ""; private set { Set(value); NotifyChanged(nameof(BusyText)); } }
   public string Error { get => Get<string>() ?? ""; private set => Set(value); }
   public bool HasError => Error.Length > 0;
   public string Title { get => Get<string>() ?? "Release settings"; private set => Set(value); }
   public string SolutionPath { get => Get<string>() ?? ""; set { Set(value); _draft.SolutionPath = value.Trim(); RaiseCommands(); } }
   public IReadOnlyList<string> Hosts { get => Get<IReadOnlyList<string>>() ?? []; private set => Set(value); }
   public string SelectedHost {
      get => Get<string>() ?? "";
      set {
         if (SelectedHost == value) return;
         Set(value); _draft.HostProject = value ?? ""; RaiseCommands();
         if (!_loadingHosts && CanEdit && !string.IsNullOrWhiteSpace(value)) _ = Run("Reading application identity…", ReadIdentityAsync);
      }
   }
   public string AppName { get => Get<string>() ?? ""; set { Set(value); _draft.Product.AppName = value; } }
   public string Publisher { get => Get<string>() ?? ""; set { Set(value); _draft.Product.Publisher = value; } }
   public string AppId { get => Get<string>() ?? ""; set { Set(value); _draft.Product.AppId = value; } }
   public string AppExe { get => Get<string>() ?? ""; set { Set(value); _draft.Product.AppExe = value; } }
   public string PublishFolder { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>Requires a matching release note and a newer application version.</summary>
   public bool ReleaseNoteLock { get => Get<bool>(); set => Set(value); }
   /// <summary>Profile-relative notes folder selected once in this editor.</summary>
   public string ReleaseNotesFolder { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>The host's application version.</summary>
   public string ProductVersion => _draft.Product.Version;
   /// <summary>Selects a folder on the same drive as the profile store.</summary>
   public void BrowseReleaseNotesCommand() => BrowseFolder(value => {
      try { ReleaseNotesFolder = Em.Ui.Wpf.Core.ReleaseNotes.ReleaseNoteFolder.Relative(_store!.Root, value); }
      catch (Exception ex) { MainWindow?.ShowMboxError(ex.Message); }
   });
   public ReleaseTargetKind TargetKind { get => Get<ReleaseTargetKind>(); set { Set(value); NotifyChanged(nameof(IsCdn)); NotifyChanged(nameof(IsFolder)); RaiseCommands(); } }
   public ReleaseTargetKind[] TargetKinds => Enum.GetValues<ReleaseTargetKind>();
   public bool IsCdn => TargetKind == ReleaseTargetKind.Cdn;
   public bool IsFolder => !IsCdn;
   public bool CanUseCdn { get => Get<bool>(); private set => Set(value); }
   public string TargetFolder { get => Get<string>() ?? ""; set => Set(value); }
   public string ReleaseFolder { get => Get<string>() ?? ""; set => Set(value); }
   public string CdnAddress { get => Get<string>() ?? ""; set => Set(value); }
   public string LauncherFileName { get => Get<string>() ?? ""; set => Set(value); }
   public string LauncherVersion { get => Get<string>() ?? ""; set => Set(value); }
   public string LauncherChannel { get => Get<string>() ?? "beta"; set => Set(value); }
   public string LauncherUpdate { get => Get<string>() ?? "automatic"; set => Set(value); }
   public string[] Channels => ["prealpha", "alpha", "beta", "rc", "release"];
   public string[] UpdateBehaviors => ["automatic", "ask"];
   public string LauncherSource { get => Get<string>() ?? ""; set => Set(value); }
   public string LauncherOutput { get => Get<string>() ?? ""; set => Set(value); }
   public ImageSource? IconPreview { get => Get<ImageSource>(); private set => Set(value); }
   public ImageSource? LogoPreview { get => Get<ImageSource>(); private set { Set(value); NotifyChanged(nameof(HasLogo)); } }
   public bool HasLogo => LogoPreview is not null;
   public string BrandingStatus { get => Get<string>() ?? ""; private set => Set(value); }
   public string IconSizes { get => Get<string>() ?? "No icon imported."; private set => Set(value); }
   public double LogoWidth { get => Get<double>(); private set => Set(value); }
   public double LogoHeight { get => Get<double>(); private set => Set(value); }
   public double LogoDoubleWidth => LogoWidth * 2;
   public double LogoDoubleHeight => LogoHeight * 2;
   public string LogoWarning { get => Get<string>() ?? ""; private set => Set(value); }
   public string KeyStatus => _draft.PrivateKeyPem.Length > 0 ? "Manifest signing key is ready. Export this profile as a protected backup." : "Create or import a manifest signing key before preparing a release.";

   public void Configure(EmApp app, ReleaseProfileStore store, ReleaseProfile profile, DateTime lastWrite,
      IReadOnlyList<ReleaseProfile> others, ReleaseManagerPreferences preferences, bool canUseCdn) {
      EmApp = app; _store = store; _draft = profile.Clone(); _lastWrite = lastWrite; _others = others; _preferences = preferences;
      Title = "Release settings · " + profile.Name; CanUseCdn = canUseCdn;
      Android.Profile = _draft; Android.Owner = MainWindow; Android.SelectedPlatform = _draft.Platform; Android.Refresh();
      Android.Sdk = preferences.AndroidSdkDirectory; Android.Java = preferences.JavaSdkDirectory;
      SolutionPath = profile.SolutionPath;
      // Opening shows only the saved host: Browse or Check solution reads the solution again.
      _loadingHosts = true;
      try { Hosts = profile.HostProject.Length > 0 ? [profile.HostProject] : []; SelectedHost = profile.HostProject; }
      finally { _loadingHosts = false; }
      ApplyProductFields(); PublishFolder = profile.PublishFolder; TargetKind = profile.TargetKind;
      ReleaseNoteLock = profile.ReleaseNoteLock; ReleaseNotesFolder = profile.ReleaseNotesFolder;
      TargetFolder = profile.TargetFolder; ReleaseFolder = profile.ReleaseFolder; CdnAddress = profile.CdnAddress;
      LauncherFileName = profile.Launcher.FileName; LauncherVersion = profile.Launcher.BaseVersion;
      LauncherChannel = profile.Launcher.Channel; LauncherUpdate = profile.Launcher.UpdateBehavior;
      LauncherSource = preferences.LauncherSourceFolder; LauncherOutput = preferences.LauncherOutputFolder;
      NotifyChanged(nameof(Draft)); NotifyChanged(nameof(KeyStatus)); Preview(); RaiseCommands();
   }
   public Task InitializeAsync() {
      if (_initialized) return Task.CompletedTask;
      _initialized = true;
      return IsAndroid ? Run("Finding installed Android and Java SDKs…", Android.RefreshCandidatesAsync) : Task.CompletedTask;
   }
   private void NotifyBusy() {
      NotifyChanged(nameof(IsBusy)); NotifyChanged(nameof(CanEdit)); NotifyChanged(nameof(BusyText)); RaiseCommands();
   }
   private void RaiseCommands() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
   public bool IdleAllowed() => CanEdit;
   public bool BrowseSolutionCommandAllowed() => IdleAllowed();
   public bool BrowsePublishCommandAllowed() => IdleAllowed();
   public bool BrowseTargetCommandAllowed() => IdleAllowed();
   public bool BrowseLauncherSourceCommandAllowed() => IdleAllowed();
   public bool BrowseLauncherOutputCommandAllowed() => IdleAllowed();
   public bool SelectManagedCommandAllowed() => CdnAllowed();
   public bool ReleaseManagedCommandAllowed() => CdnAllowed();
   public bool CreatePemCommandAllowed() => IdleAllowed();
   public bool ImportPemCommandAllowed() => IdleAllowed();
   public bool ImportIconCommandAllowed() => IdleAllowed();
   public bool ImportLogoCommandAllowed() => IdleAllowed();
   public bool ClearLogoCommandAllowed() => IdleAllowed();
   public bool CheckSolutionCommandAllowed() => CanEdit && !string.IsNullOrWhiteSpace(SolutionPath);
   public bool SaveCommandAllowed() => CanEdit && Android.SelectedPlatform == _draft.Platform;
   public bool CdnAllowed() => CanEdit && IsCdn && CanUseCdn;
   public bool ExportPemCommandAllowed() => CanEdit && _draft.PrivateKeyPem.Length > 0;
   private bool Confirm(string message) => MainWindow?.ShowMboxDecideWarning(message, Title) == MessageBoxResult.Yes;
   private async Task Run(string caption, Func<Task> action) {
      if (!CanEdit) return;
      Status = caption; Error = ""; NotifyChanged(nameof(HasError)); Busy = true;
      try { await action(); Status = ""; }
      catch (Exception ex) { Error = ex.Message; NotifyChanged(nameof(HasError)); Status = ""; }
      finally { Busy = false; }
   }
   public Task CheckSolutionCommand() => Run("Reading compatible application projects…", LoadSolutionAsync);
   public Task BrowseSolutionCommand() => Run("Reading solution…", async () => {
      var picker = new OpenFileDialog { Filter = "Solution|*.slnx" };
      if (picker.ShowDialog(MainWindow) != true) return;
      SolutionPath = picker.FileName; await LoadSolutionAsync();
   });
   private async Task ApplyPlatformAsync() {
      NotifyChanged(nameof(IsAndroid)); NotifyChanged(nameof(IsWindows)); NotifyChanged(nameof(KeyStatus));
      if (!string.IsNullOrWhiteSpace(SolutionPath)) await LoadSolutionAsync();
      else {
         _loadingHosts = true;
         try { Hosts = []; SelectedHost = ""; } finally { _loadingHosts = false; }
      }
   }
   private async Task LoadSolutionAsync() {
      var solution = Path.GetFullPath(SolutionPath.Trim()); var platform = _draft.Platform;
      var previous = SelectedHost; var done = false;
      // Progress is posted to the UI thread and may land after the read finished; drop late reports.
      var progress = new Progress<string>(text => { if (!done && !_closed) { Status = text; Android.UpdateBusyText(text); } });
      IReadOnlyList<string> hosts;
      try { hosts = await Task.Run(() => ReleaseBuilder.ReadHostProjects(solution, progress, CancellationToken.None, platform)); }
      finally { done = true; }
      _loadingHosts = true;
      try { Hosts = hosts; SelectedHost = hosts.Contains(previous) ? previous : hosts.FirstOrDefault() ?? ""; }
      finally { _loadingHosts = false; }
      if (hosts.Count == 0) throw new InvalidDataException("No compatible application host was found. Choose a solution containing a " + platform + " application.");
      await ReadIdentityAsync();
   }
   private ReleaseProduct Product() {
      var product = _draft.Product;
      product.AppName = AppName.Trim(); product.Publisher = Publisher.Trim(); product.AppId = AppId.Trim(); product.AppExe = AppExe.Trim();
      return product;
   }
   private void ApplyProductFields() {
      NotifyChanged(nameof(ProductVersion));
      AppName = _draft.Product.AppName; Publisher = _draft.Product.Publisher; AppId = _draft.Product.AppId; AppExe = _draft.Product.AppExe;
   }
   private async Task ReadIdentityAsync() {
      if (SelectedHost.Length == 0) return;
      var host = SelectedHost; var solution = Path.GetFullPath(SolutionPath.Trim());
      var identity = await ReleaseProduct.ReadAsync(Path.GetFullPath(host, Path.GetDirectoryName(solution)!), CancellationToken.None, _draft.Platform);
      var old = Product();
      string Hash(string value) => value.Length == 0 ? "(empty)" : Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(value)))[..16];
      var changes = new[] { ("Product", old.AppName, identity.AppName), ("Company", old.Publisher, identity.Publisher), ("app_id", old.AppId, identity.AppId), ("Application exe", old.AppExe, identity.AppExe), ("Icon hash", Hash(old.IconBase64), Hash(identity.IconBase64)), ("Logo hash", Hash(old.LogoBase64), Hash(identity.LogoBase64)) }
         .Where(v => v.Item2 != v.Item3).Select(v => $"{v.Item1}: profile '{v.Item2}' → solution '{v.Item3}'").ToArray();
      if (changes.Length == 0 || old.AppId.Length == 0 || Confirm(string.Join("\n", changes) + "\n\nUse solution identity? No keeps the profile.")) {
         _draft.Product = identity; ApplyProductFields();
      }
      _draft.Product.IsEmHost = identity.IsEmHost;
      if (IsAndroid) {
         _draft.Product.Version = identity.Version; _draft.Product.ApplicationVersion = identity.ApplicationVersion;
         _draft.Product.AndroidTargetFramework = identity.AndroidTargetFramework;
         _draft.Product.MinimumSupportedJavaVersion = identity.MinimumSupportedJavaVersion;
         _draft.Product.LatestSupportedJavaVersion = identity.LatestSupportedJavaVersion;
         if (_draft.Android.ApplicationId.Length == 0) _draft.Android.ApplicationId = identity.ApplicationId;
         Android.Refresh();
      }
      _draft.HostProject = host; _draft.SolutionPath = solution; Preview();
      NotifyChanged(nameof(ProductVersion));
   }
   private void BrowseFolder(Action<string> apply) { var picker = new OpenFolderDialog(); if (picker.ShowDialog(MainWindow) == true) apply(picker.FolderName); }
   public void BrowsePublishCommand() => BrowseFolder(value => PublishFolder = value);
   public void BrowseTargetCommand() => BrowseFolder(value => TargetFolder = value);
   public void BrowseLauncherSourceCommand() => BrowseFolder(value => LauncherSource = value);
   public void BrowseLauncherOutputCommand() => BrowseFolder(value => LauncherOutput = value);
   public Task SelectManagedCommand() => Run("Reading release folders…", async () => {
      var folder = "";
      while (true) {
         var content = await Service.GetMeta_CdnReleaseFolder(folder);
         var dialog = new TextInputDialog("Choose CDN folder", "Folders: " + string.Join(", ", content.Entries.Where(e => e.IsFolder).Select(e => e.Name)) + "\nType a full relative path to browse. Repeat that path to select it; a missing path creates a new folder.", "Folder path", "Browse / select", initialText: folder.Length == 0 ? ReleaseFolder : folder) { Owner = MainWindow };
         if (dialog.ShowDialog() != true) return;
         var path = dialog.Vm.Result;
         if (path == folder || !content.Entries.Any(e => e.IsFolder && e.Path == path)) { await Service.PostGetMeta_CdnReleaseManage(path, AppId.Trim()); ReleaseFolder = path; return; }
         folder = path;
      }
   });
   public Task ReleaseManagedCommand() => Run("Releasing managed folder…", async () => {
      if (Confirm("Release this product's managed folder?")) await Service.PostMeta_CdnReleaseUnmanage(ReleaseFolder.Trim(), AppId.Trim());
   });
   private ICdnReleaseServices Service => EmApp!.ServiceProvider.GetRequiredService<ICdnReleaseServices>();
   public void CreatePemCommand() {
      if (_draft.PrivateKeyPem.Length == 0 || Confirm("Replace the product key? Existing installations reject a different key for the same app_id.")) _draft.PrivateKeyPem = ReleasePemKey.Create();
      NotifyChanged(nameof(KeyStatus)); RaiseCommands();
   }
   public Task ImportPemCommand() => Run("Importing manifest key…", () => {
      var picker = new OpenFileDialog { Filter = "Private PEM|*.pem" };
      if (picker.ShowDialog(MainWindow) == true) { using var key = ReleasePemKey.Open(File.ReadAllText(picker.FileName)); _draft.PrivateKeyPem = key.ExportPkcs8PrivateKeyPem(); NotifyChanged(nameof(KeyStatus)); RaiseCommands(); }
      return Task.CompletedTask;
   });
   public Task ExportPemCommand() => Run("Exporting public key…", () => {
      var picker = new SaveFileDialog { Filter = "Public PEM|*.pem" };
      if (picker.ShowDialog(MainWindow) == true) File.WriteAllText(picker.FileName, ReleasePemKey.Public(_draft.PrivateKeyPem));
      return Task.CompletedTask;
   });
   private Task ImportAsset(bool logo) => Run("Importing product branding…", () => {
      var picker = new OpenFileDialog { Filter = logo ? "PNG|*.png" : "ICO|*.ico" };
      if (picker.ShowDialog(MainWindow) == true) {
         var data = Convert.ToBase64String(File.ReadAllBytes(picker.FileName));
         if (logo) { ReleaseProduct.ReadLogo(data); _draft.Product.LogoBase64 = data; }
         else _draft.Product.IconBase64 = data;
         Preview();
      }
      return Task.CompletedTask;
   });
   public Task ImportIconCommand() => ImportAsset(false);
   public Task ImportLogoCommand() => ImportAsset(true);
   public void ClearLogoCommand() { _draft.Product.LogoBase64 = ""; Preview(); }
   private void Preview() {
      IconPreview = LogoPreview = null; IconSizes = "No icon imported."; LogoWarning = "";
      if (_draft.Product.IconBase64.Length > 0) {
         try {
            using var stream = new MemoryStream(Convert.FromBase64String(_draft.Product.IconBase64));
            var frames = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames;
            IconPreview = frames.OrderByDescending(frame => frame.PixelWidth).FirstOrDefault();
            IconSizes = "Sizes in file: " + string.Join(", ", frames.Select(frame => frame.PixelWidth).Distinct().Order()) + " px";
         } catch (Exception ex) when (ex is IOException or FileFormatException or FormatException or NotSupportedException) { }
      }
      if (_draft.Product.LogoBase64.Length > 0) {
         var image = ReleaseProduct.ReadLogo(_draft.Product.LogoBase64); LogoPreview = image;
         var fit = Math.Min(240d / image.PixelWidth, 48d / image.PixelHeight);
         LogoWidth = image.PixelWidth * fit; LogoHeight = image.PixelHeight * fit;
         NotifyChanged(nameof(LogoDoubleWidth)); NotifyChanged(nameof(LogoDoubleHeight));
         if (Math.Min(image.PixelWidth, image.PixelHeight) * fit < 24 || fit * 2 > 1)
            LogoWarning = "Warning: logo is smaller than 24 px on one side or has insufficient resolution for 200%.";
      }
      BrandingStatus = _draft.Product.IsEmHost ? "Em host: branding and in-app updates are supported." : "Other .NET host: basic launcher support; in-app updates are unavailable.";
   }
   public Task SaveCommand() => Run("Saving release profile…", async () => {
      if (Android.SelectedPlatform != _draft.Platform) throw new InvalidDataException("Apply the selected platform before saving.");
      _draft.Product = Product(); if (IsWindows) _draft.Product.Validate();
      if (ReleaseNotesFolder.Length > 0) _ = Em.Ui.Wpf.Core.ReleaseNotes.ReleaseNoteFolder.Resolve(_store!.Root, ReleaseNotesFolder);
      _draft.ReleaseNoteLock = ReleaseNoteLock; _draft.ReleaseNotesFolder = ReleaseNotesFolder;
      _draft.SolutionPath = SolutionPath.Trim(); _draft.HostProject = SelectedHost; _draft.PublishFolder = PublishFolder.Trim();
      _draft.TargetKind = TargetKind; _draft.TargetFolder = TargetFolder.Trim(); _draft.ReleaseFolder = ReleaseFolder.Trim(); _draft.CdnAddress = CdnAddress.Trim();
      if (_draft.CdnAddress.Length > 0 && (!Uri.TryCreate(_draft.CdnAddress, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) throw new InvalidDataException("CDN address must be an absolute HTTP(S) URL.");
      _draft.Launcher.FileName = LauncherFileName.Trim(); _draft.Launcher.BaseVersion = LauncherVersion.Trim(); _draft.Launcher.Channel = LauncherChannel; _draft.Launcher.UpdateBehavior = LauncherUpdate;
      if (IsWindows) LauncherBuild.ValidateSettings(_draft.Launcher, _draft.Product.AppExe);
      var conflicts = ReleaseProfileConflicts.Find(_draft, _others, true, true);
      if (conflicts.Count > 0 && !Confirm(string.Join("\n", conflicts) + "\nContinue?")) return;
      if (IsCdn) await Service.PostGetMeta_CdnReleaseManage(_draft.ReleaseFolder, _draft.Product.AppId);
      try { _store!.Save(_draft, _lastWrite); }
      catch (ReleaseProfileChangedException) { if (!Confirm("Profile JSON changed outside this editor. Overwrite it? No keeps editing.")) return; _store!.Save(_draft); }
      _preferences!.LauncherSourceFolder = LauncherSource.Trim(); _preferences.LauncherOutputFolder = LauncherOutput.Trim();
      _preferences.AndroidSdkDirectory = Android.Sdk.Trim(); _preferences.JavaSdkDirectory = Android.Java.Trim();
      _closed = true; Busy = false; RequestClose?.Invoke(true);
   });
}
