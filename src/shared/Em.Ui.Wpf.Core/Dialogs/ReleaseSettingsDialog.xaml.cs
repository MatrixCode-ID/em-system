using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using FontAwesome6;
using Microsoft.Win32;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Clipboard = System.Windows.Clipboard;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// The Release Manager settings dialog: the publish source (<c>.slnx</c>, host project, local publish
   /// folder), the release target (CDN server or folder), and the signing key together with its management.
   /// New settings are saved when the Save button is pressed; Cancel discards the changes. The exception is
   /// signing key operations (import, create, export): those happen directly in the profile folder or the
   /// Windows certificate store, so Cancel does not undo them - only the choice of key is cancelled.
   /// </summary>
   public partial class ReleaseSettingsDialog : EmWindow
   {
      /// <summary>
      /// Creates the Release Manager settings dialog.
      /// </summary>
      /// <param name="app">The application that owns the dialog.</param>
      /// <param name="store">The store of the release profiles.</param>
      /// <param name="profile">The profile being edited.</param>
      /// <param name="lastWriteUtc">The last write time of the profile file when it was read, to detect outside changes.</param>
      /// <param name="others">The other profiles, to warn about collisions.</param>
      /// <param name="secrets">The store of signing key passwords.</param>
      /// <param name="sdkVersion">The .NET SDK 10.x version that was found, or <c>null</c>.</param>
      /// <param name="isSdkChecked"><c>true</c> when the SDK check has finished.</param>
      /// <param name="canUseCdn"><c>true</c> when the user may use the server CDN as a target.</param>
      public ReleaseSettingsDialog(EmApp app, ReleaseProfileStore store, ReleaseProfile profile,
         DateTime lastWriteUtc, IReadOnlyList<ReleaseProfile> others, ReleaseSigningSecrets secrets,
         string? sdkVersion, bool isSdkChecked, bool canUseCdn) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.Load(store, profile, lastWriteUtc, others, secrets, sdkVersion, isSdkChecked, canUseCdn);
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>The view model of this dialog.</summary>
      public ReleaseSettingsDialogVm Vm => (ReleaseSettingsDialogVm)DataContext;
   }

   /// <summary>One signing key installed on this machine, ready to be shown.</summary>
   public class ReleaseSigningKey
   {
      /// <summary>Thumbprint of its certificate.</summary>
      public required string Thumbprint { get; init; }

      /// <summary>Subject of its certificate.</summary>
      public required string Subject { get; init; }

      /// <summary>The <c>keyId</c> of its public key, as in <c>release.json.sig</c>.</summary>
      public required string KeyId { get; init; }

      /// <summary>Validity limit of its certificate.</summary>
      public required DateTime NotAfter { get; init; }

      /// <summary><c>true</c> when its private key may be exported from this machine.</summary>
      public required bool IsExportable { get; init; }

      /// <summary>Tulisan di combobox.</summary>
      public string Caption => $"{Subject.Replace("CN=", "")} · {KeyId}";

      /// <summary>Tulisan chip exportable.</summary>
      public string ExportableCaption => IsExportable ? "Exportable" : "Non-exportable";

      /// <summary>The validity limit in a human-friendly form.</summary>
      public string ExpiresCaption => NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      /// <inheritdoc />
      /// <remarks>The combobox's selection box shows items through this value, not through DisplayMemberPath.</remarks>
      public override string ToString() => Caption;

      /// <summary>Every signing key installed in this user's certificate store.</summary>
      public static IReadOnlyList<ReleaseSigningKey> ListInstalled() {
         var keys = new List<ReleaseSigningKey>();
         foreach (var certificate in SigningCertificates.List()) {
            using (certificate) keys.Add(From(certificate));
         }

         return keys;
      }

      /// <summary>The signing key with thumbprint <paramref name="thumbprint"/>, or <c>null</c> when it is not installed.</summary>
      public static ReleaseSigningKey? Find(string? thumbprint) {
         using var certificate = SigningCertificates.Find(thumbprint);
         return certificate is null ? null : From(certificate);
      }

      /// <summary>The key source that is represented.</summary>
      public ReleaseSigningSource Source { get; init; }

      /// <summary>Model of the public key from the profile file.</summary>
      public static ReleaseSigningKey FromCertificateFile(X509Certificate2 certificate) => new() {
         Thumbprint = certificate.Thumbprint, Subject = certificate.Subject,
         KeyId = SigningCertificates.KeyIdOf(certificate), NotAfter = certificate.NotAfter,
         IsExportable = true, Source = ReleaseSigningSource.ProfileFile
      };

      private static ReleaseSigningKey From(X509Certificate2 certificate) => new() {
         Thumbprint = certificate.Thumbprint,
         Subject = certificate.Subject,
         KeyId = SigningCertificates.KeyIdOf(certificate),
         NotAfter = certificate.NotAfter,
         IsExportable = SigningCertificates.IsExportable(certificate)
      };
   }

   /// <summary>
   /// View model for <see cref="ReleaseSettingsDialog"/>. Holds a copy of the settings being edited; the
   /// profile file is only written by <see cref="SaveCommand"/>.
   /// </summary>
   public class ReleaseSettingsDialogVm : MvvmModelBase
   {
      private string? _sdkVersion;
      private bool _isSdkChecked;
      private ReleaseProfileStore _store = null!;
      private ReleaseProfile _profile = null!;
      private ReleaseSigningSecrets _secrets = null!;
      private DateTime _lastWriteUtc;
      private IReadOnlyList<ReleaseProfile> _others = [];
      /// <summary>The saved profile.</summary>
      public ReleaseProfile? SavedProfile { get; private set; }
      /// <summary>The banner title.</summary>
      public string BannerTitle => $"Release Settings · {_profile?.Name}";
      /// <summary>Indicates store source.</summary>
      public bool IsStoreSource {
         get => Get<bool>();
         set => Set(value, _ => { if (value) SetSource(ReleaseSigningSource.Store); });
      }
      /// <summary>Indicates file source.</summary>
      public bool IsFileSource {
         get => Get<bool>();
         set => Set(value, _ => { if (value) SetSource(ReleaseSigningSource.ProfileFile); });
      }
      /// <summary>Indicates separate password.</summary>
      public bool IsSeparatePassword {
         get => Get<bool>();
         set => Set(value, _ => { if (value && _profile is not null) { _profile.Signing.PasswordStorage = ReleasePasswordStorage.Separate; RefreshPassword(); } });
      }
      /// <summary>Indicates plaintext password.</summary>
      public bool IsPlaintextPassword {
         get => Get<bool>();
         set => Set(value, _ => {
            if (!value || _profile is null) return;
            _profile.Signing.PasswordStorage = ReleasePasswordStorage.Plaintext;
            _profile.Signing.Password ??= _secrets.Get(_profile.Id, _profile.Signing.Thumbprint);
            RefreshPassword();
         });
      }
      /// <summary>The password caption.</summary>
      public string PasswordCaption => IsPlaintextPassword
         ? _profile.Signing.Password is null ? "Not set" : "Saved in profile.json"
         : _secrets.IsRemembered(_profile.Id) ? "Remembered on this PC (encrypted for this Windows user)" : "Asked once per session";
      private void RefreshPassword() {
         NotifyChanged(nameof(PasswordCaption));
         RaiseCommandsChanged();
      }
      private void SetSource(ReleaseSigningSource source) {
         if (_profile is null) return;
         _profile.Signing.Source = source;
         LoadKeys(_profile.Signing.Thumbprint);
      }

      /// <summary>Creates a new view model and registers all commands of the dialog.</summary>
      public ReleaseSettingsDialogVm() {
         RegisterCommand(nameof(BrowseSolutionCommand), BrowseSolutionCommand);
         RegisterCommand(nameof(BrowsePublishFolderCommand), BrowsePublishFolderCommand);
         RegisterCommand(nameof(BrowseTargetFolderCommand), BrowseTargetFolderCommand);
         RegisterCommand(nameof(ImportKeyCommand), ImportKeyCommand);
         RegisterCommand(nameof(CreateKeyCommand), CreateKeyCommand);
         RegisterCommand(nameof(ExportKeyCommand), ExportKeyCommand, ExportKeyCommandAllowed);
         RegisterCommand(nameof(CopyPublicKeyCommand), CopyPublicKeyCommand, CopyPublicKeyCommandAllowed);
         RegisterCommand(nameof(ExportPublicKeyCommand), ExportPublicKeyCommand, ExportPublicKeyCommandAllowed);
         RegisterCommand(nameof(RefreshKeysCommand), RefreshKeysCommand);
         RegisterCommand(nameof(SaveCommand), SaveCommand);
         RegisterCommand(nameof(SetPasswordCommand), SetPasswordCommand, () => IsFileSource && IsPlaintextPassword && _store.HasKeyFile(_profile.Id));
         RegisterCommand(nameof(ForgetPasswordCommand), ForgetPasswordCommand, () => IsFileSource && IsSeparatePassword && _secrets.IsRemembered(_profile.Id));
         RegisterCommand(nameof(RemoveKeyFileCommand), RemoveKeyFileCommand, () => IsFileSource && (_store.HasKeyFile(_profile.Id) || File.Exists(_store.CertificateFilePath(_profile.Id))));
      }

      /// <summary>Raised when the dialog is about to close; <c>true</c> when the settings are saved.</summary>
      public event Action<bool>? RequestClose;

      /// <summary>
      /// Raised for every event worth entering in the Release Manager log, e.g. a signing key that was just
      /// imported.
      /// </summary>
      public event Action<string>? Logged;

      #region Source

      /// <summary>Path file <c>.slnx</c>.</summary>
      public string SolutionPath {
         get => Get<string>() ?? "";
         private set => Set(value, _ => LoadHostProjects());
      }

      /// <summary>The application project in the <c>.slnx</c>, as written there.</summary>
      public ObservableCollection<string> HostProjects { get; } = [];

      /// <summary>The host project that Prepare publishes.</summary>
      public string? HostProject {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>Local publish folder.</summary>
      public string PublishFolder {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Caption of the .NET SDK in the Source card.</summary>
      public string SdkCaption =>
         !_isSdkChecked ? "Checking the .NET SDK..."
         : _sdkVersion is null ? ".NET SDK 10.x was not found on this PC, so Prepare is not available here."
         : $".NET SDK {_sdkVersion} found.";

      /// <summary><c>true</c> when the SDK is not found.</summary>
      public bool IsSdkMissing => _isSdkChecked && _sdkVersion is null;

      #endregion

      #region Target

      /// <summary><c>true</c> when the target is the server CDN.</summary>
      public bool IsCdnTarget {
         get => Get<bool>();
         set => Set(value, _ => {
            if (value) TargetKind = ReleaseTargetKind.Cdn;
         });
      }

      /// <summary><c>true</c> when the target is a folder.</summary>
      public bool IsFolderTarget {
         get => Get<bool>();
         set => Set(value, _ => {
            if (value) TargetKind = ReleaseTargetKind.Folder;
         });
      }

      /// <summary>The kind of target that is chosen.</summary>
      public ReleaseTargetKind TargetKind {
         get => Get<ReleaseTargetKind>();
         private set => Set(value, _ => {
            IsCdnTarget = value == ReleaseTargetKind.Cdn;
            IsFolderTarget = value == ReleaseTargetKind.Folder;
            RefreshTargetState();
         });
      }

      /// <summary>
      /// <c>true</c> when the user may use the server CDN as a target: the CDN actions require the CDN
      /// manager right, which is separate from the Release Manager right.
      /// </summary>
      public bool CanUseCdn {
         get => Get<bool>();
         private set => Set(value, _ => RefreshTargetState());
      }

      /// <summary>The target folder for a folder target.</summary>
      public string TargetFolder {
         get => Get<string>() ?? "";
         private set => Set(value, _ => RefreshTargetState());
      }

      /// <summary>Name of the release folder inside the target.</summary>
      public string ReleaseFolder {
         get => Get<string>() ?? "";
         set => Set(value, _ => RefreshTargetState());
      }

      /// <summary>Caption of the target: the address of its release folder, or why it cannot be used yet.</summary>
      public string TargetCaption => TargetBlockedReason ?? $"Release at {TargetLocation}";

      /// <summary><c>true</c> when <see cref="TargetCaption"/> is a warning.</summary>
      public bool IsTargetBlocked => TargetBlockedReason is not null;

      /// <summary>Caption of the CDN choice.</summary>
      public string CdnOptionTooltip =>
         CanUseCdn ? "Publish to the CDN of the connected server" : "You need CDN Manager access to publish to the server CDN";

      private string? TargetBlockedReason =>
         TargetKind == ReleaseTargetKind.Cdn
            ? !CanUseCdn ? "You need CDN Manager access to publish to the server CDN. Choose Folder instead."
            : EmApp?.ActiveConnection is null ? "No server connection."
            : null
            : TargetFolder.Length == 0 ? "Choose the target folder."
            : !Directory.Exists(TargetFolder) ? "The target folder does not exist."
            : null;

      private string TargetLocation => TargetKind == ReleaseTargetKind.Cdn
         ? $"{EmApp?.ActiveConnection?.Host.TrimEnd('/')}/cdn/{ReleaseTarget.NormalizeReleaseFolder(ReleaseFolder)}/"
         : new FolderReleaseTarget(TargetFolder, ReleaseFolder).Description;

      #endregion

      #region Signing

      /// <summary>The signing keys installed on this machine.</summary>
      public ObservableCollection<ReleaseSigningKey> SigningKeys { get; } = [];

      /// <summary>The signing key used by Sync.</summary>
      public ReleaseSigningKey? SelectedKey {
         get => Get<ReleaseSigningKey?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(HasSigningKey));
            NotifyChanged(nameof(SigningCaption));
            NotifyChanged(nameof(ExportKeyTooltip));
            RaiseCommandsChanged();
         });
      }

      /// <summary><c>true</c> when a signing key is chosen.</summary>
      public bool HasSigningKey => SelectedKey is not null;

      /// <summary>Caption in the Signing key card.</summary>
      public string SigningCaption => IsFileSource && !_store.HasKeyFile(_profile.Id)
         ? "No key file in this profile - create or import one."
         : SelectedKey is null
         ? IsFileSource ? "No key file in this profile - create or import one." : "No signing key - import or create one to publish."
         : $"Thumbprint {SelectedKey.Thumbprint}";

      /// <summary>Tooltip of the Export signing key button.</summary>
      public string ExportKeyTooltip => SelectedKey is { IsExportable: false }
         ? "This key was imported as non-exportable"
         : "Write this key to a password-protected .pfx file";

      #endregion

      #region Commands - source and target

      /// <summary>Chooses the <c>.slnx</c> file.</summary>
      public void BrowseSolutionCommand() {
         var dialog = new OpenFileDialog {
            Title = "Choose the solution",
            Filter = "Solution (*.slnx)|*.slnx",
            CheckFileExists = true
         };
         if (File.Exists(SolutionPath)) dialog.InitialDirectory = Path.GetDirectoryName(SolutionPath);
         if (dialog.ShowDialog(DialogOwner) == true) SolutionPath = dialog.FileName;
      }

      /// <summary>Chooses the local publish folder.</summary>
      public void BrowsePublishFolderCommand() {
         var dialog = new OpenFolderDialog { Title = "Choose the local publish folder" };
         if (Directory.Exists(PublishFolder)) dialog.InitialDirectory = PublishFolder;
         if (dialog.ShowDialog(DialogOwner) == true) PublishFolder = dialog.FolderName;
      }

      /// <summary>Chooses the target folder.</summary>
      public void BrowseTargetFolderCommand() {
         var dialog = new OpenFolderDialog { Title = "Choose the target folder" };
         if (Directory.Exists(TargetFolder)) dialog.InitialDirectory = TargetFolder;
         if (dialog.ShowDialog(DialogOwner) == true) TargetFolder = dialog.FolderName;
      }

      #endregion

      #region Commands - signing key

      /// <summary>Installs a signing key from a <c>.pfx</c> file, then chooses it.</summary>
      public void ImportKeyCommand() {
         var owner = DialogOwner;
         var destination = new SigningKeyDestinationDialog("Import Signing Key", _profile.Signing.Source) { Owner = owner };
         if (destination.ShowDialog() != true) return;
         if (destination.Destination == ReleaseSigningSource.ProfileFile) { ImportFileKey(); return; }
         var file = new OpenFileDialog {
            Title = "Import signing key",
            Filter = "Signing key (*.pfx)|*.pfx|All files|*.*",
            CheckFileExists = true
         };
         if (file.ShowDialog(owner) != true) return;

         var password = new PasswordInputDialog("Import Signing Key",
            $"Password of '{Path.GetFileName(file.FileName)}'.", showExportable: true, okCaption: "Import",
            icon: EFontAwesomeIcon.Solid_FileImport) { Owner = owner };
         if (password.ShowDialog() != true) return;

         try {
            using var certificate = SigningCertificates.Import(file.FileName, password.Vm.Password, password.Vm.Exportable);
            IsStoreSource = true;
            IsFileSource = false;
            LoadKeys(certificate.Thumbprint);
            Logged?.Invoke(
               $"Signing key {SigningCertificates.KeyIdOf(certificate)} imported{(password.Vm.Exportable ? " (exportable)" : "")}.");
            owner?.ShowMboxInfo(
               "The signing key is installed on this machine.\n\n" +
               $"Delete '{file.FileName}' from this machine unless it is where you keep the master copy.",
               "Import Signing Key");
         }
         catch (CryptographicException x) {
            owner?.ShowMboxError(x.Message, "Import Signing Key");
         }
      }

      /// <summary>
      /// Creates a new signing key: a password-protected <c>.pfx</c> file as the master copy, and a key
      /// installed without being exportable on this machine. The new key is chosen right away.
      /// </summary>
      public void CreateKeyCommand() {
         if (DialogOwner is not { } owner) return;
         var destination = new SigningKeyDestinationDialog("Create Signing Key", _profile.Signing.Source) { Owner = owner };
         if (destination.ShowDialog() != true) return;
         if (destination.Destination == ReleaseSigningSource.ProfileFile) { CreateFileKey(); return; }
         if (owner.ShowMboxDecideWarning(
                "Create a new signing key?\n\n" +
                "Launchers only accept releases signed by a key they trust, so every launcher must be given the new public key.\n\n" +
                "The key is saved as a password-protected .pfx file: that file and its password are the master copy - keep both safe.",
                "Create Signing Key") != MessageBoxResult.Yes)
            return;

         var password = new PasswordInputDialog("Create Signing Key", "Password that protects the new .pfx file.",
            requireConfirmation: true, okCaption: "Continue", icon: EFontAwesomeIcon.Solid_Key) { Owner = owner };
         if (password.ShowDialog() != true) return;

         var file = new SaveFileDialog {
            Title = "Save the signing key",
            FileName = "em-release-signing.pfx",
            Filter = "Signing key (*.pfx)|*.pfx"
         };
         if (file.ShowDialog(owner) != true) return;

         try {
            using var certificate = SigningCertificates.Create(file.FileName, password.Vm.Password);
            IsStoreSource = true;
            IsFileSource = false;
            LoadKeys(certificate.Thumbprint);
            Logged?.Invoke($"Signing key {SigningCertificates.KeyIdOf(certificate)} created and saved to {file.FileName}.");
         }
         catch (Exception x) when (x is CryptographicException or IOException or UnauthorizedAccessException) {
            owner.ShowMboxError(x.Message, "Create Signing Key");
         }
      }

      /// <summary>Rewrites the chosen signing key to a <c>.pfx</c> file with a new password.</summary>
      public void ExportKeyCommand() {
         if (SelectedKey is not { } key || DialogOwner is not { } owner) return;

         var sourcePassword = IsFileSource ? AcquireFilePassword() : null;
         if (IsFileSource && sourcePassword is null) return;
         var password = new PasswordInputDialog("Export Signing Key", "Password that protects the exported .pfx file.",
            requireConfirmation: true, okCaption: "Continue", icon: EFontAwesomeIcon.Solid_FileExport) { Owner = owner };
         if (password.ShowDialog() != true) return;

         var file = new SaveFileDialog {
            Title = "Export the signing key",
            FileName = $"em-release-signing-{key.KeyId}.pfx",
            Filter = "Signing key (*.pfx)|*.pfx"
         };
         if (file.ShowDialog(owner) != true) return;

         try {
            if (IsFileSource) SigningCertificates.ExportPfxFile(_store.KeyFilePath(_profile.Id), sourcePassword!, file.FileName, password.Vm.Password);
            else {
               using var certificate = SigningCertificates.Find(key.Thumbprint)
                                       ?? throw new CryptographicException("The signing key is no longer installed on this machine.");
               SigningCertificates.Export(certificate, file.FileName, password.Vm.Password);
            }
            Logged?.Invoke($"Signing key {key.KeyId} exported to {file.FileName}.");
         }
         catch (Exception x) when (x is CryptographicException or IOException or UnauthorizedAccessException) {
            owner.ShowMboxError(x.Message, "Export Signing Key");
         }
      }

      /// <summary>Only for a key that was imported with the exportable option.</summary>
      public bool ExportKeyCommandAllowed() => SelectedKey is { IsExportable: true } && (!IsFileSource || _store.HasKeyFile(_profile.Id));

      /// <summary>Copies the chosen public key (PEM, with the <c>keyId</c> line) to the clipboard.</summary>
      public void CopyPublicKeyCommand() {
         if (PublicKeyPem() is not { } pem) return;
         try {
            Clipboard.SetText(pem.Replace("\n", Environment.NewLine));
            Logged?.Invoke("Public key copied to the clipboard.");
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Only when a signing key is chosen.</summary>
      public bool CopyPublicKeyCommandAllowed() => SelectedKey is not null;

      /// <summary>Saves the chosen public key to a <c>.pem</c> file.</summary>
      public void ExportPublicKeyCommand() {
         if (SelectedKey is not { } key || PublicKeyPem() is not { } pem) return;

         var file = new SaveFileDialog {
            Title = "Export the public key",
            FileName = $"em-release-signing-{key.KeyId}.pem",
            Filter = "Public key (*.pem)|*.pem"
         };
         if (file.ShowDialog(DialogOwner) != true) return;

         try {
            File.WriteAllText(file.FileName, pem);
            Logged?.Invoke($"Public key {key.KeyId} saved to {file.FileName}.");
         }
         catch (Exception x) {
            AlertError(x);
         }
      }

      /// <summary>Only when a signing key is chosen.</summary>
      public bool ExportPublicKeyCommandAllowed() => SelectedKey is not null;

      /// <summary>Reads the list of signing keys from the certificate store again.</summary>
      public void RefreshKeysCommand() => LoadKeys(SelectedKey?.Thumbprint);

      #endregion

      #region Commands - dialog

      /// <summary>Saves all settings to profile.json, then closes the dialog.</summary>
      public void SaveCommand() {
         try {
            var settings = _profile.Clone();
            settings.SolutionPath = SolutionPath;
            settings.HostProject = HostProject ?? "";
            settings.PublishFolder = PublishFolder;
            settings.TargetKind = TargetKind;
            settings.TargetFolder = TargetFolder;
            settings.ReleaseFolder = ReleaseTarget.NormalizeReleaseFolder(ReleaseFolder);
            settings.Signing.Thumbprint = SelectedKey?.Thumbprint ?? "";
            var conflicts = ReleaseProfileConflicts.Find(settings, _others, true, true);
            if (conflicts.Count > 0 && DialogOwner?.ShowMboxDecideWarning(string.Join("\n", conflicts) + "\n\nSave anyway?") != MessageBoxResult.Yes) return;
            try { _store.Save(settings, _lastWriteUtc); }
            catch (ReleaseProfileChangedException) {
               if (DialogOwner?.ShowMboxDecideWarning("profile.json was changed outside Release Manager. Overwrite it?") != MessageBoxResult.Yes) return;
               _store.Save(settings);
            }
            if (IsSeparatePassword && _profile.Signing.Password is { } plaintext) {
               _secrets.Put(settings.Id, settings.Signing.Thumbprint, plaintext, remember: false);
               _profile.Signing.Password = null;
            }
            SavedProfile = settings;
            RequestClose?.Invoke(true);
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message, "Release Settings"); }
      }

      #endregion

      #region Methods

      /// <summary>
      /// Fills the dialog from the stored settings; called once after <see cref="MvvmModelBase.EmApp"/> is set.
      /// </summary>
      /// <param name="store">The store of the release profiles.</param>
      /// <param name="profile">The profile being edited.</param>
      /// <param name="lastWriteUtc">The last write time of the profile file when it was read, to detect outside changes.</param>
      /// <param name="others">The other profiles, to warn about collisions.</param>
      /// <param name="secrets">The store of signing key passwords.</param>
      /// <param name="sdkVersion">The .NET SDK 10.x version that was found, or <c>null</c>.</param>
      /// <param name="isSdkChecked"><c>true</c> when the SDK check has finished.</param>
      /// <param name="canUseCdn"><c>true</c> when the user may use the server CDN as a target.</param>
      public void Load(ReleaseProfileStore store, ReleaseProfile profile, DateTime lastWriteUtc,
         IReadOnlyList<ReleaseProfile> others, ReleaseSigningSecrets secrets,
         string? sdkVersion, bool isSdkChecked, bool canUseCdn) {
         _store = store;
         _profile = profile.Clone();
         _lastWriteUtc = lastWriteUtc;
         _others = others;
         _secrets = secrets;
         _sdkVersion = sdkVersion;
         _isSdkChecked = isSdkChecked;
         var settings = _profile;
         SolutionPath = settings.SolutionPath;
         HostProject = HostProjects.FirstOrDefault(r => string.Equals(r, settings.HostProject, StringComparison.OrdinalIgnoreCase))
                       ?? (settings.HostProject.Length > 0 ? settings.HostProject : null);
         PublishFolder = settings.PublishFolder;
         CanUseCdn = canUseCdn;
         TargetFolder = settings.TargetFolder;
         ReleaseFolder = settings.ReleaseFolder;
         // Through the radio properties, which is also what sets TargetKind when it is the default.
         if (settings.TargetKind == ReleaseTargetKind.Folder) IsFolderTarget = true;
         else IsCdnTarget = true;
         IsSeparatePassword = settings.Signing.PasswordStorage == ReleasePasswordStorage.Separate;
         IsPlaintextPassword = settings.Signing.PasswordStorage == ReleasePasswordStorage.Plaintext;
         IsStoreSource = settings.Signing.Source == ReleaseSigningSource.Store;
         IsFileSource = settings.Signing.Source == ReleaseSigningSource.ProfileFile;
         LoadKeys(settings.Signing.Thumbprint);
         NotifyChanged(nameof(BannerTitle));
         NotifyChanged(nameof(SdkCaption));
         NotifyChanged(nameof(IsSdkMissing));
      }

      private void LoadKeys(string? select) {
         SigningKeys.Clear();
         if (_profile.Signing.Source == ReleaseSigningSource.ProfileFile) {
            using var certificate = SigningCertificates.LoadCertificateFile(_store.CertificateFilePath(_profile.Id));
            SelectedKey = certificate is null ? null : ReleaseSigningKey.FromCertificateFile(certificate);
            if (SelectedKey is not null) _profile.Signing.Thumbprint = SelectedKey.Thumbprint;
            RefreshPassword();
            return;
         }
         foreach (var key in ReleaseSigningKey.ListInstalled()) SigningKeys.Add(key);
         SelectedKey = SigningKeys.FirstOrDefault(r => string.Equals(r.Thumbprint, select, StringComparison.OrdinalIgnoreCase));
      }

      private void LoadHostProjects() {
         HostProjects.Clear();
         if (File.Exists(SolutionPath)) {
            try {
               foreach (var project in ReleaseBuilder.ReadHostProjects(SolutionPath)) HostProjects.Add(project);
            }
            catch (Exception x) when (x is System.Xml.XmlException or IOException) {
               Logged?.Invoke($"The solution could not be read: {x.Message}");
            }
         }

         if (HostProject is not null && !HostProjects.Contains(HostProject, StringComparer.OrdinalIgnoreCase))
            HostProject = null;
      }

      private string? PublicKeyPem() {
         if (SelectedKey is not { } key) return null;
         using var certificate = IsFileSource ? SigningCertificates.LoadCertificateFile(_store.CertificateFilePath(_profile.Id)) : SigningCertificates.Find(key.Thumbprint);
         return certificate is null ? null : SigningCertificates.PublicKeyPem(certificate);
      }

      private bool ConfirmReplaceFile() => !_store.HasKeyFile(_profile.Id) ||
         DialogOwner?.ShowMboxDecideWarning("Replace the signing key file in this profile?", "Signing Key") == MessageBoxResult.Yes;

      private void AcceptFileKey(X509Certificate2 certificate, PasswordInputDialog password) {
         _profile.Signing.Thumbprint = certificate.Thumbprint;
         if (IsPlaintextPassword) _profile.Signing.Password = password.Vm.Password;
         else _secrets.Put(_profile.Id, certificate.Thumbprint, password.Vm.Password, password.Vm.Remember);
         IsFileSource = true;
         IsStoreSource = false;
         LoadKeys(certificate.Thumbprint);
         Logged?.Invoke($"Signing key {SigningCertificates.KeyIdOf(certificate)} saved in profile '{_profile.Name}'.");
      }

      private void ImportFileKey() {
         var file = new OpenFileDialog { Title = "Import signing key", Filter = "Signing key (*.pfx)|*.pfx" };
         if (file.ShowDialog(DialogOwner) != true) return;
         var password = new PasswordInputDialog("Import Signing Key", "Password of the key file.", okCaption: "Import", showRemember: IsSeparatePassword) { Owner = DialogOwner };
         if (password.ShowDialog() != true) return;
         try {
            using var certificate = SigningCertificates.ValidatePfx(file.FileName, password.Vm.Password);
            if (!ConfirmReplaceFile()) return;
            var destination = _store.KeyFilePath(_profile.Id);
            if (!string.Equals(Path.GetFullPath(file.FileName), destination, StringComparison.OrdinalIgnoreCase)) File.Copy(file.FileName, destination, overwrite: true);
            SigningCertificates.WriteCertificateFile(certificate, _store.CertificateFilePath(_profile.Id));
            AcceptFileKey(certificate, password);
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message, "Import Signing Key"); }
      }

      private void CreateFileKey() {
         if (DialogOwner?.ShowMboxDecideWarning("Create a new signing key? Every launcher must trust the new public key.\n\nThe key is saved in this profile folder, which is its master copy. Use Export to keep a backup.", "Create Signing Key") != MessageBoxResult.Yes || !ConfirmReplaceFile()) return;
         var password = new PasswordInputDialog("Create Signing Key", "Password that protects the new .pfx file.", requireConfirmation: true, okCaption: "Create", showRemember: IsSeparatePassword) { Owner = DialogOwner };
         if (password.ShowDialog() != true) return;
         try {
            using var certificate = SigningCertificates.CreatePfx(_store.KeyFilePath(_profile.Id), _store.CertificateFilePath(_profile.Id), password.Vm.Password);
            AcceptFileKey(certificate, password);
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message, "Create Signing Key"); }
      }

      /// <summary>Removes only the profile's key files and the session/DPAPI password.</summary>
      public void RemoveKeyFileCommand() {
         if (DialogOwner?.ShowMboxDecideWarning("Remove signing.pfx and signing.cer from this profile?", "Remove Key File") != MessageBoxResult.Yes) return;
         try {
            File.Delete(_store.KeyFilePath(_profile.Id));
            File.Delete(_store.CertificateFilePath(_profile.Id));
            _secrets.Forget(_profile.Id);
            _profile.Signing.Thumbprint = "";
            _profile.Signing.Password = null;
            LoadKeys(null);
            Logged?.Invoke("Signing key files removed from the profile.");
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      /// <summary>Validates the new password before storing it in the profile draft.</summary>
      public void SetPasswordCommand() {
         var dialog = new PasswordInputDialog("Signing Key Password", "Password of the key file in this profile.", okCaption: "Set") { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;
         try {
            using var certificate = SigningCertificates.ValidatePfx(_store.KeyFilePath(_profile.Id), dialog.Vm.Password);
            _profile.Signing.Password = dialog.Vm.Password;
            RefreshPassword();
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      /// <summary>Runs the forget password command.</summary>
      public void ForgetPasswordCommand() {
         _secrets.Forget(_profile.Id);
         RefreshPassword();
      }

      private string? AcquireFilePassword() => ReleaseProfileKeyAccess.AcquirePassword(_profile, _store, _secrets, DialogOwner, "Export");

      private void RefreshTargetState() {
         NotifyChanged(nameof(TargetCaption));
         NotifyChanged(nameof(IsTargetBlocked));
         NotifyChanged(nameof(CdnOptionTooltip));
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
