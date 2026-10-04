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
   /// Dialog setting Release Manager: sumber publish (<c>.slnx</c>, project host, local publish folder),
   /// tujuan rilis (CDN server atau folder), dan signing key beserta pengelolaannya. Setting baru disimpan
   /// saat tombol Save ditekan; Cancel membuang perubahannya. Pengecualiannya operasi signing key (import,
   /// buat, ekspor): operasi itu langsung terjadi di folder profile atau certificate store Windows, jadi tidak ikut dibatalkan
   /// Cancel - yang dibatalkan hanya pilihan key-nya.
   /// </summary>
   public partial class ReleaseSettingsDialog : EmWindow
   {
      /// <summary>
      /// Membuat dialog setting Release Manager.
      /// </summary>
      /// <param name="app">Aplikasi pemilik dialog.</param>
      /// <param name="sdkVersion">Versi .NET SDK 10.x yang ditemukan, atau <c>null</c>.</param>
      /// <param name="isSdkChecked"><c>true</c> kalau pemeriksaan SDK sudah selesai.</param>
      /// <param name="canUseCdn"><c>true</c> kalau user boleh memakai CDN server sebagai tujuan.</param>
      public ReleaseSettingsDialog(EmApp app, ReleaseProfileStore store, ReleaseProfile profile,
         DateTime lastWriteUtc, IReadOnlyList<ReleaseProfile> others, ReleaseSigningSecrets secrets,
         string? sdkVersion, bool isSdkChecked, bool canUseCdn) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.Load(store, profile, lastWriteUtc, others, secrets, sdkVersion, isSdkChecked, canUseCdn);
         Vm.RequestClose += result => DialogResult = result;
      }

      /// <summary>ViewModel dialog ini.</summary>
      public ReleaseSettingsDialogVm Vm => (ReleaseSettingsDialogVm)DataContext;
   }

   /// <summary>Satu signing key yang terpasang di mesin ini, siap ditampilkan.</summary>
   public class ReleaseSigningKey
   {
      /// <summary>Thumbprint sertifikatnya.</summary>
      public required string Thumbprint { get; init; }

      /// <summary>Subject sertifikatnya.</summary>
      public required string Subject { get; init; }

      /// <summary><c>keyId</c> public key-nya, seperti di <c>release.json.sig</c>.</summary>
      public required string KeyId { get; init; }

      /// <summary>Batas berlaku sertifikatnya.</summary>
      public required DateTime NotAfter { get; init; }

      /// <summary><c>true</c> kalau private key-nya boleh diekspor dari mesin ini.</summary>
      public required bool IsExportable { get; init; }

      /// <summary>Tulisan di combobox.</summary>
      public string Caption => $"{Subject.Replace("CN=", "")} · {KeyId}";

      /// <summary>Tulisan chip exportable.</summary>
      public string ExportableCaption => IsExportable ? "Exportable" : "Non-exportable";

      /// <summary>Batas berlaku yang mudah dibaca.</summary>
      public string ExpiresCaption => NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      /// <inheritdoc />
      /// <remarks>Kotak pilihan combobox menampilkan item lewat nilai ini, bukan lewat DisplayMemberPath.</remarks>
      public override string ToString() => Caption;

      /// <summary>Setiap signing key yang terpasang di certificate store user ini.</summary>
      public static IReadOnlyList<ReleaseSigningKey> ListInstalled() {
         var keys = new List<ReleaseSigningKey>();
         foreach (var certificate in SigningCertificates.List()) {
            using (certificate) keys.Add(From(certificate));
         }

         return keys;
      }

      /// <summary>Signing key dengan thumbprint <paramref name="thumbprint"/>, atau <c>null</c> kalau tidak terpasang.</summary>
      public static ReleaseSigningKey? Find(string? thumbprint) {
         using var certificate = SigningCertificates.Find(thumbprint);
         return certificate is null ? null : From(certificate);
      }

      /// <summary>Sumber key yang diwakili.</summary>
      public ReleaseSigningSource Source { get; init; }

      /// <summary>Model key publik dari berkas profile.</summary>
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
   /// ViewModel untuk <see cref="ReleaseSettingsDialog"/>. Memegang salinan setting yang sedang diedit;
   /// Berkas profile baru ditulis oleh <see cref="SaveCommand"/>.
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
      public ReleaseProfile? SavedProfile { get; private set; }
      public string BannerTitle => $"Release Settings · {_profile?.Name}";
      public bool IsStoreSource {
         get => Get<bool>();
         set => Set(value, _ => { if (value) SetSource(ReleaseSigningSource.Store); });
      }
      public bool IsFileSource {
         get => Get<bool>();
         set => Set(value, _ => { if (value) SetSource(ReleaseSigningSource.ProfileFile); });
      }
      public bool IsSeparatePassword {
         get => Get<bool>();
         set => Set(value, _ => { if (value && _profile is not null) { _profile.Signing.PasswordStorage = ReleasePasswordStorage.Separate; RefreshPassword(); } });
      }
      public bool IsPlaintextPassword {
         get => Get<bool>();
         set => Set(value, _ => {
            if (!value || _profile is null) return;
            _profile.Signing.PasswordStorage = ReleasePasswordStorage.Plaintext;
            _profile.Signing.Password ??= _secrets.Get(_profile.Id, _profile.Signing.Thumbprint);
            RefreshPassword();
         });
      }
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

      /// <summary>Membuat ViewModel baru dan mendaftarkan seluruh command dialog.</summary>
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

      /// <summary>Dipicu saat dialog hendak ditutup; <c>true</c> kalau setting disimpan.</summary>
      public event Action<bool>? RequestClose;

      /// <summary>
      /// Dipicu untuk setiap kejadian yang layak masuk log Release Manager, mis. signing key yang baru
      /// diimpor.
      /// </summary>
      public event Action<string>? Logged;

      #region Source

      /// <summary>Path file <c>.slnx</c>.</summary>
      public string SolutionPath {
         get => Get<string>() ?? "";
         private set => Set(value, _ => LoadHostProjects());
      }

      /// <summary>Project aplikasi di <c>.slnx</c>, seperti tertulis di sana.</summary>
      public ObservableCollection<string> HostProjects { get; } = [];

      /// <summary>Project host yang dipublish Prepare.</summary>
      public string? HostProject {
         get => Get<string?>();
         set => Set(value);
      }

      /// <summary>Local publish folder.</summary>
      public string PublishFolder {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Keterangan .NET SDK di card Source.</summary>
      public string SdkCaption =>
         !_isSdkChecked ? "Checking the .NET SDK..."
         : _sdkVersion is null ? ".NET SDK 10.x was not found on this PC, so Prepare is not available here."
         : $".NET SDK {_sdkVersion} found.";

      /// <summary><c>true</c> kalau SDK tidak ditemukan.</summary>
      public bool IsSdkMissing => _isSdkChecked && _sdkVersion is null;

      #endregion

      #region Target

      /// <summary><c>true</c> kalau tujuannya CDN server.</summary>
      public bool IsCdnTarget {
         get => Get<bool>();
         set => Set(value, _ => {
            if (value) TargetKind = ReleaseTargetKind.Cdn;
         });
      }

      /// <summary><c>true</c> kalau tujuannya folder.</summary>
      public bool IsFolderTarget {
         get => Get<bool>();
         set => Set(value, _ => {
            if (value) TargetKind = ReleaseTargetKind.Folder;
         });
      }

      /// <summary>Jenis tujuan yang dipilih.</summary>
      public ReleaseTargetKind TargetKind {
         get => Get<ReleaseTargetKind>();
         private set => Set(value, _ => {
            IsCdnTarget = value == ReleaseTargetKind.Cdn;
            IsFolderTarget = value == ReleaseTargetKind.Folder;
            RefreshTargetState();
         });
      }

      /// <summary>
      /// <c>true</c> kalau user boleh memakai CDN server sebagai tujuan: action CDN mensyaratkan hak
      /// pengelola CDN, yang terpisah dari hak Release Manager.
      /// </summary>
      public bool CanUseCdn {
         get => Get<bool>();
         private set => Set(value, _ => RefreshTargetState());
      }

      /// <summary>Folder tujuan untuk tujuan folder.</summary>
      public string TargetFolder {
         get => Get<string>() ?? "";
         private set => Set(value, _ => RefreshTargetState());
      }

      /// <summary>Nama folder rilis di dalam tujuan.</summary>
      public string ReleaseFolder {
         get => Get<string>() ?? "";
         set => Set(value, _ => RefreshTargetState());
      }

      /// <summary>Keterangan tujuan: alamat folder rilisnya, atau kenapa belum bisa dipakai.</summary>
      public string TargetCaption => TargetBlockedReason ?? $"Release at {TargetLocation}";

      /// <summary><c>true</c> kalau <see cref="TargetCaption"/> berupa peringatan.</summary>
      public bool IsTargetBlocked => TargetBlockedReason is not null;

      /// <summary>Keterangan pilihan CDN.</summary>
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

      /// <summary>Signing key yang terpasang di mesin ini.</summary>
      public ObservableCollection<ReleaseSigningKey> SigningKeys { get; } = [];

      /// <summary>Signing key yang dipakai Sync.</summary>
      public ReleaseSigningKey? SelectedKey {
         get => Get<ReleaseSigningKey?>();
         set => Set(value, _ => {
            NotifyChanged(nameof(HasSigningKey));
            NotifyChanged(nameof(SigningCaption));
            NotifyChanged(nameof(ExportKeyTooltip));
            RaiseCommandsChanged();
         });
      }

      /// <summary><c>true</c> kalau ada signing key yang dipilih.</summary>
      public bool HasSigningKey => SelectedKey is not null;

      /// <summary>Keterangan di card Signing key.</summary>
      public string SigningCaption => IsFileSource && !_store.HasKeyFile(_profile.Id)
         ? "No key file in this profile - create or import one."
         : SelectedKey is null
         ? IsFileSource ? "No key file in this profile - create or import one." : "No signing key - import or create one to publish."
         : $"Thumbprint {SelectedKey.Thumbprint}";

      /// <summary>Tooltip tombol Export signing key.</summary>
      public string ExportKeyTooltip => SelectedKey is { IsExportable: false }
         ? "This key was imported as non-exportable"
         : "Write this key to a password-protected .pfx file";

      #endregion

      #region Commands - source and target

      /// <summary>Memilih file <c>.slnx</c>.</summary>
      public void BrowseSolutionCommand() {
         var dialog = new OpenFileDialog {
            Title = "Choose the solution",
            Filter = "Solution (*.slnx)|*.slnx",
            CheckFileExists = true
         };
         if (File.Exists(SolutionPath)) dialog.InitialDirectory = Path.GetDirectoryName(SolutionPath);
         if (dialog.ShowDialog(DialogOwner) == true) SolutionPath = dialog.FileName;
      }

      /// <summary>Memilih local publish folder.</summary>
      public void BrowsePublishFolderCommand() {
         var dialog = new OpenFolderDialog { Title = "Choose the local publish folder" };
         if (Directory.Exists(PublishFolder)) dialog.InitialDirectory = PublishFolder;
         if (dialog.ShowDialog(DialogOwner) == true) PublishFolder = dialog.FolderName;
      }

      /// <summary>Memilih folder tujuan.</summary>
      public void BrowseTargetFolderCommand() {
         var dialog = new OpenFolderDialog { Title = "Choose the target folder" };
         if (Directory.Exists(TargetFolder)) dialog.InitialDirectory = TargetFolder;
         if (dialog.ShowDialog(DialogOwner) == true) TargetFolder = dialog.FolderName;
      }

      #endregion

      #region Commands - signing key

      /// <summary>Memasang signing key dari file <c>.pfx</c>, lalu memilihnya.</summary>
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
      /// Membuat signing key baru: file <c>.pfx</c> berpassword sebagai salinan induk, dan key yang
      /// terpasang tanpa bisa diekspor di mesin ini. Key baru langsung dipilih.
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

      /// <summary>Menulis ulang signing key terpilih ke file <c>.pfx</c> dengan password baru.</summary>
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

      /// <summary>Hanya untuk key yang diimpor dengan pilihan exportable.</summary>
      public bool ExportKeyCommandAllowed() => SelectedKey is { IsExportable: true } && (!IsFileSource || _store.HasKeyFile(_profile.Id));

      /// <summary>Menyalin public key terpilih (PEM, dengan baris <c>keyId</c>) ke clipboard.</summary>
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

      /// <summary>Hanya kalau ada signing key yang dipilih.</summary>
      public bool CopyPublicKeyCommandAllowed() => SelectedKey is not null;

      /// <summary>Menyimpan public key terpilih ke file <c>.pem</c>.</summary>
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

      /// <summary>Hanya kalau ada signing key yang dipilih.</summary>
      public bool ExportPublicKeyCommandAllowed() => SelectedKey is not null;

      /// <summary>Membaca ulang daftar signing key dari certificate store.</summary>
      public void RefreshKeysCommand() => LoadKeys(SelectedKey?.Thumbprint);

      #endregion

      #region Commands - dialog

      /// <summary>Menyimpan seluruh setting ke profile.json lalu menutup dialog.</summary>
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
      /// Mengisi dialog dari setting tersimpan; dipanggil sekali setelah <see cref="MvvmModelBase.EmApp"/>
      /// diisi.
      /// </summary>
      /// <param name="sdkVersion">Versi .NET SDK 10.x yang ditemukan, atau <c>null</c>.</param>
      /// <param name="isSdkChecked"><c>true</c> kalau pemeriksaan SDK sudah selesai.</param>
      /// <param name="canUseCdn"><c>true</c> kalau user boleh memakai CDN server sebagai tujuan.</param>
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

      /// <summary>Menghapus hanya berkas key profile serta password sesi/DPAPI.</summary>
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

      /// <summary>Memvalidasi password baru sebelum menyimpannya pada draft profile.</summary>
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
