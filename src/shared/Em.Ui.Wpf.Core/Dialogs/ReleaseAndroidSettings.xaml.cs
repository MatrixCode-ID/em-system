using System.Diagnostics;
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
}

public sealed class ReleaseAndroidSettingsVm : MvvmModelBase {
   public ReleaseAndroidSettingsVm() {
      RegisterCommand(nameof(ChangePlatformCommand), ChangePlatformCommand, IdleAllowed);
      RegisterCommand(nameof(CheckCommand), CheckCommand, AndroidAllowed);
      RegisterCommand(nameof(PrepareEnvironmentCommand), PrepareEnvironmentCommand, AndroidAllowed);
      RegisterCommand(nameof(CreateKeystoreCommand), CreateKeystoreCommand, AndroidAllowed);
      RegisterCommand(nameof(ImportKeystoreCommand), ImportKeystoreCommand, AndroidAllowed);
      RegisterCommand(nameof(BrowseSdkCommand), BrowseSdkCommand, AndroidAllowed);
      RegisterCommand(nameof(BrowseJavaCommand), BrowseJavaCommand, AndroidAllowed);
      RegisterCommand(nameof(CopyStorePasswordCommand), CopyStorePasswordCommand, AndroidAllowed);
      RegisterCommand(nameof(CopyKeyPasswordCommand), CopyKeyPasswordCommand, AndroidAllowed);
   }
   public ReleaseProfile Profile { get; set; } = new();
   public Window? Owner { get; set; }
   public Action? PlatformChanged { get; set; }
   public ReleaseAndroid Android => Profile.Android;
   public ReleasePlatform[] Platforms => Enum.GetValues<ReleasePlatform>();
   public ReleasePlatform SelectedPlatform { get => Get<ReleasePlatform>(); set => Set(value); }
   public bool IsAndroid => Profile.Platform == ReleasePlatform.Android;
   public string Sdk { get => Get<string>() ?? ""; set => Set(value); }
   public string Java { get => Get<string>() ?? ""; set => Set(value); }
   public string Status { get => Get<string>() ?? ""; set => Set(value); }
   public bool Busy { get => Get<bool>(); set { Set(value); foreach (var command in Commands) command.RaiseCanExecuteChanged(); } }
   public bool SetUserEnvironment { get => Get<bool>(); set => Set(value); }
   public IReadOnlyList<string> SdkCandidates => AndroidEnvironment.SdkCandidates(Sdk);
   public IReadOnlyList<string> JavaCandidates => AndroidEnvironment.JavaCandidates(Java);
   public bool IdleAllowed() => !Busy;
   public bool AndroidAllowed() => !Busy && IsAndroid;
   public void Refresh() { NotifyChanged(nameof(Android)); NotifyChanged(nameof(IsAndroid)); NotifyChanged(nameof(SdkCandidates)); NotifyChanged(nameof(JavaCandidates)); foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
   public void ChangePlatformCommand() {
      if (SelectedPlatform == Profile.Platform) return;
      if (Owner?.ShowMboxDecideWarning("Change platform and create a new manifest signing key? Existing installations will reject this new key. Export a backup first.", "Release platform") != MessageBoxResult.Yes) { SelectedPlatform = Profile.Platform; return; }
      Profile.ChangePlatform(SelectedPlatform); Refresh(); PlatformChanged?.Invoke();
   }
   private async Task Run(Func<Task> action) { Busy = true; try { await action(); } catch (Exception ex) { Status = ex.Message; } finally { Busy = false; } }
   public Task CheckCommand() => Run(async () => Status = string.Join("\n", await AndroidEnvironment.CheckAsync(Sdk, Java, Profile.Product, CancellationToken.None)));
   public Task CreateKeystoreCommand() => Run(async () => {
      if (Android.KeystoreBase64.Length > 0 && Owner?.ShowMboxDecideWarning("Replace the permanent Android signing key? Existing installations cannot accept updates signed by the replacement.", "Keystore") != MessageBoxResult.Yes) return;
      var key = await AndroidKeystore.CreateAsync(Java, Profile.Product.Publisher, CancellationToken.None); ApplyKey(key);
   });
   private void ApplyKey(ReleaseAndroid key) {
      Android.KeystoreBase64 = key.KeystoreBase64; Android.KeystoreAlias = key.KeystoreAlias;
      Android.KeystorePassword = key.KeystorePassword; Android.KeyPassword = key.KeyPassword; Android.CertificateSha256 = key.CertificateSha256;
      Android.PreparedVersionCode = 0; Android.PreparedApkSha256 = Android.PreparedVersionName = ""; Refresh();
   }
   public Task ImportKeystoreCommand() => Run(async () => {
      var picker = new OpenFileDialog { Filter = "Android keystore|*.jks;*.keystore;*.p12;*.pfx" };
      if (picker.ShowDialog(Owner) != true) return;
      if (Android.KeystoreBase64.Length > 0 && Owner?.ShowMboxDecideWarning("Replace this profile's permanent keystore? Export a backup first.", "Import keystore") != MessageBoxResult.Yes) return;
      ApplyKey(await AndroidKeystore.ImportAsync(Java, picker.FileName, Android.KeystoreAlias, Android.KeystorePassword, Android.KeyPassword, CancellationToken.None));
   });
   public void BrowseSdkCommand() { var picker = new OpenFolderDialog(); if (picker.ShowDialog(Owner) == true) Sdk = picker.FolderName; Refresh(); }
   public void BrowseJavaCommand() { var picker = new OpenFolderDialog(); if (picker.ShowDialog(Owner) == true) Java = picker.FolderName; Refresh(); }
   public void CopyStorePasswordCommand() => Clipboard.SetText(Android.KeystorePassword);
   public void CopyKeyPasswordCommand() => Clipboard.SetText(Android.KeyPassword);
   public Task PrepareEnvironmentCommand() => Run(async () => {
      if (Sdk.Length == 0) Sdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
      var project = Path.GetFullPath(Profile.HostProject, Path.GetDirectoryName(Path.GetFullPath(Profile.SolutionPath))!);
      var checks = await AndroidEnvironment.CheckAsync(Sdk, Java, Profile.Product, CancellationToken.None);
      var commands = AndroidEnvironment.PreparationCommands(checks, project, Profile.Product.AndroidTargetFramework, Sdk, Java, Profile.Product.LatestSupportedJavaVersion);
      if (commands.Count == 0) { Status = "Environment is ready."; return; }
      if (Owner?.ShowMboxDecideWarning(string.Join("\n", commands) + "\n\nInstall these missing components? SDK licenses will be accepted.", "Prepare Environment") != MessageBoxResult.Yes) return;
      var log = new Progress<string>(line => Status += "\n" + line);
      Status = "Preparing Android environment…";
      foreach (var command in commands) {
         if (command.Elevated) {
            var start = new ProcessStartInfo(command.Executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in command.Arguments) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Cannot start elevated environment preparation.");
            await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new IOException("Environment preparation failed with exit code " + process.ExitCode);
         } else await AndroidProcess.CaptureAsync(command.Executable, command.Arguments, null, CancellationToken.None, log);
         if (command.Executable == "winget") { Java = AndroidEnvironment.JavaCandidates("").LastOrDefault() ?? Java; }
      }
      if (SetUserEnvironment) { Environment.SetEnvironmentVariable("ANDROID_HOME", Sdk, EnvironmentVariableTarget.User); Environment.SetEnvironmentVariable("JAVA_HOME", Java, EnvironmentVariableTarget.User); }
      Status += "\n" + string.Join("\n", await AndroidEnvironment.CheckAsync(Sdk, Java, Profile.Product, CancellationToken.None)); Refresh();
   });
}
