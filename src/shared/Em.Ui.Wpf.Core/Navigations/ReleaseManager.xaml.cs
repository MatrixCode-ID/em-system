using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using FontAwesome6;
using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Core.Release;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// Layar Release Manager: menyiapkan hasil publish client desktop di mesin ini (Prepare),
   /// membandingkannya dengan rilis di tujuan - CDN server atau folder pilihan - lalu menerbitkannya
   /// satu arah (Sync) dan memeriksa hasilnya (Verify). Format rilis yang ditulis diatur
   /// <c>doc/release-format.md</c>; logikanya ada di namespace <see cref="Em.Ui.Wpf.Core.Release"/>.
   /// </summary>
   public partial class ReleaseManager : UserControl, INavigationBody
   {
      /// <summary>Membuat layar Release Manager untuk aplikasi <paramref name="app"/>.</summary>
      public ReleaseManager(EmApp app) : this() {
         Vm.EmApp = app;
         Vm.Initialize();
      }

      /// <summary>Jalur view tanpa akses Registry produksi, untuk harness.</summary>
      public ReleaseManager() {
         InitializeComponent();
         // Keeping the newest line in view is a view concern with no bindable equivalent.
         logBox.TextChanged += (_, _) => logBox.ScrollToEnd();
      }

      private void ProfileMenuClick(object sender, RoutedEventArgs args) {
         var button = (Button)sender;
         button.ContextMenu.DataContext = Vm;
         button.ContextMenu.PlacementTarget = button;
         button.ContextMenu.IsOpen = true;
      }

      /// <summary>ViewModel layar ini.</summary>
      public ReleaseManagerVm Vm => (ReleaseManagerVm)DataContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (Vm.ConfirmLeave(args.NavigationItem == args.Entry.Navigation)) return Task.CompletedTask;

         args.Cancel = true;
         args.Message = "A Sync is still running.";
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.CancelRunning();
         return Task.CompletedTask;
      }
   }

   /// <summary>Item pilihan profile; entry invalid tidak dapat dipilih.</summary>
   public sealed class ReleaseProfileItem(ReleaseProfileEntry entry)
   {
      public ReleaseProfileEntry Entry { get; } = entry;
      public string Id => Entry.Profile?.Id ?? Path.GetFileName(Entry.Directory);
      public string Name => Entry.Profile?.Name ?? Path.GetFileName(Entry.Directory);
      public string? Error => Entry.Error;
      public bool IsValid => Entry.Profile is not null;
      public string Caption => IsValid ? Name : $"{Name} (invalid: {Error})";
      public override string ToString() => Caption;
   }

   /// <summary>Satu baris daftar perbandingan di <see cref="ReleaseManager"/>.</summary>
   public class ReleaseManagerRow
   {
      internal ReleaseManagerRow(ReleaseDiffItem item) => Item = item;

      /// <summary>Hasil perbandingan yang diwakili baris ini.</summary>
      public ReleaseDiffItem Item { get; }

      /// <summary>Path file relatif terhadap <c>binaries/</c>.</summary>
      public string Path => Item.Path;

      /// <summary>Status file ini.</summary>
      public ReleaseDiffStatus Status => Item.Status;

      /// <summary>Tulisan chip status.</summary>
      public string StatusCaption => Item.Status switch {
         ReleaseDiffStatus.New => "New",
         ReleaseDiffStatus.Changed => "Changed",
         ReleaseDiffStatus.Removed => "Removed",
         _ => "Same"
      };

      /// <summary>Ukuran lokal → ukuran di tujuan, mis. <c>"12 KB → 11 KB"</c>.</summary>
      public string SizeCaption => Item.Status switch {
         ReleaseDiffStatus.Removed => $"– → {ReleaseManagerVm.FormatSize(Item.RemoteSize ?? 0)}",
         ReleaseDiffStatus.New when Item.RemoteSize is null => $"{ReleaseManagerVm.FormatSize(Item.LocalSize ?? 0)} → –",
         _ => $"{ReleaseManagerVm.FormatSize(Item.LocalSize ?? 0)} → {(Item.RemoteSize is { } remote ? ReleaseManagerVm.FormatSize(remote) : "–")}"
      };

      /// <summary>Ukuran persis dalam byte, untuk tooltip.</summary>
      public string SizeTooltip =>
         $"Local: {(Item.LocalSize is { } local ? $"{local:N0} bytes" : "none")} · Target: {(Item.RemoteSize is { } remote ? $"{remote:N0} bytes" : "none")}";
   }

   /// <summary>Satu kelompok daftar perbandingan (modul utama, library extra, runtime .NET).</summary>
   public class ReleaseManagerGroup : NotifyPropertyBase
   {
      internal ReleaseManagerGroup(ReleaseGroup group, string title, bool isExpanded) {
         Group = group;
         Title = title;
         IsExpanded = isExpanded;
      }

      /// <summary>Kelompok yang diwakili.</summary>
      public ReleaseGroup Group { get; }

      /// <summary>Judul kelompok.</summary>
      public string Title { get; }

      /// <summary>Baris yang tampil, sesuai filter "Hide unchanged".</summary>
      public ObservableCollection<ReleaseManagerRow> Rows { get; } = [];

      /// <summary><c>true</c> kalau isi kelompok sedang dibuka.</summary>
      public bool IsExpanded {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Jumlah per status di header kelompok, mis. <c>"3 new · 1 changed · 40 same"</c>.</summary>
      public string CountsCaption {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary><c>true</c> kalau tidak ada baris yang tampil.</summary>
      public bool IsEmpty {
         get => Get<bool>();
         private set => Set(value);
      }

      internal void Fill(IReadOnlyList<ReleaseDiffItem> items, bool hideUnchanged) {
         Rows.Clear();
         foreach (var item in items.Where(r => !hideUnchanged || r.Status != ReleaseDiffStatus.Same))
            Rows.Add(new ReleaseManagerRow(item));

         CountsCaption = ReleaseManagerVm.CountsText(items);
         IsEmpty = Rows.Count == 0;
      }
   }

   /// <summary>
   /// ViewModel untuk <see cref="ReleaseManager"/>. Hanya satu operasi (Prepare, Compare, Sync, Verify)
   /// yang boleh berjalan pada satu waktu, dan setiap operasi bisa dibatalkan. Setting berasal dari profile aktif.
   /// </summary>
   public class ReleaseManagerVm : MvvmModelBase
   {
      private const int MaxLogLines = 3000;
      private const string CdnNavigationName = "admin.cdn";

      private ReleaseManagerPreferences? _preferences;
      private ReleaseProfileStore? _store;
      private ReleaseSigningSecrets _secrets = new();
      private DateTime _profileLastWriteUtc;
      private string? _loadedServerKey;
      private bool _reloadingProfiles;
      private CancellationTokenSource? _cancel;
      private ReleaseTarget? _comparedTarget;
      private readonly List<string> _log = [];

      /// <summary>Membuat ViewModel baru dan mendaftarkan seluruh command layar.</summary>
      public ReleaseManagerVm() {
         RegisterCommand(nameof(NewProfileCommand), NewProfileCommand, () => !IsRunning);
         RegisterCommand(nameof(DuplicateProfileCommand), DuplicateProfileCommand, SettingsCommandAllowed);
         RegisterCommand(nameof(RenameProfileCommand), RenameProfileCommand, SettingsCommandAllowed);
         RegisterCommand(nameof(DeleteProfileCommand), DeleteProfileCommand, SettingsCommandAllowed);
         RegisterCommand(nameof(ImportProfileCommand), ImportProfileCommand, () => !IsRunning);
         RegisterCommand(nameof(ExportProfileCommand), ExportProfileCommand, SettingsCommandAllowed);
         RegisterCommand(nameof(OpenProfilesFolderCommand), OpenProfilesFolderCommand, () => !IsRunning);
         RegisterCommand(nameof(ProfilesFolderCommand), ProfilesFolderCommand, () => !IsRunning);
         RegisterCommand(nameof(RefreshSummaryCommand), RefreshSummaryCommand, () => !IsRunning);
         RegisterCommand(nameof(SettingsCommand), SettingsCommand, SettingsCommandAllowed);
         RegisterCommand(nameof(PrepareCommand), PrepareCommand, PrepareCommandAllowed);
         RegisterCommand(nameof(CompareCommand), CompareCommand, CompareCommandAllowed);
         RegisterCommand(nameof(SyncCommand), SyncCommand, SyncCommandAllowed);
         RegisterCommand(nameof(VerifyCommand), VerifyCommand, VerifyCommandAllowed);
         RegisterCommand(nameof(CancelCommand), CancelCommand, CancelCommandAllowed);
         RegisterCommand(nameof(ClearLogCommand), ClearLogCommand);

         Groups.Add(new ReleaseManagerGroup(ReleaseGroup.MainModules, "Main modules", true));
         Groups.Add(new ReleaseManagerGroup(ReleaseGroup.ExtraLibraries, "Extra libraries", true));
         Groups.Add(new ReleaseManagerGroup(ReleaseGroup.DotNetRuntime, ".NET runtime", false));
      }

      /// <summary>Daftar profile valid dan berkas rusak yang tetap ditampilkan.</summary>
      public ObservableCollection<ReleaseProfileItem> Profiles { get; } = [];
      public ReleaseProfileItem? SelectedProfile {
         get => Get<ReleaseProfileItem?>();
         set {
            if (!_reloadingProfiles && (IsRunning || value is { IsValid: false })) return;
            Set(value, selected => { if (!_reloadingProfiles && value is not null) _ = SelectProfileAsync(value); });
         }
      }
      public ReleaseProfile? Profile {
         get => Get<ReleaseProfile?>();
         private set => Set(value);
      }
      public bool HasProfile => Profile is not null;
      public bool IsEmptyState => !HasProfile && !Profiles.Any(item => item.IsValid);
      public bool CanChangeProfile => !IsRunning;
      public string ProfilesFolder => _store?.Root ?? "";
      public string SigningNote => !HasProfile ? "" : Profile!.Signing.Source == ReleaseSigningSource.Store
         ? SigningKey?.ExportableCaption ?? ""
         : "Key file in profile · " + (Profile.Signing.PasswordStorage == ReleasePasswordStorage.Plaintext ? "password in profile.json"
            : _secrets.IsRemembered(Profile.Id) ? "remembered on this PC" : "asked per session");
      private string CurrentServerKey => ReleaseManagerPreferences.ServerKey(EmApp?.ActiveConnection);
      private IReadOnlyList<ReleaseProfile> OtherProfiles => _store?.List().Where(entry => entry.Profile is not null).Select(entry => entry.Profile!).ToArray() ?? [];

      #region Settings

      /// <summary>Path file <c>.slnx</c>, dari setting.</summary>
      public string SolutionPath {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Project host yang dipublish Prepare, dari setting.</summary>
      public string HostProject {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Local publish folder, dari setting.</summary>
      public string PublishFolder {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Jenis tujuan, dari setting.</summary>
      public ReleaseTargetKind TargetKind {
         get => Get<ReleaseTargetKind>();
         private set => Set(value);
      }

      /// <summary>Folder tujuan untuk tujuan folder, dari setting.</summary>
      public string TargetFolder {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Nama folder rilis di dalam tujuan, dari setting.</summary>
      public string ReleaseFolder {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Signing key yang dipakai Sync, atau <c>null</c> kalau belum ada yang dipilih.</summary>
      public ReleaseSigningKey? SigningKey {
         get => Get<ReleaseSigningKey?>();
         private set => Set(value);
      }

      /// <summary>Versi .NET SDK 10.x yang ditemukan, atau <c>null</c>.</summary>
      public string? SdkVersion {
         get => Get<string?>();
         private set => Set(value, _ => RefreshState());
      }

      /// <summary><c>true</c> setelah pemeriksaan SDK selesai.</summary>
      public bool IsSdkChecked {
         get => Get<bool>();
         private set => Set(value, _ => RefreshState());
      }

      /// <summary>
      /// <c>true</c> kalau user boleh memakai CDN server sebagai tujuan: action CDN mensyaratkan hak
      /// pengelola CDN, yang terpisah dari hak Release Manager.
      /// </summary>
      public bool CanUseCdn {
         get => Get<bool>();
         private set => Set(value, _ => RefreshState());
      }

      /// <summary><c>true</c> kalau local publish folder berisi hasil publish.</summary>
      public bool HasPublishContent {
         get => Get<bool>();
         private set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Kenapa Prepare tidak bisa dijalankan, atau <c>null</c> kalau bisa.</summary>
      public string? PrepareBlockedReason =>
         !IsSdkChecked ? "Checking the .NET SDK..."
         : SdkVersion is null ? ".NET SDK 10.x was not found on this PC, so Prepare is not available. Compare, Sync and Verify still work on an existing publish folder."
         : SolutionPath.Length == 0 || !File.Exists(SolutionPath) ? "Set the .slnx file in Settings to enable Prepare."
         : HostProject.Length == 0 || !File.Exists(HostProjectPath) ? "Set the host project in Settings to enable Prepare."
         : PublishFolder.Length == 0 ? "Set the local publish folder in Settings."
         : null;

      /// <summary>Isi ringkasan Source: local publish folder, atau keterangan kalau belum diisi.</summary>
      public string SourceCaption => !HasProfile ? "No profile" : PublishFolder.Length > 0 ? PublishFolder : "No local publish folder";

      /// <summary>Baris kedua ringkasan Source: kesiapan Prepare.</summary>
      public string SdkCaption => !HasProfile ? "" : PrepareBlockedReason ?? $".NET SDK {SdkVersion} found · ready to prepare";

      /// <summary><c>true</c> kalau <see cref="SdkCaption"/> berupa peringatan.</summary>
      public bool IsPrepareBlocked => HasProfile && PrepareBlockedReason is not null;

      /// <summary>Isi ringkasan Target: alamat folder rilis, atau kenapa tujuan belum bisa dipakai.</summary>
      public string TargetCaption => !HasProfile ? "No profile" : TargetBlockedReason ?? CreateTarget()?.Description ?? "";

      /// <summary>Jenis tujuan untuk ringkasan Target.</summary>
      public string TargetKindCaption => !HasProfile ? "" : TargetKind == ReleaseTargetKind.Cdn ? "Server CDN" : "Folder";

      /// <summary><c>true</c> kalau <see cref="TargetCaption"/> berupa peringatan.</summary>
      public bool IsTargetBlocked => HasProfile && TargetBlockedReason is not null;

      /// <summary>Isi ringkasan Signing key.</summary>
      public string SigningCaption => !HasProfile ? "No profile"
         : Profile!.Signing.Source == ReleaseSigningSource.ProfileFile && !_store!.HasKeyFile(Profile.Id) ? "Key file missing - create or import one in Settings."
         : SigningKey is { } key
         ? $"{key.KeyId} · expires {key.ExpiresCaption}"
         : Profile?.Signing.Source == ReleaseSigningSource.ProfileFile ? "Key file missing - create or import one in Settings." : "No signing key - choose or import one in Settings to publish.";

      /// <summary><c>true</c> kalau ada signing key yang dipilih.</summary>
      public bool HasSigningKey => SigningKey is not null;

      private string HostProjectPath =>
         Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SolutionPath) ?? "", HostProject));

      private string? TargetBlockedReason =>
         TargetKind == ReleaseTargetKind.Cdn
            ? !CanUseCdn ? "You need CDN Manager access to publish to the server CDN. Choose Folder in Settings."
            : EmApp?.ActiveConnection is null ? "No server connection."
            : null
            : TargetFolder.Length == 0 ? "Set the target folder in Settings."
            : !Directory.Exists(TargetFolder) ? "The target folder does not exist."
            : null;

      #endregion

      #region Operation

      /// <summary>Operasi yang sedang berjalan (<c>"Prepare"</c>, <c>"Compare"</c>, ...), atau string kosong.</summary>
      public string RunningOperation {
         get => Get<string>() ?? "";
         private set => Set(value, _ => {
            NotifyChanged(nameof(IsRunning));
            NotifyChanged(nameof(CanChangeProfile));
            NotifyChanged(nameof(IsSyncRunning));
            RaiseCommandsChanged();
         });
      }

      /// <summary><c>true</c> selama sebuah operasi berjalan.</summary>
      public bool IsRunning => RunningOperation.Length > 0;

      /// <summary><c>true</c> selama Sync berjalan.</summary>
      public bool IsSyncRunning => RunningOperation == "Sync";

      /// <summary>Keterangan kemajuan operasi yang berjalan.</summary>
      public string ProgressCaption {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Kemajuan operasi, 0 sampai 100.</summary>
      public double ProgressPercent {
         get => Get<double>();
         private set => Set(value);
      }

      /// <summary><c>true</c> kalau kemajuan tidak bisa dihitung.</summary>
      public bool IsProgressIndeterminate {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>Isi panel log.</summary>
      public string LogText {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Ringkasan Verify terakhir; string kosong kalau belum pernah.</summary>
      public string VerifyCaption {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      /// <summary>Hasil Verify terakhir; <c>null</c> kalau belum pernah.</summary>
      public bool? VerifySucceeded {
         get => Get<bool?>();
         private set => Set(value);
      }

      #endregion

      #region Comparison

      /// <summary>Hasil Compare terakhir, atau <c>null</c>.</summary>
      public ReleaseComparison? Comparison {
         get => Get<ReleaseComparison?>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(HasComparison));
            NotifyChanged(nameof(SummaryCaption));
            NotifyChanged(nameof(UploadCaption));
            NotifyChanged(nameof(TargetReleaseCaption));
            FillGroups();
            RaiseCommandsChanged();
         });
      }

      /// <summary><c>true</c> kalau sudah ada hasil Compare.</summary>
      public bool HasComparison => Comparison is not null;

      /// <summary>Tiga kelompok daftar perbandingan.</summary>
      public ObservableCollection<ReleaseManagerGroup> Groups { get; } = [];

      /// <summary>Sembunyikan file yang sama; aktif secara default.</summary>
      public bool HideUnchanged {
         get => Get<bool>();
         set => Set(value, _ => FillGroups());
      }

      /// <summary>Jumlah per status seluruh file.</summary>
      public string SummaryCaption => Comparison is null ? "" : CountsText(Comparison.Items);

      /// <summary>Total yang akan diunggah dan dihapus.</summary>
      public string UploadCaption => Comparison is null
         ? ""
         : $"Upload {FormatSize(Comparison.UploadBytes)} · delete {Comparison.CountOf(ReleaseDiffStatus.Removed):N0}";

      /// <summary>Keterangan rilis yang ada di tujuan.</summary>
      public string TargetReleaseCaption => Comparison switch {
         null => "",
         { RemoteManifest: { } manifest } =>
            $"Target release {manifest.PublishedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC",
         { RemoteManifestError: not null } => "Target release.json is invalid",
         _ => "No release at the target yet"
      };

      #endregion

      #region Commands - settings

      /// <summary>
      /// Membuka dialog setting (sumber, tujuan, signing key). Setelah disimpan, setting dibaca ulang; kalau
      /// sumber atau tujuannya berubah, hasil Compare lama dibuang lalu dibandingkan ulang.
      /// </summary>
      public async Task SettingsCommand() {
         if (EmApp is not { } app || Profile is null || _store is null) return;

         var dialog = new ReleaseSettingsDialog(app, _store, Profile, _profileLastWriteUtc, OtherProfiles, _secrets, SdkVersion, IsSdkChecked, CanUseCdn) { Owner = DialogOwner };
         dialog.Vm.Logged += Log;
         var saved = dialog.ShowDialog() == true;
         dialog.Vm.Logged -= Log;
         if (!saved) {
            // A key imported or created in the dialog stays installed even when the dialog is canceled, but
            // the selection does not change; the key in use is read again only in case it has gone.
            LoadSigningKey();
            return;
         }

         var before = (PublishFolder, TargetKind, TargetFolder, ReleaseFolder);
         LoadProfiles(Profile.Id);
         if (before != (PublishFolder, TargetKind, TargetFolder, ReleaseFolder)) {
            ResetComparison();
            ResetVerify();
         }

         if (!HasComparison && CompareCommandAllowed()) await CompareCommand();
      }

      /// <summary>Hanya saat tidak ada operasi berjalan.</summary>
      public bool SettingsCommandAllowed() => !IsRunning && HasProfile;

      #endregion

      #region Commands - operations

      /// <summary>
      /// Mengosongkan local publish folder (dengan konfirmasi), menjalankan <c>dotnet publish</c> ke sana,
      /// lalu langsung membandingkannya dengan tujuan.
      /// </summary>
      public async Task PrepareCommand() {
         if (DialogOwner is not { } owner) return;

         if (!ConfirmConflicts(checkTarget: false)) return;
         var folder = PublishFolder;
         var count = await Task.Run(() => ReleaseBuilder.CountItems(folder));
         if (count > 0) {
            if (ReleaseBuilder.CheckPublishFolder(folder, SolutionPath) is { } reason) {
               owner.ShowMboxWarning($"'{folder}' cannot be used as the publish folder: {reason}.", "Prepare");
               return;
            }

            if (owner.ShowMboxDecideWarning(
                   $"Empty '{folder}' ({count:N0} item(s)) and publish into it?\n\nEverything inside it is deleted.",
                   "Prepare") != MessageBoxResult.Yes)
               return;
         }

         await RunAsync("Prepare", async token => {
            ResetComparison();
            Directory.CreateDirectory(folder);
            ShowProgress(new ReleaseProgress("Emptying the publish folder", 0, 0, null, 0, 0));
            await ReleaseBuilder.ClearFolderAsync(folder, token);

            ShowProgress(new ReleaseProgress("Publishing", 0, 0, null, 0, 0));
            var exitCode = await ReleaseBuilder.PublishAsync(SolutionPath, HostProject, folder,
               new Progress<string>(Log), token);
            RefreshPublishContent();
            if (exitCode != 0)
               throw new InvalidOperationException($"dotnet publish failed with exit code {exitCode}. See the log for details.");

            Log("Publish finished.");
            if (TargetBlockedReason is null) {
               RunningOperation = "Compare";
               await CompareCoreAsync(token);
            }
         });
      }

      /// <summary>Hanya kalau SDK, <c>.slnx</c>, project host, dan folder sudah siap.</summary>
      public bool PrepareCommandAllowed() => !IsRunning && HasProfile && PrepareBlockedReason is null;

      /// <summary>Menghitung ulang local publish folder dan membandingkannya dengan tujuan.</summary>
      public Task CompareCommand() => RunAsync("Compare", CompareCoreAsync);

      /// <summary>Hanya kalau local publish folder berisi hasil publish dan tujuannya siap.</summary>
      public bool CompareCommandAllowed() => !IsRunning && HasProfile && HasPublishContent && TargetBlockedReason is null;

      /// <summary>
      /// Menerbitkan hasil Compare terakhir ke tujuan setelah dikonfirmasi, lalu membandingkan ulang.
      /// </summary>
      public async Task SyncCommand() {
         if (Comparison is not { } comparison || _comparedTarget is not { } target || SigningKey is not { } key ||
             DialogOwner is not { } owner)
            return;

         if (!ConfirmConflicts(checkTarget: true)) return;
         var uploads = comparison.CountOf(ReleaseDiffStatus.New) + comparison.CountOf(ReleaseDiffStatus.Changed);
         var removed = comparison.CountOf(ReleaseDiffStatus.Removed);
         var what = comparison.HasChanges
            ? $"Upload {uploads:N0} file(s), {FormatSize(comparison.UploadBytes)}\nDelete {removed:N0} file(s)"
            : "Nothing has changed; release.json is written and signed again.";
         if (owner.ShowMboxDecideWarning(
                $"Profile: {Profile?.Name}\nPublish the release to\n{target.Description}\n\n{what}\n\nSigned with key {key.KeyId}.",
                "Sync") != MessageBoxResult.Yes)
            return;

         X509Certificate2? certificate;
         try {
            certificate = AcquireSigningCertificate();
         }
         catch (Exception x) { owner.ShowMboxError(x.Message, "Sync"); return; }
         if (certificate is null) return;
         using (certificate) {
            using var signingKey = certificate.GetECDsaPrivateKey();
            if (signingKey is null) { owner.ShowMboxError("The signing key has no usable private key.", "Sync"); return; }
            await RunAsync("Sync", async token => {
               try {
                  var manifest = await ReleaseSync.SyncAsync(target, comparison, signingKey, new Progress<ReleaseProgress>(ShowProgress), token);
                  Log($"Sync finished: {manifest.Files.Count:N0} files published to {target.Description}.");
                  ResetVerify();
               }
               catch (ReleaseConflictException x) {
                  ResetComparison();
                  Log(x.Message);
                  owner.ShowMboxWarning(x.Message, "Sync");
                  return;
               }

               RunningOperation = "Compare";
               await CompareCoreAsync(token);
            });
         }
      }

      /// <summary>
      /// Hanya dengan hasil Compare, tujuan yang siap, dan signing key yang dipilih - tanpa key tidak ada
      /// yang bisa diterbitkan.
      /// </summary>
      public bool SyncCommandAllowed() =>
         !IsRunning && HasProfile && Comparison is not null && _comparedTarget is not null && SigningKey is not null &&
         TargetBlockedReason is null && (Profile!.Signing.Source == ReleaseSigningSource.Store || _store!.HasKeyFile(Profile.Id));

      /// <summary>
      /// Memeriksa rilis di tujuan: tanda tangan (dengan public key signing key yang dipilih), lalu ukuran
      /// dan SHA-256 setiap file.
      /// </summary>
      public Task VerifyCommand() => RunAsync("Verify", async token => {
         var target = CreateTarget()!;
         IReadOnlyList<ReadOnlyMemory<byte>> keys = [];
         if (SigningKey is { } key && (Profile?.Signing.Source == ReleaseSigningSource.ProfileFile
            ? SigningCertificates.LoadCertificateFile(_store!.CertificateFilePath(Profile.Id)) : SigningCertificates.Find(key.Thumbprint)) is { } certificate) {
            using (certificate) keys = [SigningCertificates.PublicKeyOf(certificate)];
         }

         Log($"Verifying {target.Description}...");
         var result = await ReleaseVerifier.VerifyAsync(target, keys, new Progress<ReleaseProgress>(ShowProgress), token);
         foreach (var issue in result.Issues.Take(100))
            Log($"  {issue.Kind}: {issue.Path} - {issue.Detail}");
         if (result.Issues.Count > 100) Log($"  ... and {result.Issues.Count - 100:N0} more");
         if (result.FormatError is not null) Log($"  {result.FormatError}");
         Log(result.Summary);

         VerifyCaption = result.Summary;
         VerifySucceeded = result.IsSuccess;
      });

      /// <summary>Hanya kalau tujuannya siap.</summary>
      public bool VerifyCommandAllowed() => !IsRunning && HasProfile && TargetBlockedReason is null;

      /// <summary>Membatalkan operasi yang sedang berjalan.</summary>
      public void CancelCommand() {
         _cancel?.Cancel();
         Log($"Canceling {RunningOperation}...");
         RaiseCommandsChanged();
      }

      /// <summary>Hanya selama operasi berjalan dan belum diminta berhenti.</summary>
      public bool CancelCommandAllowed() => IsRunning && _cancel is { IsCancellationRequested: false };

      /// <summary>Mengosongkan panel log.</summary>
      public void ClearLogCommand() {
         _log.Clear();
         LogText = "";
      }

      #endregion

      #region Methods

      /// <summary>Memuat setting tersimpan; dipanggil sekali setelah <see cref="MvvmModelBase.EmApp"/> diisi.</summary>
      public void Initialize() {
         if (EmApp is null) return;

         var preferences = new ReleaseManagerPreferences(EmApp);
         Initialize(preferences, new ReleaseProfileStore(preferences.ProfilesFolder), new ReleaseSigningSecrets());
         _ = CheckSdkAsync();
      }

      /// <summary>
      /// Dipanggil host setiap kali layar dibuka dan dari tombol Reload: membaca ulang setting, hak CDN,
      /// signing key, dan isi local publish folder, lalu membandingkan otomatis kalau semuanya siap.
      /// </summary>
      public async Task ReloadAsync() {
         if (IsRunning) return;

         // The CDN actions ask for the CDN Manager claim, so the same rule that guards that screen decides
         // whether the CDN can be a target here.
         CanUseCdn = EmApp is { } app && app.Navigations.FirstOrDefault(r => r.Name == CdnNavigationName) is { } cdn &&
                     app.CanOpen(cdn);
         LoadProfiles(CurrentServerKey == _loadedServerKey ? SelectedProfile?.Id : null);
         if (CompareCommandAllowed()) await CompareCommand();
      }

      /// <summary>
      /// Apakah layar boleh ditinggalkan. Selama Sync berjalan, menutup layar - atau berpindah layar di
      /// layout satu halaman, yang bisa melepas layar ini - ditolak, dengan tawaran untuk menghentikan
      /// Sync-nya. Di layout multi-tab, berpindah tab tidak menghentikan apa pun, jadi dibiarkan.
      /// </summary>
      /// <param name="closing"><c>true</c> kalau entri layar ini sendiri yang hendak ditutup.</param>
      public bool ConfirmLeave(bool closing) {
         if (!IsSyncRunning) return true;
         if (!closing && EmApp?.ApplicationLayout == ApplicationLayout.MultiTab) return true;

         if (DialogOwner?.ShowMboxDecideWarning("A Sync is still running. Stop it?\n\n" +
                                                "The screen stays open until the Sync has stopped.", "Sync") ==
             MessageBoxResult.Yes)
            CancelCommand();
         return false;
      }

      /// <summary>Membatalkan operasi yang berjalan, tanpa bertanya.</summary>
      public void CancelRunning() => _cancel?.Cancel();

      internal static string CountsText(IReadOnlyCollection<ReleaseDiffItem> items) {
         var parts = new List<string>();
         Add(ReleaseDiffStatus.New, "new");
         Add(ReleaseDiffStatus.Changed, "changed");
         Add(ReleaseDiffStatus.Removed, "removed");
         Add(ReleaseDiffStatus.Same, "same");
         return parts.Count == 0 ? "no files" : string.Join(" · ", parts);

         void Add(ReleaseDiffStatus status, string caption) {
            var count = items.Count(r => r.Status == status);
            if (count > 0) parts.Add($"{count:N0} {caption}");
         }
      }

      internal static string FormatSize(long bytes) => CdnManagerVm.FormatSize(bytes);

      private async Task CompareCoreAsync(CancellationToken token) {
         var target = CreateTarget() ?? throw new InvalidOperationException(TargetBlockedReason);
         ResetComparison();
         Log($"Comparing {PublishFolder} with {target.Description}...");

         // Asked first for the CDN, so a server with the CDN switched off is reported as such rather than
         // as an empty release.
         await target.GetMaxFileSizeAsync(token);
         var local = await LocalPublish.ScanAsync(PublishFolder, new Progress<ReleaseProgress>(ShowProgress), token);
         ShowProgress(new ReleaseProgress("Reading the target", 0, 0, null, 0, 0));
         var comparison = await ReleaseComparer.CompareAsync(target, local, token);

         _comparedTarget = target;
         Comparison = comparison;
         if (comparison.RemoteManifestError is not null) Log($"The target release.json is not valid: {comparison.RemoteManifestError}");
         Log($"Compared {local.Files.Count:N0} files ({FormatSize(local.TotalSize)}): {CountsText(comparison.Items)}.");
         if (local.Grouping.DepsFile is null) Log("No deps.json found: every file is listed under Extra libraries.");
      }

      private ReleaseTarget? CreateTarget() {
         if (TargetBlockedReason is not null || EmApp is not { } app) return null;

         return TargetKind == ReleaseTargetKind.Cdn
            ? new CdnReleaseTarget(app.ServiceProvider.GetRequiredService<ICdnServices>(), app.ActiveConnection!, ReleaseFolder)
            : new FolderReleaseTarget(TargetFolder, ReleaseFolder);
      }

      // One operation at a time; every error ends up in the log and in a message box, a cancel only in
      // the log.
      private async Task RunAsync(string operation, Func<CancellationToken, Task> work) {
         if (IsRunning) return;

         using var cancel = new CancellationTokenSource();
         _cancel = cancel;
         RunningOperation = operation;
         if (operation == "Verify") ResetVerify();
         try {
            await work(cancel.Token);
         }
         catch (Exception) when (cancel.IsCancellationRequested) {
            Log($"{RunningOperation} canceled.");
         }
         catch (Exception x) {
            Log($"{RunningOperation} failed: {x.Message}");
            AlertError(x);
         }
         finally {
            _cancel = null;
            RunningOperation = "";
            ProgressCaption = "";
            ProgressPercent = 0;
            IsProgressIndeterminate = false;
         }
      }

      private void ShowProgress(ReleaseProgress progress) {
         var parts = new List<string> { progress.Stage };
         if (progress.FileCount > 0) parts.Add($"{progress.FileIndex:N0} of {progress.FileCount:N0}");
         if (progress.BytesTotal > 0) parts.Add($"{FormatSize(progress.BytesDone)} of {FormatSize(progress.BytesTotal)}");
         if (progress.CurrentPath is not null) parts.Add(progress.CurrentPath);
         ProgressCaption = string.Join(" · ", parts);

         IsProgressIndeterminate = progress.BytesTotal == 0 && progress.FileCount == 0;
         ProgressPercent = progress.BytesTotal > 0 ? progress.BytesDone * 100d / progress.BytesTotal
            : progress.FileCount > 0 ? progress.FileIndex * 100d / progress.FileCount
            : 0;
      }

      private void Log(string line) {
         _log.Add($"{DateTime.Now:HH:mm:ss}  {line}");
         if (_log.Count > MaxLogLines) _log.RemoveRange(0, _log.Count - MaxLogLines);
         LogText = string.Join(Environment.NewLine, _log);
      }

      private void ApplySettings() {
         SolutionPath = Profile?.SolutionPath ?? "";
         HostProject = Profile?.HostProject ?? "";
         PublishFolder = Profile?.PublishFolder ?? "";
         TargetKind = Profile?.TargetKind ?? ReleaseTargetKind.Cdn;
         TargetFolder = Profile?.TargetFolder ?? "";
         ReleaseFolder = Profile?.ReleaseFolder ?? ReleaseLayout.DefaultReleaseFolder;
         LoadSigningKey();
         RefreshPublishContent();
         RefreshState();
      }

      private void LoadSigningKey() {
         SigningKey = null;
         if (Profile is { } profile && _store is not null) {
            var before = profile.Signing.Thumbprint;
            if (profile.Signing.Source == ReleaseSigningSource.Store) {
               SigningKey = ReleaseSigningKey.Find(before);
               if (SigningKey is null && before.Length > 0) profile.Signing.Thumbprint = "";
            }
            else {
               using var certificate = SigningCertificates.LoadCertificateFile(_store.CertificateFilePath(profile.Id));
               if (certificate is not null) {
                  SigningKey = ReleaseSigningKey.FromCertificateFile(certificate);
                  profile.Signing.Thumbprint = certificate.Thumbprint;
               }
            }
            if (!string.Equals(before, profile.Signing.Thumbprint, StringComparison.OrdinalIgnoreCase)) {
               try {
                  _profileLastWriteUtc = _store.Save(profile, _profileLastWriteUtc).LastWriteUtc;
                  Log("Signing key information updated from its source.");
               }
               catch (Exception x) { Log($"Could not save signing key information: {x.Message}"); }
            }
         }
         RefreshState();
      }

      /// <summary>Inisialisasi terisolasi untuk harness tanpa membaca Registry produksi.</summary>
      public void Initialize(ReleaseManagerPreferences preferences, ReleaseProfileStore store, ReleaseSigningSecrets secrets) {
         _preferences = preferences;
         _store = store;
         _secrets = secrets;
         HideUnchanged = true;
         LoadProfiles();
      }

      public void LoadProfiles(string? selectId = null) {
         if (_store is null || _preferences is null) return;
         try {
            var entries = _store.List();
            if (!entries.Any(entry => entry.Profile is not null) && _preferences.ReadLegacyProfile() is { } legacy) {
               _store.Save(legacy);
               _preferences.DeleteLegacySettings();
               Log("Settings from the previous version were moved to profile 'Default'.");
               entries = _store.List();
            }
            _reloadingProfiles = true;
            try {
               Profiles.Clear();
               foreach (var entry in entries) Profiles.Add(new ReleaseProfileItem(entry));
               var remembered = _preferences.GetLastProfile(CurrentServerKey);
               SelectedProfile = Profiles.FirstOrDefault(item => item.IsValid && item.Id == selectId)
                  ?? Profiles.FirstOrDefault(item => item.IsValid && item.Id == remembered)
                  ?? Profiles.FirstOrDefault(item => item.IsValid);
            }
            finally { _reloadingProfiles = false; }
            ResetComparison();
            ResetVerify();
            Profile = SelectedProfile is { } selected ? _store.Load(selected.Id) : null;
            _profileLastWriteUtc = SelectedProfile?.Entry.LastWriteUtc ?? DateTime.MinValue;
            _loadedServerKey = CurrentServerKey;
            if (Profile is not null) _preferences.SetLastProfile(CurrentServerKey, Profile.Id);
            ApplySettings();
         }
         catch (Exception x) {
            Profile = null;
            ResetComparison();
            ResetVerify();
            ApplySettings();
            Log($"Could not load release profiles: {x.Message}");
            DialogOwner?.ShowMboxError(x.Message);
         }
         NotifyChanged(nameof(HasProfile));
         NotifyChanged(nameof(IsEmptyState));
         NotifyChanged(nameof(ProfilesFolder));
         RaiseCommandsChanged();
      }

      private async Task SelectProfileAsync(ReleaseProfileItem item) {
         if (IsRunning || !item.IsValid || _store is null || _preferences is null) return;
         try {
            Profile = _store.Load(item.Id);
            _profileLastWriteUtc = File.GetLastWriteTimeUtc(Path.Combine(_store.DirectoryOf(item.Id), ReleaseProfileStore.ProfileFileName));
            _preferences.SetLastProfile(CurrentServerKey, item.Id);
            _loadedServerKey = CurrentServerKey;
            ResetComparison();
            ResetVerify();
            ApplySettings();
            NotifyChanged(nameof(HasProfile));
            NotifyChanged(nameof(IsEmptyState));
            if (CompareCommandAllowed()) await CompareCommand();
         }
         catch (Exception x) {
            DialogOwner?.ShowMboxError(x.Message);
            LoadProfiles();
         }
      }

      private bool ConfirmConflicts(bool checkTarget) {
         if (Profile is null) return false;
         var conflicts = ReleaseProfileConflicts.Find(Profile, OtherProfiles, checkTarget, true);
         return conflicts.Count == 0 || DialogOwner?.ShowMboxDecideWarning(string.Join("\n", conflicts) + "\n\nContinue?") == MessageBoxResult.Yes;
      }

      private X509Certificate2? AcquireSigningCertificate() {
         if (Profile is null || _store is null) return null;
         if (Profile.Signing.Source == ReleaseSigningSource.Store) return SigningCertificates.Find(Profile.Signing.Thumbprint)
            ?? throw new CryptographicException("The signing key is no longer installed on this machine.");
         var password = ReleaseProfileKeyAccess.AcquirePassword(Profile, _store, _secrets, DialogOwner, "Sign");
         return password is null ? null : SigningCertificates.LoadPfxForSigning(_store.KeyFilePath(Profile.Id), password);
      }

      public async Task NewProfileCommand() {
         if (_store is null) return;
         while (true) {
            var dialog = new TextInputDialog("New Release Profile", "Name of the new profile.", okCaption: "Create", icon: EFontAwesomeIcon.Solid_Plus) { Owner = DialogOwner };
            if (dialog.ShowDialog() != true) return;
            try {
               var profile = _store.Create(dialog.Vm.Result);
               LoadProfiles(profile.Id);
               await SettingsCommand();
               return;
            }
            catch (InvalidOperationException x) { DialogOwner?.ShowMboxWarning(x.Message); }
            catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); return; }
         }
      }

      public void DuplicateProfileCommand() {
         if (_store is null || Profile is null) return;
         try {
            var copy = _store.Duplicate(Profile.Id);
            LoadProfiles(copy.Id);
            Log($"Profile '{copy.Name}' duplicated.");
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      public void RenameProfileCommand() {
         if (_store is null || Profile is null) return;
         var dialog = new TextInputDialog("Rename Release Profile", "Name of this profile.", okCaption: "Rename", initialText: Profile.Name) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;
         try { _store.Rename(Profile.Id, dialog.Vm.Result); LoadProfiles(Profile.Id); }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      public void DeleteProfileCommand() {
         if (_store is null || Profile is null) return;
         if (DialogOwner?.ShowMboxDecideWarning($"Delete profile '{Profile.Name}'?\n\nOnly the profile folder is deleted (settings, and its key file if any). The local publish folder, the release at the target and keys in the Windows certificate store are not touched.") != MessageBoxResult.Yes) return;
         try {
            var name = Profile.Name;
            _store.Delete(Profile.Id);
            _secrets.Forget(Profile.Id);
            LoadProfiles();
            Log($"Profile '{name}' deleted.");
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      public void ImportProfileCommand() {
         if (_store is null) return;
         var picker = new OpenFileDialog { Filter = "Release profile (*.zip;*.json)|*.zip;*.json" };
         if (picker.ShowDialog(DialogOwner) != true) return;
         try {
            ReleaseProfile imported;
            try { imported = _store.Import(picker.FileName, false); }
            catch (ReleaseProfileConflictException) {
               if (DialogOwner?.ShowMboxDecideWarning("A profile with the same id already exists. Import it as a new profile?") != MessageBoxResult.Yes) return;
               imported = _store.Import(picker.FileName, true);
            }
            LoadProfiles(imported.Id);
            Log($"Profile '{imported.Name}' imported.");
            if (imported.Signing.Source == ReleaseSigningSource.ProfileFile && !_store.HasKeyFile(imported.Id))
               DialogOwner?.ShowMboxInfo("The key file is missing. Import it in Settings before signing releases.");
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      public void ExportProfileCommand() {
         if (_store is null || Profile is null || DialogOwner is not { } owner) return;
         var sensitive = owner.ShowMboxDecideWarning("Include sensitive data?\n\nNo (default): only settings and the public certificate. Yes: also the key file and a plain-text password, if this profile has them.", "Export Profile") == MessageBoxResult.Yes;
         var name = string.Concat(Profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
         var picker = new SaveFileDialog { Filter = "Release profile (*.zip)|*.zip", FileName = name + ".zip" };
         if (picker.ShowDialog(owner) != true) return;
         try { _store.Export(Profile.Id, picker.FileName, sensitive); Log($"Profile '{Profile.Name}' exported."); }
         catch (Exception x) { owner.ShowMboxError(x.Message); }
      }

      public void OpenProfilesFolderCommand() {
         if (_store is null) return;
         try {
            Directory.CreateDirectory(_store.Root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Root}\"") { UseShellExecute = true });
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      public async Task ProfilesFolderCommand() {
         if (_preferences is null || _store is null) return;
         var dialog = new ReleaseProfilesFolderDialog(_store.Root) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true || string.Equals(_store.Root, dialog.SelectedFolder, StringComparison.OrdinalIgnoreCase)) return;
         try {
            _preferences.ProfilesFolder = dialog.SelectedFolder;
            _store = new ReleaseProfileStore(dialog.SelectedFolder);
            LoadProfiles();
            if (CompareCommandAllowed()) await CompareCommand();
         }
         catch (Exception x) { DialogOwner?.ShowMboxError(x.Message); }
      }

      public void RefreshSummaryCommand() {
         if (!IsRunning) LoadProfiles(SelectedProfile?.Id);
      }

      private async Task CheckSdkAsync() {
         SdkVersion = await ReleaseBuilder.FindSdkAsync(CancellationToken.None);
         IsSdkChecked = true;
      }

      private void FillGroups() {
         var items = Comparison?.Items ?? [];
         foreach (var group in Groups)
            group.Fill(items.Where(r => r.Group == group.Group).ToArray(), HideUnchanged);
      }

      private void ResetComparison() {
         if (Comparison is null && _comparedTarget is null) return;
         _comparedTarget = null;
         Comparison = null;
      }

      // A Verify result describes one release at one target; a new target or a new release makes it stale.
      private void ResetVerify() {
         VerifyCaption = "";
         VerifySucceeded = null;
      }

      private void RefreshPublishContent() => HasPublishContent = LocalPublish.HasContent(PublishFolder);

      private void RefreshState() {
         NotifyChanged(nameof(PrepareBlockedReason));
         NotifyChanged(nameof(SourceCaption));
         NotifyChanged(nameof(SdkCaption));
         NotifyChanged(nameof(IsPrepareBlocked));
         NotifyChanged(nameof(TargetCaption));
         NotifyChanged(nameof(TargetKindCaption));
         NotifyChanged(nameof(IsTargetBlocked));
         NotifyChanged(nameof(SigningCaption));
         NotifyChanged(nameof(HasSigningKey));
         NotifyChanged(nameof(SigningNote));
         RaiseCommandsChanged();
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
