using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FontAwesome6;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   /// <summary>
   /// Layar pengelola container registry: root, folder dan container (tiga panel).
   /// Robot dan haknya dikelola di UserManager. Server yang registry-nya tidak dinyalakan menjawab 404; layar ini
   /// lalu hanya menampilkan keadaan "tidak dinyalakan".
   /// </summary>
   public partial class ContainerManager : UserControl, INavigationBody
   {
      /// <summary>
      /// Membuat layar pengelola container registry untuk aplikasi <paramref name="app"/>.
      /// </summary>
      public ContainerManager(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         settingsCard.Attach(app, false);
         publisher.Attach(app,Em.Ui.Wpf.Publish.PublishKind.Container);
         // A right click does not select what it lands on, and every context-menu command works on the
         // selection, so the row under the pointer is selected first.
         ctnTree.PreviewMouseRightButtonDown += (_, e) => SelectUnderPointer(e.OriginalSource);
         rootList.PreviewMouseRightButtonDown += (_, e) => SelectUnderPointer(e.OriginalSource);
      }

      /// <summary>ViewModel layar ini.</summary>
      public ContainerManagerVm Vm => (ContainerManagerVm)DataContext;

      private static void SelectUnderPointer(object? source) {
         var node = source as DependencyObject;
         while (node is not null) {
            switch (node) {
               case TreeViewItem treeItem:
                  treeItem.IsSelected = true;
                  treeItem.Focus();
                  return;
               case ListBoxItem listItem:
                  listItem.IsSelected = true;
                  listItem.Focus();
                  return;
            }

            node = node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
         }
      }

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (!settingsCard.ConfirmLeave()) args.Cancel = true;
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>Satu root di daftar kiri tab Containers.</summary>
   public class CtnRootItem
   {
      internal CtnRootItem(CtnRootInfo info) {
         Info = info;
      }

      /// <summary>Data root apa adanya dari server.</summary>
      public CtnRootInfo Info { get; }

      /// <summary>Id root.</summary>
      public string Id => Info.Id;

      /// <summary>Nama root, bagian pertama nama pull.</summary>
      public string Name => Info.Name;

      /// <summary>Deskripsi; string kosong kalau tidak ada.</summary>
      public string Description => Info.Description ?? "";

      /// <summary><c>true</c> kalau deskripsi ada.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Info.Description);

      /// <summary>Status aktif.</summary>
      public bool IsActive => Info.IsActive;

      /// <summary>Jumlah container, untuk lencana di daftar.</summary>
      public int ImageCount => Info.ImageCount;

      /// <summary>Jumlah folder dan container, mis. <c>"3 folders · 12 containers"</c>.</summary>
      public string Summary =>
         $"{Info.FolderCount:N0} folder{(Info.FolderCount == 1 ? "" : "s")} · {Info.ImageCount:N0} container{(Info.ImageCount == 1 ? "" : "s")}";

      /// <summary>Waktu dibuat dalam waktu lokal.</summary>
      public string CreatedCaption => ContainerManagerVm.LocalTime(Info.CreatedAt);
   }

   /// <summary>
   /// Satu simpul tree di tengah tab Containers: sebuah folder atau sebuah container. Server mengirim tree
   /// datar; <see cref="ContainerManagerVm"/> menyusunnya menjadi simpul-simpul ini. Simpul ini juga muatan
   /// drag untuk memindahkan folder atau container ke folder lain.
   /// </summary>
   public class CtnTreeNode : NotifyPropertyBase
   {
      private readonly ContainerManagerVm _owner;

      internal CtnTreeNode(CtnFolderInfo folder, CtnTreeNode? parent, ContainerManagerVm owner) {
         Folder = folder;
         Parent = parent;
         _owner = owner;
      }

      internal CtnTreeNode(CtnImageInfo image, CtnTreeNode? parent, ContainerManagerVm owner) {
         Image = image;
         Parent = parent;
         _owner = owner;
      }

      /// <summary>Data folder; <c>null</c> untuk simpul container.</summary>
      public CtnFolderInfo? Folder { get; }

      /// <summary>Data container; <c>null</c> untuk simpul folder.</summary>
      public CtnImageInfo? Image { get; }

      /// <summary>Folder induk; <c>null</c> kalau langsung di root.</summary>
      public CtnTreeNode? Parent { get; }

      /// <summary>Isi simpul folder: folder lebih dulu, lalu container, masing-masing urut nama.</summary>
      public ObservableCollection<CtnTreeNode> Children { get; } = [];

      /// <summary><c>true</c> untuk folder.</summary>
      public bool IsFolder => Folder is not null;

      /// <summary>Id folder atau container.</summary>
      public string Id => Folder?.Id ?? Image!.Id;

      /// <summary>Nama folder atau container.</summary>
      public string Name => Folder?.Name ?? Image!.Name;

      /// <summary>Deskripsi container; string kosong untuk folder atau kalau tidak ada.</summary>
      public string Description => Image?.Description ?? "";

      /// <summary><c>true</c> kalau <see cref="Description"/> ada isinya.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Image?.Description);

      /// <summary>Waktu container dibuat dalam waktu lokal; kosong untuk folder.</summary>
      public string CreatedCaption => Image is null ? "" : ContainerManagerVm.LocalTime(Image.CreatedAt);

      /// <summary>Folder atau container yang aktif; folder selalu aktif.</summary>
      public bool IsActive => Image?.IsActive ?? true;

      /// <summary>Ikon simpul: folder atau kotak container.</summary>
      public EFontAwesomeIcon Icon => IsFolder
         ? (IsExpanded ? EFontAwesomeIcon.Solid_FolderOpen : EFontAwesomeIcon.Solid_Folder)
         : EFontAwesomeIcon.Solid_Box;

      /// <summary>Keterangan di kanan nama: jumlah tag container, atau isi folder.</summary>
      public string Caption => IsFolder
         ? (Children.Count == 0 ? "empty" : $"{Children.Count:N0} item{(Children.Count == 1 ? "" : "s")}")
         : $"{Image!.TagCount:N0} tag{(Image.TagCount == 1 ? "" : "s")}";

      /// <summary>Kedalaman simpul; folder yang langsung di root bernilai 1.</summary>
      public int Depth => Parent is null ? 1 : Parent.Depth + 1;

      /// <summary>Jalur folder dari root, mis. <c>"services / api"</c>.</summary>
      public string Path => Parent is null ? Name : $"{Parent.Path} / {Name}";

      /// <summary>Seluruh simpul di bawah simpul ini, tidak termasuk dirinya.</summary>
      public IEnumerable<CtnTreeNode> Descendants() {
         foreach (var child in Children) {
            yield return child;
            foreach (var deeper in child.Descendants()) yield return deeper;
         }
      }

      /// <summary><c>true</c> kalau <paramref name="other"/> ada di bawah simpul ini, di kedalaman mana pun.</summary>
      public bool Contains(CtnTreeNode other) {
         for (var node = other.Parent; node is not null; node = node.Parent) {
            if (node == this) return true;
         }

         return false;
      }

      /// <summary>Folder terbuka atau tertutup.</summary>
      public bool IsExpanded {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(Icon)));
      }

      /// <summary>
      /// <c>true</c> kalau container ini terbukti punya blob yang hilang dari storage server. Baru diketahui
      /// setelah manifest container dibaca (saat dipilih), dan hilang lagi saat tree dibaca ulang.
      /// </summary>
      public bool HasMissingBlobs {
         get => Get<bool>();
         internal set => Set(value);
      }

      /// <summary>Simpul ini yang dipilih; dipasang dua arah oleh gaya item TreeView.</summary>
      public bool IsSelected {
         get => Get<bool>();
         set => Set(value, selected => {
            if (selected) _owner.OnNodeSelected(this);
            else _owner.OnNodeDeselected(this);
         });
      }

      internal void RefreshCaption() => NotifyChanged(nameof(Caption));
   }

   /// <summary>Satu manifest di detail container: tag, digest, media type, ukuran, dan siapa yang mengirim.</summary>
   public class CtnManifestItem
   {
      internal CtnManifestItem(CtnManifestInfo info) {
         Info = info;
      }

      /// <summary>Data manifest apa adanya dari server.</summary>
      public CtnManifestInfo Info { get; }

      /// <summary>Digest lengkap.</summary>
      public string Digest => Info.Digest;

      /// <summary>Digest yang dipotong untuk tampilan.</summary>
      public string ShortDigest => CtnInput.ShortDigest(Info.Digest);

      /// <summary>Media type manifest.</summary>
      public string MediaType => Info.MediaType;

      /// <summary>Tag yang menunjuk manifest ini.</summary>
      public string[] Tags => Info.Tags;

      /// <summary><c>true</c> kalau ada tag yang menunjuk manifest ini.</summary>
      public bool HasTags => Info.Tags.Length > 0;

      /// <summary>
      /// Ukuran manifest itu sendiri - bukan ukuran image atau layer-nya, yang tidak dilaporkan server.
      /// </summary>
      public string SizeCaption => $"Manifest size {CdnManagerVm.FormatSize(Info.Size)}";

      /// <summary><c>true</c> kalau ada blob manifest ini yang berkasnya tidak ada di storage server.</summary>
      public bool HasMissingBlobs => Info.MissingBlobCount > 0;

      /// <summary>Peringatan blob hilang; kosong kalau semua blob ada.</summary>
      public string MissingBlobCaption => Info.MissingBlobCount switch {
         0 => "",
         var missing => $"{missing} of {Info.BlobCount} blob(s) missing from server storage - this image cannot be pulled " +
                        "until the files are restored. The database may be shared with a server whose storage holds them."
      };

      /// <summary>Satu baris keterangan: ukuran manifest, waktu push, dan pengirimnya.</summary>
      public string MetaCaption => $"{SizeCaption} · pushed {PushedCaption} · {PushedByCaption}";

      /// <summary>Waktu push dalam waktu lokal.</summary>
      public string PushedCaption => ContainerManagerVm.LocalTime(Info.PushedAt);

      /// <summary>Siapa yang mengirim: nama robot, atau "deleted robot" kalau robotnya sudah dihapus.</summary>
      public string PushedByCaption => Info.PushedBy is { Length: > 0 } robot ? robot : "deleted robot";
   }

   /// <summary>
   /// ViewModel untuk <see cref="ContainerManager"/>. Data dan seleksi di Containers.cs,
   /// mutasi di Containers.Edit.cs; berkas ini memegang status dan penanganan jawaban server.
   /// </summary>
   public partial class ContainerManagerVm : MvvmModelBase
   {
      /// <summary>
      /// Membuat ViewModel baru dan mendaftarkan seluruh command layar.
      /// </summary>
      public ContainerManagerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterContainerCommands();
      }

      private ICtnServices Service => EmApp!.ServiceProvider.GetRequiredService<ICtnServices>();

      public string StorageCaption {
         get => Get<string>() ?? "Storage: not checked";
         private set => Set(value);
      }

      public string StorageDetail {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      #region Data

      /// <summary>
      /// <c>true</c> kalau server menjawab bahwa container registry-nya tidak dinyalakan. Selama itu
      /// semua perintah kecuali Refresh mati dan layar hanya menampilkan pesannya.
      /// </summary>
      public bool IsRegistryDisabled {
         get => Get<bool>();
         private set => Set(value, _ => NotifyChanged(nameof(IsRegistryAvailable)));
      }

      /// <summary>Kebalikan <see cref="IsRegistryDisabled"/>, untuk binding.</summary>
      public bool IsRegistryAvailable => !IsRegistryDisabled;

      /// <summary>
      /// <c>true</c> sejak jawaban pertama dari server diterima - daftar root maupun kabar bahwa
      /// registry nonaktif. Sebelum itu semua perintah kecuali Refresh mati.
      /// </summary>
      public bool IsLoaded {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>
      /// <c>host[:port]</c> registry untuk perintah <c>docker</c>, diambil dari koneksi aktif - server
      /// tidak mengirimkannya. Kosong kalau belum ada koneksi aktif.
      /// </summary>
      public string RegistryHost => CtnInput.RegistryHost(EmApp?.ActiveConnection?.Host);

      /// <summary>
      /// <c>true</c> kalau alamat server memakai HTTP polos bukan ke <c>localhost</c>: Docker akan menolak
      /// <c>docker login</c> ke alamat seperti itu, jadi layar menampilkan catatan.
      /// </summary>
      public bool IsInsecureHost => CtnInput.IsInsecureRemote(EmApp?.ActiveConnection?.Host);

      #endregion

      #region Commands

      /// <summary>Membaca ulang seluruh layar dari server.</summary>
      public Task RefreshCommand() => ReloadAsync();

      /// <summary>Selalu boleh selama layar tidak sibuk, termasuk saat registry nonaktif.</summary>
      public bool RefreshCommandAllowed() => IsNotBusy;

      #endregion

      #region Methods

      /// <summary>
      /// Membaca ulang root dan tree root yang dipilih. Dipanggil host setiap kali layar dibuka
      /// lewat navigasi dan dari tombol Refresh.
      /// </summary>
      public Task ReloadAsync() => RunBusyAsync("Loading...", ReadAllAsync);

      private bool CanAct => IsNotBusy && IsLoaded && IsRegistryAvailable;

      private async Task ReadAllAsync() {
         NotifyChanged(nameof(RegistryHost));
         NotifyChanged(nameof(IsInsecureHost));
         NotifyChanged(nameof(DetailPullName));
         if (!await ReadRootsAsync()) return;
      }

      public bool IsStorageRefreshing { get => Get<bool>(); private set { Set(value); RaiseCommandsChanged(); } }
      public bool RefreshStorageCommandAllowed() => !IsStorageRefreshing;
      public async Task RefreshStorageCommand() {
         try { await ReadStorageAsync(); } catch (Exception ex) { AlertError(ex); }
      }
      private async Task ReadStorageAsync() {
         if (IsStorageRefreshing) return;
         IsStorageRefreshing = true;
         StorageCaption = "Storage: checking...";
         StorageDetail = "";
         try {
            var status = await Service.GetMeta_CtnStatus();
            if (!status.ActiveEnabled) { StorageCaption = "Storage: registry disabled"; return; }
            var storage = await Service.GetMeta_CtnStorageSize();
            StorageCaption = $"Total image storage: {CdnManagerVm.FormatSize(storage.TotalBytes)}";
            StorageDetail = $"Blobs {CdnManagerVm.FormatSize(storage.BlobBytes)} · manifests {CdnManagerVm.FormatSize(storage.ManifestBytes)}. Shared blobs counted once; includes retained blobs. Excludes temporary uploads and database/filesystem overhead. Based on registry metadata.";
         }
         catch {
            StorageCaption = "Storage: unavailable";
            throw;
         }
         finally { IsStorageRefreshing = false; }
      }

      private void ShowDisabled() {
         StorageCaption = "Storage: registry disabled";
         StorageDetail = "";
         ClearContainers();
         IsLoaded = true;
         IsRegistryDisabled = true;
      }

      private void ShowEnabled() {
         IsRegistryDisabled = false;
         IsLoaded = true;
      }

      /// <summary>Waktu UTC dari server dalam waktu lokal, untuk tampilan.</summary>
      internal static string LocalTime(DateTime utc) =>
         utc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);

      // Pesan dari server dibuang awalan "Server Error: " supaya yang tampil hanya kalimatnya.
      internal static string ServerMessage(ActionException x) => x.Message.Replace("Server Error: ", "");

      // Satu perubahan di server. 400 (nama tidak sah), 404 (barangnya sudah dihapus orang lain), dan
      // 409 (bentrok) adalah jawaban sehari-hari: ditampilkan sebagai pemberitahuan, bukan galat, lalu
      // bagian layar yang terkena dibaca ulang lewat reread - berhasil maupun tidak, karena yang
      // tampil mungkin sudah usang. Jawaban lain jatuh ke AlertError milik RunBusyAsync.
      private async Task RunMutationAsync(string waiterText, string title, Func<Task> action, Func<Task> reread) {
         var owner = DialogOwner;
         await RunBusyAsync(waiterText, async () => {
            try {
               await action();
            }
            catch (ActionException x) when (x.StatusCode is 400 or 404 or 409) {
               owner?.ShowMboxWarning(ServerMessage(x), title);
            }
            finally {
               await reread();
            }
         });
      }

      // showOverlay: false menjaga IsBusy - semua command tetap mati - tanpa lapisan tunggu, untuk
      // pekerjaan yang menampilkan kemajuannya sendiri.
      private async Task RunBusyAsync(string waiterText, Func<Task> work, bool showOverlay = true) {
         if (EmApp == null || IsBusy) return;

         try {
            WaiterText = waiterText;
            IsBusy = true;
            InWaiting = showOverlay;
            RaiseCommandsChanged();
            await work();
         }
         catch (Exception x) {
            AlertError(x);
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseCommandsChanged();
         }
      }

      // Semua command di sini bergantung pada fakta yang sama - sibuk, registry mati, baris terpilih -
      // jadi dievaluasi ulang sebagai satu himpunan.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
