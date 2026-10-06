using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Clipboard = System.Windows.Clipboard;

namespace Em.Ui.Wpf.Navigations
{
   // Bagian tab Containers: root, tree folder dan container, detail, dan perubahan atasnya.
   public partial class ContainerManagerVm
   {
      private void RegisterContainerCommands() {
         RegisterCommand(nameof(CopyPullNameCommand), CopyPullNameCommand, CopyPullNameCommandAllowed);
         RegisterCommand<string?>(nameof(CopyTagPullCommand), CopyTagPullCommand, CopyTagPullCommandAllowed);
         RegisterCommand<string?>(nameof(CopyTagPushCommand), CopyTagPushCommand, CopyTagPullCommandAllowed);
         RegisterCommand<CtnManifestItem?>(nameof(CopyDigestCommand), CopyDigestCommand, CopyDigestCommandAllowed);
         RegisterCommand<CtnManifestItem?>(nameof(CopyDigestPullCommand), CopyDigestPullCommand, CopyDigestCommandAllowed);
         RegisterContainerEditCommands();
      }

      // Dipasang selagi tree disusun ulang atau root diganti, supaya perubahan IsSelected yang datang
      // dari penyusunan itu sendiri tidak dianggap pilihan pengguna.
      private bool _rebuilding;
      private int _manifestVersion;

      #region Data

      /// <summary>Seluruh root, urut sesuai jawaban server.</summary>
      public ObservableCollection<CtnRootItem> Roots { get; } = [];

      /// <summary>Isi root yang dipilih: folder dan container di tingkat teratasnya.</summary>
      public ObservableCollection<CtnTreeNode> TreeNodes { get; } = [];

      /// <summary>Manifest container yang dipilih, yang terbaru lebih dulu.</summary>
      public ObservableCollection<CtnManifestItem> Manifests { get; } = [];

      /// <summary>Root yang dipilih di daftar kiri.</summary>
      public CtnRootItem? SelectedRoot {
         get => Get<CtnRootItem?>();
         set => Set(value, _ => OnSelectedRootChanged());
      }

      /// <summary>Folder atau container yang dipilih di tree; <c>null</c> kalau yang dipilih rootnya.</summary>
      public CtnTreeNode? SelectedNode {
         get => Get<CtnTreeNode?>();
         private set => Set(value, _ => OnSelectedNodeChanged());
      }

      /// <summary><c>true</c> sejak tree root yang dipilih terbaca.</summary>
      public bool IsTreeLoaded {
         get => Get<bool>();
         private set => Set(value, _ => NotifyDetailChanged());
      }

      /// <summary><c>true</c> selama manifest container yang dipilih sedang dibaca.</summary>
      public bool IsManifestsLoading {
         get => Get<bool>();
         private set => Set(value, _ => NotifyHealthChanged());
      }

      /// <summary>Jumlah manifest container terpilih yang punya blob hilang dari storage server.</summary>
      public int MissingBlobManifestCount => Manifests.Count(m => m.HasMissingBlobs);

      /// <summary>
      /// <c>true</c> kalau container terpilih tidak lengkap di storage. Ini keadaan nyata image, terpisah dari
      /// status Active/Disabled yang diatur admin, jadi ditampilkan sebagai chip dan banner tersendiri.
      /// </summary>
      public bool HasMissingBlobs => MissingBlobManifestCount > 0;

      /// <summary>Chip "Active" hanya tampil bila container aktif dan terbukti lengkap.</summary>
      public bool ShowActiveChip => SelectedNode is { IsActive: true } && !IsManifestsLoading && !HasMissingBlobs;

      /// <summary>Teks banner untuk container yang blob-nya hilang; kosong kalau lengkap.</summary>
      public string MissingBlobBanner => MissingBlobManifestCount switch {
         0 => "",
         var missing => $"{missing} of {Manifests.Count} manifest(s) reference blobs that are not in this server's storage. " +
                        "Pulling this container will fail, including by tag: an index only points at the broken manifests. " +
                        "The database is probably shared with another server whose storage holds the files."
      };

      private void NotifyHealthChanged() {
         NotifyChanged(nameof(MissingBlobManifestCount));
         NotifyChanged(nameof(HasMissingBlobs));
         NotifyChanged(nameof(ShowActiveChip));
         NotifyChanged(nameof(MissingBlobBanner));
      }

      /// <summary>Server belum punya satu root pun.</summary>
      public bool HasNoRoots => IsLoaded && IsRegistryAvailable && Roots.Count == 0;

      /// <summary>Root yang dipilih tidak punya folder maupun container.</summary>
      public bool IsRootEmpty => SelectedRoot is not null && IsTreeLoaded && TreeNodes.Count == 0;

      /// <summary>Detail root: ada root terpilih dan tidak ada simpul tree yang dipilih.</summary>
      public bool ShowRootDetail => SelectedRoot is not null && SelectedNode is null;

      /// <summary>Detail folder.</summary>
      public bool ShowFolderDetail => SelectedNode is { IsFolder: true };

      /// <summary>Detail container.</summary>
      public bool ShowImageDetail => SelectedNode is { IsFolder: false };

      /// <summary>
      /// Nama pull yang disalin dari detail: <c>host/root/nama</c> untuk container, <c>host/root</c> untuk
      /// root. Kosong untuk folder, yang tidak muncul di nama pull.
      /// </summary>
      public string DetailPullName =>
         SelectedNode is { Image: { } image } ? CtnInput.PullName(RegistryHost, image.FullName)
         : SelectedNode is null && SelectedRoot is { } root ? CtnInput.PullName(RegistryHost, root.Name)
         : "";

      /// <summary>Isi folder yang dipilih, dihitung sampai ke dalam, mis. <c>"2 folders · 5 containers"</c>.</summary>
      public string FolderSummary {
         get {
            if (SelectedNode is not { IsFolder: true } node) return "";

            var all = node.Descendants().ToList();
            var folders = all.Count(r => r.IsFolder);
            var images = all.Count - folders;
            return $"{folders:N0} folder{(folders == 1 ? "" : "s")} · {images:N0} container{(images == 1 ? "" : "s")}";
         }
      }

      /// <summary>
      /// Waktu push terakhir container yang dipilih, diambil dari manifest terbarunya - server tidak
      /// mengirimkannya di data container. Tanda pisah kalau belum pernah di-push.
      /// </summary>
      public string LastPushedCaption => Manifests.Count > 0 ? Manifests[0].PushedCaption : "—";

      #endregion

      #region Commands

      /// <summary>Menyalin nama pull (<c>host/root/nama</c>) container atau root yang dipilih.</summary>
      public void CopyPullNameCommand() => CopyToClipboard(DetailPullName);

      /// <summary>Hanya kalau ada nama pull untuk disalin.</summary>
      public bool CopyPullNameCommandAllowed() => DetailPullName.Length > 0;

      /// <summary>Menyalin <c>docker pull</c> untuk tag <paramref name="tag"/> container yang dipilih.</summary>
      public void CopyTagPullCommand(string? tag) {
         if (SelectedNode?.Image is { } image && !string.IsNullOrEmpty(tag))
            CopyToClipboard(CtnInput.DockerPullTag(RegistryHost, image.FullName, tag));
      }

      /// <summary>Hanya untuk container yang dipilih dan sebuah tag.</summary>
      public bool CopyTagPullCommandAllowed(string? tag) => SelectedNode?.Image is not null && !string.IsNullOrEmpty(tag);

      /// <summary>Menyalin <c>docker tag</c> dan <c>docker push</c> untuk tag <paramref name="tag"/> container yang dipilih.</summary>
      public void CopyTagPushCommand(string? tag) {
         if (SelectedNode?.Image is { } image && !string.IsNullOrEmpty(tag))
            CopyToClipboard(CtnInput.DockerTagPush(RegistryHost, image.FullName, tag));
      }

      /// <summary>Menyalin digest lengkap manifest <paramref name="manifest"/>.</summary>
      public void CopyDigestCommand(CtnManifestItem? manifest) {
         if (manifest is not null) CopyToClipboard(manifest.Digest);
      }

      /// <summary>Hanya untuk sebuah manifest.</summary>
      public bool CopyDigestCommandAllowed(CtnManifestItem? manifest) => manifest is not null && SelectedNode?.Image is not null;

      /// <summary>Menyalin <c>docker pull</c> dengan digest manifest <paramref name="manifest"/>.</summary>
      public void CopyDigestPullCommand(CtnManifestItem? manifest) {
         if (manifest is not null && SelectedNode?.Image is { } image)
            CopyToClipboard(CtnInput.DockerPullDigest(RegistryHost, image.FullName, manifest.Digest));
      }

      #endregion

      #region Reading

      // false = registry tidak dinyalakan di server (404): layar sudah dialihkan ke keadaan nonaktif.
      // selectRootId memilih root tertentu (mis. yang baru dibuat); selectNodeId memilih simpul tertentu
      // di tree-nya. Tanpa keduanya, pilihan yang sedang ada dipertahankan.
      private async Task<bool> ReadRootsAsync(string? selectRootId = null, string? selectNodeId = null) {
         var status = await Service.GetMeta_CtnStatus();
         if (!status.ActiveEnabled) { ShowDisabled(); return false; }
         var roots = await Service.GetMeta_CtnRoots();

         ShowEnabled();
         var keep = selectRootId ?? SelectedRoot?.Id;
         _rebuilding = true;
         try {
            Roots.Clear();
            foreach (var root in roots) Roots.Add(new CtnRootItem(root));
            SelectedRoot = Roots.FirstOrDefault(r => r.Id == keep) ?? Roots.FirstOrDefault();
         }
         finally {
            _rebuilding = false;
         }

         NotifyChanged(nameof(HasNoRoots));
         await ReadTreeAsync(selectNodeId, retryRoots: false);
         if (IsRegistryAvailable) await ReadStorageAsync();
         return true;
      }

      // Membaca tree root yang dipilih dan menyusunnya. Folder yang terbuka dan simpul yang dipilih
      // diingat berdasarkan id, jadi membaca ulang sesudah sebuah perubahan tidak melempar pengguna
      // ke atas tree. selectId memilih simpul tertentu, mis. yang baru dibuat atau dipindah.
      private async Task ReadTreeAsync(string? selectId = null, bool retryRoots = true) {
         var root = SelectedRoot;
         if (root is null) {
            ClearTree();
            return;
         }

         CtnTree tree;
         try {
            tree = await Service.GetMeta_CtnTree(root.Id);
         }
         catch (ActionException x) when (x.StatusCode == 404 && retryRoots) {
            // Root dihapus orang lain: daftar root dibaca ulang dan memilih yang lain.
            await ReadRootsAsync();
            return;
         }

         // Pilihan berganti selagi jawabannya di jalan; yang datang menggambarkan root yang ditinggalkan.
         if (root != SelectedRoot) return;

         var expanded = ExpandedIds();
         var keep = selectId ?? SelectedNode?.Id;
         CtnTreeNode? selected;
         _rebuilding = true;
         try {
            SelectedNode = null;
            TreeNodes.Clear();
            var all = new Dictionary<string, CtnTreeNode>();
            foreach (var node in BuildTree(tree)) TreeNodes.Add(node);
            foreach (var node in TreeNodes.SelectMany(r => r.Descendants().Prepend(r))) all[node.Id] = node;

            foreach (var node in all.Values) {
               if (node.IsFolder && expanded.Contains(node.Id)) node.IsExpanded = true;
               node.RefreshCaption();
            }

            selected = keep is not null && all.TryGetValue(keep, out var found) ? found : null;
            if (selected is not null) {
               for (var parent = selected.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
               selected.IsSelected = true;
            }
         }
         finally {
            _rebuilding = false;
         }

         IsTreeLoaded = true;
         SelectedNode = selected;
         NotifyDetailChanged();
      }

      private HashSet<string> ExpandedIds() =>
         [.. TreeNodes.SelectMany(r => r.Descendants().Prepend(r)).Where(r => r.IsFolder && r.IsExpanded).Select(r => r.Id)];

      /// <summary>
      /// Menyusun tree datar dari server menjadi simpul bersarang: folder lebih dulu, lalu container,
      /// masing-masing urut nama. Folder yang induknya tidak ada di daftar - tidak terjadi kalau server
      /// konsisten - ditaruh di tingkat teratas daripada hilang dari layar.
      /// </summary>
      internal List<CtnTreeNode> BuildTree(CtnTree tree) {
         var folders = tree.Folders.ToDictionary(r => r.Id);
         var nodes = new Dictionary<string, CtnTreeNode>();

         // Induk dibuat lebih dulu daripada anaknya; rantai induk dibatasi supaya data yang berputar
         // tidak membuat perulangan tak berujung.
         CtnTreeNode NodeOf(CtnFolderInfo folder, int guard) {
            if (nodes.TryGetValue(folder.Id, out var existing)) return existing;

            CtnTreeNode? parent = null;
            if (folder.ParentId is { } parentId && guard < CtnInput.MaxFolderDepth + 4 &&
                folders.TryGetValue(parentId, out var parentFolder)) {
               parent = NodeOf(parentFolder, guard + 1);
            }

            var node = new CtnTreeNode(folder, parent, this);
            nodes[folder.Id] = node;
            return node;
         }

         foreach (var folder in tree.Folders) NodeOf(folder, 0);

         var top = new List<CtnTreeNode>();
         foreach (var node in nodes.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
            if (node.Parent is null) top.Add(node);
            else node.Parent.Children.Add(node);
         }

         foreach (var image in tree.Images.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)) {
            var parent = image.FolderId is { } folderId && nodes.TryGetValue(folderId, out var folderNode) ? folderNode : null;
            var node = new CtnTreeNode(image, parent, this);
            if (parent is null) top.Add(node);
            else parent.Children.Add(node);
         }

         // Folder di atas container di tingkat teratas; urutan nama di dalam masing-masing kelompok
         // sudah terjaga oleh urutan penambahan di atas.
         return [.. top.Where(r => r.IsFolder), .. top.Where(r => !r.IsFolder)];
      }

      private void ClearTree() {
         _rebuilding = true;
         try {
            SelectedNode = null;
            TreeNodes.Clear();
         }
         finally {
            _rebuilding = false;
         }

         IsTreeLoaded = false;
         Manifests.Clear();
         NotifyDetailChanged();
      }

      private void ClearContainers() {
         _rebuilding = true;
         try {
            Roots.Clear();
            SelectedRoot = null;
         }
         finally {
            _rebuilding = false;
         }

         ClearTree();
         NotifyChanged(nameof(HasNoRoots));
      }

      #endregion

      #region Selection

      private void OnSelectedRootChanged() {
         NotifyDetailChanged();
         RaiseCommandsChanged();
         if (_rebuilding) return;

         ClearTree();
         if (SelectedRoot is not null) _ = RunBusyAsync("Loading...", () => ReadTreeAsync());
      }

      internal void OnNodeSelected(CtnTreeNode node) {
         if (_rebuilding) return;

         SelectedNode = node;
      }

      internal void OnNodeDeselected(CtnTreeNode node) {
         if (_rebuilding) return;

         if (SelectedNode == node) SelectedNode = null;
      }

      private void OnSelectedNodeChanged() {
         Manifests.Clear();
         NotifyChanged(nameof(LastPushedCaption));
         NotifyDetailChanged();
         RaiseCommandsChanged();
         if (_rebuilding) return;

         if (SelectedNode is { IsFolder: false } image) _ = LoadManifestsAsync(image);
      }

      // Dibaca di luar RunBusyAsync supaya berpindah dari container ke container tetap lancar: tiap
      // pembacaan membawa nomor, dan jawaban yang datang sesudah pilihan berganti dibuang.
      private async Task LoadManifestsAsync(CtnTreeNode node) {
         var version = ++_manifestVersion;
         IsManifestsLoading = true;
         try {
            var manifests = await Service.GetMeta_CtnImageManifests(node.Id);
            if (version != _manifestVersion) return;

            Manifests.Clear();
            foreach (var manifest in manifests) Manifests.Add(new CtnManifestItem(manifest));
            NotifyChanged(nameof(LastPushedCaption));
            node.HasMissingBlobs = manifests.Any(m => m.MissingBlobCount > 0);
            NotifyHealthChanged();
         }
         catch (ActionException x) when (x.StatusCode == 404) {
            // Container dihapus orang lain: tree dibaca ulang supaya hilang dari layar.
            if (version != _manifestVersion) return;

            _ = RunBusyAsync("Loading...", () => ReadRootsAsync());
         }
         catch (Exception x) {
            if (version == _manifestVersion) AlertError(x);
         }
         finally {
            if (version == _manifestVersion) IsManifestsLoading = false;
         }
      }

      private void NotifyDetailChanged() {
         NotifyChanged(nameof(IsRootEmpty));
         NotifyChanged(nameof(ShowRootDetail));
         NotifyChanged(nameof(ShowFolderDetail));
         NotifyChanged(nameof(ShowImageDetail));
         NotifyChanged(nameof(DetailPullName));
         NotifyChanged(nameof(FolderSummary));
         NotifyChanged(nameof(EditNodeCaption));
         NotifyHealthChanged();
      }

      #endregion

      private void CopyToClipboard(string text) {
         try {
            Clipboard.SetText(text);
         }
         catch (Exception x) {
            AlertError(x);
         }
      }
   }
}
