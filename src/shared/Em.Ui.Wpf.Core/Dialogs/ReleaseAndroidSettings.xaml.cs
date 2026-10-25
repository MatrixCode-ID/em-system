using System.IO;
using System.Windows;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Core.Release;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Dialogs;

public partial class ReleaseAndroidSettings : System.Windows.Controls.UserControl {
   public ReleaseAndroidSettings() => InitializeComponent();
   public ReleaseAndroidSettingsVm Vm => (ReleaseAndroidSettingsVm)DataContext;
   public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(nameof(Section), typeof(ReleaseAndroidSection), typeof(ReleaseAndroidSettings), new PropertyMetadata(ReleaseAndroidSection.Identity));
   public ReleaseAndroidSection Section { get => (ReleaseAndroidSection)GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
}

public enum ReleaseAndroidSection { Identity, Environment, Signing }

public sealed class ReleaseAndroidSettingsVm : MvvmModelBase {
   public ReleaseAndroidSettingsVm() {
      RegisterCommand(nameof(ChangePlatformCommand), ChangePlatformCommand, ChangePlatformCommandAllowed);
      RegisterCommand(nameof(CheckCommand), CheckCommand, CheckCommandAllowed);
      RegisterCommand(nameof(PrepareEnvironmentCommand), PrepareEnvironmentCommand, PrepareEnvironmentCommandAllowed);
      RegisterCommand(nameof(CreateKeystoreCommand), CreateKeystoreCommand, CreateKeystoreCommandAllowed);
      RegisterCommand(nameof(ImportKeystoreCommand), ImportKeystoreCommand, ImportKeystoreCommandAllowed);
      RegisterCommand(nameof(BrowseSdkCommand), BrowseSdkCommand, BrowseSdkCommandAllowed);
      RegisterCommand(nameof(BrowseJavaCommand), BrowseJavaCommand, BrowseJavaCommandAllowed);
      RegisterCommand(nameof(RefreshEnvironmentCommand), RefreshEnvironmentCommand, RefreshEnvironmentCommandAllowed);
      RegisterCommand(nameof(CopyStorePasswordCommand), CopyStorePasswordCommand, CopyStorePasswordCommandAllowed);
      RegisterCommand(nameof(CopyKeyPasswordCommand), CopyKeyPasswordCommand, CopyKeyPasswordCommandAllowed);
   }
   public ReleaseProfile Profile { get; set; } = new();
   public Window? Owner { get; set; }
   public Func<Task>? PlatformChanged { get; set; }
   public ReleaseAndroid Android => Profile.Android;
   public ReleasePlatform[] Platforms => Enum.GetValues<ReleasePlatform>();
   public ReleasePlatform SelectedPlatform { get => Get<ReleasePlatform>(); set => Set(value); }
   public bool IsAndroid => Profile.Platform == ReleasePlatform.Android;
   public string Sdk { get => Get<string>() ?? ""; set => Set(value); }
   public string Java { get => Get<string>() ?? ""; set => Set(value); }
   public string Status { get => Get<string>() ?? ""; set => Set(value); }
   public bool Busy { get => Get<bool>(); private set { Set(value); RaiseCommands(); } }
   public bool ParentBusy { get => Get<bool>(); set { Set(value); RaiseCommands(); } }
   public string BusyText { get => Get<string>() ?? ""; private set => Set(value); }
   public bool SetUserEnvironment { get => Get<bool>(); set => Set(value); }
   public IReadOnlyList<string> SdkCandidates { get => Get<IReadOnlyList<string>>() ?? []; private set => Set(value); }
   public IReadOnlyList<string> JavaCandidates { get => Get<IReadOnlyList<string>>() ?? []; private set => Set(value); }
   public bool IdleAllowed() => !Busy && !ParentBusy;
   public bool ChangePlatformCommandAllowed() => IdleAllowed();
   public bool CheckCommandAllowed() => AndroidAllowed();
   public bool PrepareEnvironmentCommandAllowed() => AndroidAllowed();
   public bool CreateKeystoreCommandAllowed() => AndroidAllowed();
   public bool ImportKeystoreCommandAllowed() => AndroidAllowed();
   public bool BrowseSdkCommandAllowed() => AndroidAllowed();
   public bool BrowseJavaCommandAllowed() => AndroidAllowed();
   public bool RefreshEnvironmentCommandAllowed() => AndroidAllowed();
   public bool CopyStorePasswordCommandAllowed() => AndroidAllowed();
   public bool CopyKeyPasswordCommandAllowed() => AndroidAllowed();
   public bool AndroidAllowed() => IdleAllowed() && IsAndroid;
   public void UpdateBusyText(string text) => BusyText = text;
   private void RaiseCommands() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
   public void Refresh() { NotifyChanged(nameof(Android)); NotifyChanged(nameof(IsAndroid)); RaiseCommands(); }
   public async Task RefreshCandidatesAsync() {
      var sdk = Sdk; var java = Java;
      var candidates = await Task.Run(() => (Sdk: AndroidEnvironment.SdkCandidates(sdk), Java: AndroidEnvironment.JavaCandidates(java)));
      SdkCandidates = candidates.Sdk; JavaCandidates = candidates.Java;
   }
   public Task RefreshEnvironmentCommand() => Run("Finding installed Android and Java SDKs…", RefreshCandidatesAsync);
   public Task ChangePlatformCommand() => Run("Applying platform and reading application source…", async () => {
      if (SelectedPlatform == Profile.Platform) return;
      if (Owner?.ShowMboxDecideWarning("Change platform and create a new manifest signing key? Existing installations will reject this new key. Export a backup first.", "Release platform") != MessageBoxResult.Yes) { SelectedPlatform = Profile.Platform; return; }
      Profile.ChangePlatform(SelectedPlatform); Refresh();
      if (PlatformChanged is not null) await PlatformChanged();
      if (IsAndroid) await RefreshCandidatesAsync();
   });
   private async Task Run(string caption, Func<Task> action) {
      if (!IdleAllowed()) return;
      BusyText = caption; Status = ""; Busy = true;
      try { await action(); } catch (Exception ex) { Status = ex.Message; } finally { Busy = false; }
   }
   public Task CheckCommand() => Run("Checking Android build environment…", async () => Status = string.Join("\n", await AndroidEnvironment.CheckAsync(Sdk, Java, Profile.Product, CancellationToken.None)));
   public Task CreateKeystoreCommand() => Run("Creating Android signing keystore…", async () => {
      if (Android.KeystoreBase64.Length > 0 && Owner?.ShowMboxDecideWarning("Replace the permanent Android signing key? Existing installations cannot accept updates signed by the replacement.", "Keystore") != MessageBoxResult.Yes) return;
      var key = await AndroidKeystore.CreateAsync(Java, Profile.Product.Publisher, CancellationToken.None); ApplyKey(key);
   });
   private void ApplyKey(ReleaseAndroid key) {
      Android.KeystoreBase64 = key.KeystoreBase64; Android.KeystoreAlias = key.KeystoreAlias;
      Android.KeystorePassword = key.KeystorePassword; Android.KeyPassword = key.KeyPassword; Android.CertificateSha256 = key.CertificateSha256;
      Android.PreparedVersionCode = 0; Android.PreparedApkSha256 = Android.PreparedVersionName = ""; Refresh();
   }
   public Task ImportKeystoreCommand() => Run("Importing Android signing keystore…", async () => {
      var picker = new OpenFileDialog { Filter = "Android keystore|*.jks;*.keystore;*.p12;*.pfx" };
      if (picker.ShowDialog(Owner) != true) return;
      if (Android.KeystoreBase64.Length > 0 && Owner?.ShowMboxDecideWarning("Replace this profile's permanent keystore? Export a backup first.", "Import keystore") != MessageBoxResult.Yes) return;
      ApplyKey(await AndroidKeystore.ImportAsync(Java, picker.FileName, Android.KeystoreAlias, Android.KeystorePassword, Android.KeyPassword, CancellationToken.None));
   });
   public void BrowseSdkCommand() { var picker = new OpenFolderDialog(); if (picker.ShowDialog(Owner) == true) Sdk = picker.FolderName; }
   public void BrowseJavaCommand() { var picker = new OpenFolderDialog(); if (picker.ShowDialog(Owner) == true) Java = picker.FolderName; }
   public void CopyStorePasswordCommand() => Clipboard.SetText(Android.KeystorePassword);
   public void CopyKeyPasswordCommand() => Clipboard.SetText(Android.KeyPassword);
   public Task PrepareEnvironmentCommand() => Run("Preparing Android build environment…", async () => {
      if (string.IsNullOrWhiteSpace(Profile.HostProject) || string.IsNullOrWhiteSpace(Profile.SolutionPath) || string.IsNullOrWhiteSpace(Profile.Product.AndroidTargetFramework))
         throw new InvalidOperationException("Check the solution and select an Android host before preparing the environment.");
      if (Sdk.Length == 0) Sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
      var project = Path.GetFullPath(Profile.HostProject, Path.GetDirectoryName(Path.GetFullPath(Profile.SolutionPath))!);
      var checks = await AndroidEnvironment.CheckAsync(Sdk, Java, Profile.Product, CancellationToken.None);
      var commands = AndroidEnvironment.PreparationCommands(checks, project, Profile.Product.AndroidTargetFramework, Sdk, Java, Profile.Product.LatestSupportedJavaVersion);
      if (commands.Count == 0) { Status = "Environment is ready."; return; }
      if (Owner?.ShowMboxDecideWarning(string.Join("\n", commands) + "\n\nInstall these missing components? SDK licenses will be accepted.", "Prepare Environment") != MessageBoxResult.Yes) return;
      var log = new Progress<string>(line => Status += "\n" + line);
      Status = "Preparing Android environment…";
      foreach (var command in commands) {
         if (command.Executable == "winget" && !((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(path => File.Exists(Path.Combine(path, "winget.exe")))))
            throw new InvalidOperationException("winget is unavailable. Install App Installer or choose an installed JDK manually.");
         if (command.Elevated) {
            await AndroidEnvironment.RunElevatedAsync(command, log, CancellationToken.None);
         } else await AndroidProcess.CaptureAsync(command.Executable, command.Arguments.Select(arg => arg.StartsWith("-p:JavaSdkDirectory=") ? "-p:JavaSdkDirectory=" + Java : arg), null, CancellationToken.None, log);
         if (command.Executable == "winget") { await RefreshCandidatesAsync(); Java = JavaCandidates.LastOrDefault() ?? Java; }
      }
      if (SetUserEnvironment) { Environment.SetEnvironmentVariable("ANDROID_HOME", Sdk, EnvironmentVariableTarget.User); Environment.SetEnvironmentVariable("JAVA_HOME", Java, EnvironmentVariableTarget.User); }
      Status += "\n" + string.Join("\n", await AndroidEnvironment.CheckAsync(Sdk, Java, Profile.Product, CancellationToken.None)); await RefreshCandidatesAsync(); Refresh();
   });
}
