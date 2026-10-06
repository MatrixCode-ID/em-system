using System.Windows;
using FontAwesome6;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   // Bagian tab Containers yang mengubah data: root, folder, container, pindah, dan hapus. Semua
   // perubahan lewat RunMutationAsync: jawaban 400/404/409 server ditampilkan apa adanya, lalu bagian
   // layar yang terkena dibaca ulang.
   public partial class ContainerManagerVm
   {
      private void RegisterContainerEditCommands() {
         RegisterCommand(nameof(NewRootCommand), NewRootCommand, NewRootCommandAllowed);
         RegisterCommand(nameof(EditRootCommand), EditRootCommand, EditRootCommandAllowed);
         RegisterCommand(nameof(DeleteRootCommand), DeleteRootCommand, DeleteRootCommandAllowed);
         RegisterCommand(nameof(NewFolderCommand), NewFolderCommand, NewFolderCommandAllowed);
         RegisterCommand(nameof(NewImageCommand), NewImageCommand, NewImageCommandAllowed);
         RegisterCommand(nameof(EditNodeCommand), EditNodeCommand, EditNodeCommandAllowed);
         RegisterCommand(nameof(MoveNodeCommand), MoveNodeCommand, MoveNodeCommandAllowed);
         RegisterCommand(nameof(DeleteNodeCommand), DeleteNodeCommand, DeleteNodeCommandAllowed);
         RegisterCommand<object?>(nameof(DropCommand), DropCommand, DropCommandAllowed);
         RegisterCommand<string?>(nameof(DeleteTagCommand), DeleteTagCommand, DeleteTagCommandAllowed);
         RegisterCommand<CtnManifestItem?>(nameof(DeleteManifestCommand), DeleteManifestCommand, DeleteManifestCommandAllowed);
         RegisterCommand(nameof(GarbageCollectionCommand), GarbageCollectionCommand, GarbageCollectionCommandAllowed);
      }

      private const string MetadataOnlyNote =
         "Only the metadata is removed. The layer files stay on disk until you run Garbage collection " +
         "from the toolbar.";

      #region Data

      /// <summary>Tulisan tombol edit di detail: <c>Rename</c> untuk folder, <c>Edit</c> untuk container.</summary>
      public string EditNodeCaption => SelectedNode is { IsFolder: true } ? "Rename" : "Edit";

      // Folder tempat folder atau container baru dibuat: folder yang dipilih, folder tempat container
      // yang dipilih berada, atau - tanpa pilihan - root itu sendiri (null).
      private CtnTreeNode? CreationParent => SelectedNode is { IsFolder: true } folder ? folder : SelectedNode?.Parent;

      #endregion

      #region Root

      /// <summary>Meminta nama dan deskripsi lalu membuat root baru, dan memilihnya.</summary>
      public async Task NewRootCommand() {
         var dialog = new CtnRootDialog { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var name = dialog.Vm.NameResult;
         var description = dialog.Vm.DescriptionResult;
         string? createdId = null;
         await RunMutationAsync("Creating root...", "New Root",
            async () => createdId = (await Service.PostGetMeta_CtnRootCreate(name, description)).Id,
            async () => {
               await ReadRootsAsync(selectRootId: createdId);
            });
      }

      /// <summary>Hanya saat registry aktif dan layar tidak sibuk.</summary>
      public bool NewRootCommandAllowed() => CanAct;

      /// <summary>Mengubah deskripsi dan status aktif root yang dipilih.</summary>
      public async Task EditRootCommand() {
         if (SelectedRoot is not { } root) return;

         var dialog = new CtnRootDialog(root.Info) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var description = dialog.Vm.DescriptionResult;
         var isActive = dialog.Vm.IsActive;
         await RunMutationAsync("Saving root...", "Edit Root",
            () => Service.PostMeta_CtnRootUpdate(root.Id, description, isActive),
            () => ReadRootsAsync());
      }

      /// <summary>Hanya untuk root yang dipilih.</summary>
      public bool EditRootCommandAllowed() => CanAct && SelectedRoot is not null;

      /// <summary>
      /// Menghapus root yang dipilih setelah dikonfirmasi. Server menolak (409) root yang masih punya
      /// folder, container, atau hak robot; jawabannya ditampilkan apa adanya.
      /// </summary>
      public async Task DeleteRootCommand() {
         if (SelectedRoot is not { } root || DialogOwner is not { } owner) return;

         var answer = owner.ShowMboxDecideWarning(
            $"Delete root '{root.Name}'?\n\n" +
            "A root can only be deleted once it has no folders, containers or robot access left. " +
            $"{MetadataOnlyNote}\n\nThis cannot be undone.", "Delete Root");
         if (answer != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting root...", "Delete Root",
            () => Service.PostMeta_CtnRootDelete(root.Id),
            async () => {
               await ReadRootsAsync();
            });
      }

      /// <summary>Hanya untuk root yang dipilih.</summary>
      public bool DeleteRootCommandAllowed() => CanAct && SelectedRoot is not null;

      #endregion

      #region Folder and container

      /// <summary>
      /// Meminta nama lalu membuat folder baru di folder yang dipilih (atau di folder tempat container yang
      /// dipilih berada, atau di root kalau tidak ada yang dipilih), dan memilihnya.
      /// </summary>
      public async Task NewFolderCommand() {
         if (SelectedRoot is not { } root) return;

         var parent = CreationParent;
         var where = parent is null ? $"the root '{root.Name}'" : $"'{parent.Path}'";
         var dialog = new TextInputDialog("New Folder", $"Name of the folder to create in {where}.",
            "Folder name", "Create", EFontAwesomeIcon.Solid_FolderPlus) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var name = dialog.Vm.Result;
         var parentId = parent?.Id;
         string? createdId = null;
         await RunMutationAsync("Creating folder...", "New Folder",
            async () => createdId = (await Service.PostGetMeta_CtnFolderCreate(root.Id, parentId, name)).Id,
            () => ReadRootsAsync(selectNodeId: createdId));
      }

      /// <summary>Hanya untuk root yang dipilih, dan selama folder barunya tidak melewati kedalaman maksimum.</summary>
      public bool NewFolderCommandAllowed() =>
         CanAct && SelectedRoot is not null && (CreationParent?.Depth ?? 0) < CtnInput.MaxFolderDepth;

      /// <summary>
      /// Meminta nama dan deskripsi lalu membuat container baru di folder yang dipilih (atau di root), dan
      /// memilihnya. Container harus ada dulu sebelum image bisa di-push ke sana.
      /// </summary>
      public async Task NewImageCommand() {
         if (SelectedRoot is not { } root) return;

         var dialog = new CtnImageDialog(root.Name) { Owner = DialogOwner };
         if (dialog.ShowDialog() != true) return;

         var name = dialog.Vm.NameResult;
         var description = dialog.Vm.DescriptionResult;
         var folderId = CreationParent?.Id;
         string? createdId = null;
         await RunMutationAsync("Creating container...", "New Container",
            async () => createdId = (await Service.PostGetMeta_CtnImageCreate(root.Id, folderId, name, description)).Id,
            () => ReadRootsAsync(selectNodeId: createdId));
      }

      /// <summary>Hanya untuk root yang dipilih.</summary>
      public bool NewImageCommandAllowed() => CanAct && SelectedRoot is not null;

      /// <summary>
      /// Mengganti nama folder yang dipilih, atau mengedit deskripsi dan status aktif container yang
      /// dipilih. Nama root dan container tidak bisa diganti: kontraknya tidak punya rename untuk keduanya.
      /// </summary>
      public async Task EditNodeCommand() {
         if (SelectedNode is not { } node || SelectedRoot is not { } root) return;

         if (node.IsFolder) {
            var dialog = new TextInputDialog("Rename Folder", $"New name for '{node.Path}'.",
               "Folder name", "Rename", EFontAwesomeIcon.Solid_PenToSquare) { Owner = DialogOwner };
            dialog.Vm.Value = node.Name;
            if (dialog.ShowDialog() != true || dialog.Vm.Result == node.Name) return;

            var name = dialog.Vm.Result;
            await RunMutationAsync("Renaming folder...", "Rename Folder",
               () => Service.PostGetMeta_CtnFolderRename(node.Id, name),
               () => ReadTreeAsync(node.Id));
            return;
         }

         var form = new CtnImageDialog(root.Name, node.Image) { Owner = DialogOwner };
         if (form.ShowDialog() != true) return;

         var description = form.Vm.DescriptionResult;
         var isActive = form.Vm.IsActive;
         await RunMutationAsync("Saving container...", "Edit Container",
            () => Service.PostMeta_CtnImageUpdate(node.Id, description, isActive),
            () => ReadRootsAsync(selectNodeId: node.Id));
      }

      /// <summary>Hanya untuk folder atau container yang dipilih.</summary>
      public bool EditNodeCommandAllowed() => CanAct && SelectedNode is not null;

      /// <summary>
      /// Memindahkan folder atau container yang dipilih lewat dialog pemilih folder. Nama pull tidak
      /// berubah karena folder tidak ikut nama pull.
      /// </summary>
      public async Task MoveNodeCommand() {
         if (SelectedNode is not { } node || SelectedRoot is not { } root || DialogOwner is not { } owner) return;

         var choices = BuildMoveChoices(node, root);
         if (choices.Count == 0) {
            owner.ShowMboxInfo($"There is no other place in '{root.Name}' to move '{node.Name}' to.", "Move");
            return;
         }

         var what = node.IsFolder ? $"folder '{node.Path}'" : $"container '{node.Image!.FullName}'";
         var dialog = new CtnFolderPickerDialog("Move", $"Choose where to move {what}. " +
               "Folders are not part of the pull name, so the pull name does not change.", choices) { Owner = owner };
         if (dialog.ShowDialog() != true || dialog.Vm.Selected is not { } target) return;

         await MoveAsync(node, target.FolderId);
      }

      /// <summary>Hanya untuk folder atau container yang dipilih.</summary>
      public bool MoveNodeCommandAllowed() => CanAct && SelectedNode is not null;

      /// <summary>
      /// Menghapus folder yang dipilih (harus kosong) atau container yang dipilih, setelah dikonfirmasi.
      /// Menghapus container membuang manifest, tag, dan tautan blob-nya; berkas di disk menunggu garbage
      /// collection, dan konfirmasinya mengatakan itu.
      /// </summary>
      public async Task DeleteNodeCommand() {
         if (SelectedNode is not { } node || DialogOwner is not { } owner) return;

         if (node.IsFolder) {
            if (owner.ShowMboxDecideWarning($"Delete folder '{node.Path}'?\n\nThis cannot be undone.", "Delete Folder") !=
                MessageBoxResult.Yes) return;

            await RunMutationAsync("Deleting folder...", "Delete Folder",
               () => Service.PostMeta_CtnFolderDelete(node.Id),
               () => ReadRootsAsync());
            return;
         }

         var image = node.Image!;
         var answer = owner.ShowMboxDecideWarning(
            $"Delete container '{image.FullName}' with its {image.ManifestCount:N0} manifest(s) and " +
            $"{image.TagCount:N0} tag(s)?\n\n{MetadataOnlyNote}\n\nThis cannot be undone.", "Delete Container");
         if (answer != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting container...", "Delete Container",
            () => Service.PostMeta_CtnImageDelete(node.Id),
            () => ReadRootsAsync());
      }

      /// <summary>Hanya untuk container yang dipilih, atau folder yang sudah kosong.</summary>
      public bool DeleteNodeCommandAllowed() =>
         CanAct && SelectedNode is { } node && (!node.IsFolder || node.Children.Count == 0);

      #endregion

      #region Tag and manifest

      /// <summary>Menghapus satu tag container yang dipilih; manifest-nya tetap ada.</summary>
      public async Task DeleteTagCommand(string? tag) {
         if (string.IsNullOrEmpty(tag) || SelectedNode?.Image is not { } image || DialogOwner is not { } owner) return;
         var node = SelectedNode;
         if (owner.ShowMboxDecideWarning(
                $"Delete tag '{tag}' from '{image.FullName}'?\n\n" +
                "The manifest stays and can still be pulled by digest.", "Delete Tag") != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting tag...", "Delete Tag",
            () => Service.PostMeta_CtnTagDelete(node.Id, tag),
            () => ReadTreeAsync(node.Id));
      }

      public bool DeleteTagCommandAllowed(string? tag) => CanAct && SelectedNode?.Image is not null && !string.IsNullOrEmpty(tag);

      /// <summary>Menghapus satu manifest beserta tag-nya. Ditolak server (409) bila masih dirujuk index.</summary>
      public async Task DeleteManifestCommand(CtnManifestItem? manifest) {
         if (manifest is null || SelectedNode?.Image is not { } image || DialogOwner is not { } owner) return;
         var node = SelectedNode;
         var tags = manifest.HasTags ? $"Tags removed with it: {string.Join(", ", manifest.Tags)}." : "It has no tags.";
         if (owner.ShowMboxDecideWarning(
                $"Delete manifest {manifest.ShortDigest} from '{image.FullName}'?\n\n{tags}\n\n" +
                $"{MetadataOnlyNote}\n\nThis cannot be undone.", "Delete Manifest") != MessageBoxResult.Yes) return;

         await RunMutationAsync("Deleting manifest...", "Delete Manifest",
            () => Service.PostMeta_CtnManifestDelete(node.Id, manifest.Info.Id),
            () => ReadTreeAsync(node.Id));
      }

      public bool DeleteManifestCommandAllowed(CtnManifestItem? manifest) => CanAct && SelectedNode?.Image is not null && manifest is not null;

      #endregion

      #region Garbage collection

      /// <summary>Membuka dialog review garbage collection; storage dibaca ulang bila GC dijalankan.</summary>
      public async Task GarbageCollectionCommand() {
         var dialog = new CtnGcDialog(Service) { Owner = DialogOwner };
         dialog.ShowDialog();
         if (dialog.Vm.HasRun) await RefreshStorageCommand();
      }

      public bool GarbageCollectionCommandAllowed() => CanAct;

      #endregion

      #region Move

      // Tujuan yang boleh dipilih untuk memindahkan simpul: root-nya sendiri dan setiap folder, kecuali
      // tempat simpul itu sudah berada, simpul itu sendiri berikut isinya (folder tidak boleh masuk ke
      // dalam dirinya), dan folder yang membuat tree melewati kedalaman maksimum.
      private List<CtnFolderChoice> BuildMoveChoices(CtnTreeNode node, CtnRootItem root) {
         var choices = new List<CtnFolderChoice>();
         var height = node.IsFolder ? FolderHeight(node) : 0;
         if (node.Parent is not null)
            choices.Add(new CtnFolderChoice { FolderId = null, Label = $"{root.Name} (root)", Depth = 0 });

         Walk(TreeNodes);
         return choices;

         void Walk(IEnumerable<CtnTreeNode> nodes) {
            foreach (var folder in nodes.Where(r => r.IsFolder)) {
               if (node.IsFolder && (folder == node || node.Contains(folder))) continue;

               if (folder != node.Parent && (!node.IsFolder || folder.Depth + height <= CtnInput.MaxFolderDepth)) {
                  choices.Add(new CtnFolderChoice { FolderId = folder.Id, Label = folder.Name, Depth = folder.Depth });
               }

               Walk(folder.Children);
            }
         }
      }

      // Jumlah tingkat folder dari folder ini ke bawah, folder ini sendiri dihitung.
      private static int FolderHeight(CtnTreeNode folder) =>
         1 + folder.Children.Where(r => r.IsFolder).Select(FolderHeight).DefaultIfEmpty(0).Max();

      private Task MoveAsync(CtnTreeNode node, string? targetFolderId) =>
         RunMutationAsync($"Moving {node.Name}...", "Move",
            async () => {
               if (node.IsFolder) await Service.PostGetMeta_CtnFolderMove(node.Id, targetFolderId);
               else await Service.PostGetMeta_CtnImageMove(node.Id, targetFolderId);
            },
            () => ReadTreeAsync(node.Id));

      #endregion

      #region Drag and drop

      /// <summary>
      /// Menerima jatuhan sebuah simpul tree: ke baris folder (masuk ke folder itu), ke baris root
      /// (keluar ke tingkat teratas root), atau ke ruang kosong tree (sama dengan baris root). Jatuhan
      /// di root lain tidak diterima karena server hanya memindahkan di root yang sama.
      /// </summary>
      public Task DropCommand(object? payload) {
         var drop = ResolveDrop(payload);
         return drop.IsValid ? MoveAsync(drop.Node!, drop.TargetFolderId) : Task.CompletedTask;
      }

      /// <summary>Boleh kalau jatuhan itu pindahan yang sah; kursor "tidak boleh" muncul kalau tidak.</summary>
      public bool DropCommandAllowed(object? payload) => CanAct && ResolveDrop(payload).IsValid;

      private (CtnTreeNode? Node, string? TargetFolderId, bool IsValid) ResolveDrop(object? payload) {
         CtnTreeNode? dragged;
         object? target;
         if (payload is DropRequest request) {
            dragged = request.Payload as CtnTreeNode;
            target = request.Target;
         }
         else {
            dragged = payload as CtnTreeNode;
            target = null;
         }

         if (dragged is null || SelectedRoot is null) return (null, null, false);

         switch (target) {
            case CtnTreeNode { IsFolder: true } folder:
               var valid = folder != dragged && !dragged.Contains(folder) && dragged.Parent != folder &&
                           (!dragged.IsFolder || folder.Depth + FolderHeight(dragged) <= CtnInput.MaxFolderDepth);
               return (dragged, folder.Id, valid);
            case CtnRootItem root:
               return (dragged, null, root.Id == SelectedRoot.Id && dragged.Parent is not null);
            case null:
               return (dragged, null, dragged.Parent is not null);
            default:
               return (null, null, false);
         }
      }

      #endregion
   }
}
