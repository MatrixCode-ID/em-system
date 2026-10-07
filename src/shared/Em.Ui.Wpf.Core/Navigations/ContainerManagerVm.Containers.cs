using System.Collections.ObjectModel;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Clipboard = System.Windows.Clipboard;

namespace Em.Ui.Wpf.Navigations
{
   // The Containers tab part: roots, the folder and container tree, the details, and the changes to them.
   public partial class ContainerManagerVm
   {
      private void RegisterContainerCommands() {
         RegisterCommand(nameof(CopyPullNameCommand), CopyPullNameCommand, CopyPullNameCommandAllowed);
         RegisterCommand<string?>(nameof(CopyTagPullCommand), CopyTagPullCommand, CopyTagPullCommandAllowed);
         RegisterCommand<string?>(nameof(CopyTagPushCommand), CopyTagPushCommand, CopyTagPullCommandAllowed);
         RegisterCommand<CtnManifestItem?>(nameof(CopyDigestCommand), CopyDigestCommand, CopyDigestCommandAllowed);
         RegisterCommand<CtnManifestItem?>(nameof(CopyDigestPullCommand), CopyDigestPullCommand, CopyDigestCommandAllowed);
         RegisterContainerEditCommands();
         RegisterDeployCommands();
      }

      // Installed while the tree is rebuilt or the root is replaced, so IsSelected changes that come from the
      // rebuilding itself are not taken as the user's choice.
      private bool _rebuilding;
      private int _manifestVersion;

      #region Data

      /// <summary>All roots, in the order of the server's answer.</summary>
      public ObservableCollection<CtnRootItem> Roots { get; } = [];

      /// <summary>Content of the selected root: the folders and containers at its top level.</summary>
      public ObservableCollection<CtnTreeNode> TreeNodes { get; } = [];

      /// <summary>Manifests of the selected container, newest first.</summary>
      public ObservableCollection<CtnManifestItem> Manifests { get; } = [];

      /// <summary>The root selected in the left list.</summary>
      public CtnRootItem? SelectedRoot {
         get => Get<CtnRootItem?>();
         set => Set(value, _ => OnSelectedRootChanged());
      }

      /// <summary>The folder or container selected in the tree; <c>null</c> when the root is what is selected.</summary>
      public CtnTreeNode? SelectedNode {
         get => Get<CtnTreeNode?>();
         private set => Set(value, _ => OnSelectedNodeChanged());
      }

      /// <summary><c>true</c> since the tree of the selected root has been read.</summary>
      public bool IsTreeLoaded {
         get => Get<bool>();
         private set => Set(value, _ => NotifyDetailChanged());
      }

      /// <summary><c>true</c> while the manifests of the selected container are being read.</summary>
      public bool IsManifestsLoading {
         get => Get<bool>();
         private set => Set(value, _ => NotifyHealthChanged());
      }

      /// <summary>Number of manifests of the selected container that have a blob missing from the server storage.</summary>
      public int MissingBlobManifestCount => Manifests.Count(m => m.HasMissingBlobs);

      /// <summary>
      /// <c>true</c> when the selected container is incomplete in storage. This is the real state of the
      /// image, separate from the Active/Disabled status set by an admin, so it is shown as its own chip and
      /// banner.
      /// </summary>
      public bool HasMissingBlobs => MissingBlobManifestCount > 0;

      /// <summary>The "Active" chip only appears when the container is active and proven complete.</summary>
      public bool ShowActiveChip => SelectedNode is { IsActive: true } && !IsManifestsLoading && !HasMissingBlobs;

      /// <summary>Banner text for a container whose blobs are missing; empty when complete.</summary>
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

      /// <summary>The server has no root at all.</summary>
      public bool HasNoRoots => IsLoaded && IsRegistryAvailable && Roots.Count == 0;

      /// <summary>The selected root has no folder or container.</summary>
      public bool IsRootEmpty => SelectedRoot is not null && IsTreeLoaded && TreeNodes.Count == 0;

      /// <summary>Root details: a root is selected and no tree node is selected.</summary>
      public bool ShowRootDetail => SelectedRoot is not null && SelectedNode is null;

      /// <summary>Detail folder.</summary>
      public bool ShowFolderDetail => SelectedNode is { IsFolder: true };

      /// <summary>Detail container.</summary>
      public bool ShowImageDetail => SelectedNode is { IsFolder: false };

      /// <summary>
      /// The pull name copied from the details: <c>host/root/name</c> for a container, <c>host/root</c> for a
      /// root. Empty for a folder, which does not appear in the pull name.
      /// </summary>
      public string DetailPullName =>
         SelectedNode is { Image: { } image } ? CtnInput.PullName(RegistryHost, image.FullName)
         : SelectedNode is null && SelectedRoot is { } root ? CtnInput.PullName(RegistryHost, root.Name)
         : "";

      /// <summary>Content of the selected folder, counted all the way down, e.g. <c>"2 folders · 5 containers"</c>.</summary>
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
      /// The last push time of the selected container, taken from its newest manifest - the server does not
      /// send it in the container data. A dash when it has never been pushed.
      /// </summary>
      public string LastPushedCaption => Manifests.Count > 0 ? Manifests[0].PushedCaption : "—";

      #endregion

      #region Commands

      /// <summary>Copies the pull name (<c>host/root/name</c>) of the selected container or root.</summary>
      public void CopyPullNameCommand() => CopyToClipboard(DetailPullName);

      /// <summary>Only when there is a pull name to copy.</summary>
      public bool CopyPullNameCommandAllowed() => DetailPullName.Length > 0;

      /// <summary>Copies the <c>docker pull</c> for tag <paramref name="tag"/> of the selected container.</summary>
      public void CopyTagPullCommand(string? tag) {
         if (SelectedNode?.Image is { } image && !string.IsNullOrEmpty(tag))
            CopyToClipboard(CtnInput.DockerPullTag(RegistryHost, image.FullName, tag));
      }

      /// <summary>Only for a selected container and a tag.</summary>
      public bool CopyTagPullCommandAllowed(string? tag) => SelectedNode?.Image is not null && !string.IsNullOrEmpty(tag);

      /// <summary>Copies <c>docker tag</c> and <c>docker push</c> for tag <paramref name="tag"/> of the selected container.</summary>
      public void CopyTagPushCommand(string? tag) {
         if (SelectedNode?.Image is { } image && !string.IsNullOrEmpty(tag))
            CopyToClipboard(CtnInput.DockerTagPush(RegistryHost, image.FullName, tag));
      }

      /// <summary>Menyalin digest lengkap manifest <paramref name="manifest"/>.</summary>
      public void CopyDigestCommand(CtnManifestItem? manifest) {
         if (manifest is not null) CopyToClipboard(manifest.Digest);
      }

      /// <summary>Only for a manifest.</summary>
      public bool CopyDigestCommandAllowed(CtnManifestItem? manifest) => manifest is not null && SelectedNode?.Image is not null;

      /// <summary>Copies <c>docker pull</c> with the digest of manifest <paramref name="manifest"/>.</summary>
      public void CopyDigestPullCommand(CtnManifestItem? manifest) {
         if (manifest is not null && SelectedNode?.Image is { } image)
            CopyToClipboard(CtnInput.DockerPullDigest(RegistryHost, image.FullName, manifest.Digest));
      }

      #endregion

      #region Reading

      // false = the registry is not turned on on the server (404): the screen has already been redirected to
      // the disabled state. selectRootId selects a specific root (e.g. one just created); selectNodeId selects
      // a specific node in its tree. Without either, the current selection is kept.
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

      // Reads the tree of the selected root and arranges it. Open folders and the selected node are
      // remembered by id, so reading again after a change does not throw the user to the top of the tree.
      // selectId selects a specific node, e.g. one just created or moved.
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
            // The root was deleted by someone else: the root list is read again and another one is chosen.
            await ReadRootsAsync();
            return;
         }

         // The selection changed while the answer was on its way; what arrives describes the root that was left.
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
      /// Arranges the flat tree from the server into nested nodes: folders first, then containers, each sorted
      /// by name. A folder whose parent is not in the list - which does not happen when the server is
      /// consistent - is placed at the top level instead of disappearing from the screen.
      /// </summary>
      internal List<CtnTreeNode> BuildTree(CtnTree tree) {
         var folders = tree.Folders.ToDictionary(r => r.Id);
         var nodes = new Dictionary<string, CtnTreeNode>();

         // A parent is created before its child; the chain of parents is limited so circular data does not cause
         // an endless loop.
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

         // Folders above containers at the top level; the name order within each group is already kept by the
         // order of addition above.
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
         ClearDeploy();
         NotifyChanged(nameof(LastPushedCaption));
         NotifyDetailChanged();
         RaiseCommandsChanged();
         if (_rebuilding) return;

         if (SelectedNode is { IsFolder: false } image) {
            _ = LoadManifestsAsync(image);
            _ = LoadDeployAsync(image);
         }
      }

      // Read outside RunBusyAsync so moving from container to container stays smooth: every read carries a
      // number, and an answer that arrives after the selection changed is discarded.
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
            // The container was deleted by someone else: the tree is read again so it disappears from the screen.
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
