using System.Windows;
using FontAwesome6;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Navigations
{
   // The part of the Containers tab that changes data: root, folder, container, move, and delete. All
   // changes go through RunMutationAsync: the server's 400/404/409 answers are shown as-is, then the part
   // of the screen that was affected is read again.
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

      /// <summary>Text of the edit button in the details: <c>Rename</c> for a folder, <c>Edit</c> for a container.</summary>
      public string EditNodeCaption => SelectedNode is { IsFolder: true } ? "Rename" : "Edit";

      // The folder where a new folder or container is created: the selected folder, the folder where the
      // selected container sits, or - with no selection - the root itself (null).
      private CtnTreeNode? CreationParent => SelectedNode is { IsFolder: true } folder ? folder : SelectedNode?.Parent;

      #endregion

      #region Root

      /// <summary>Asks for a name and description, then creates a new root, and selects it.</summary>
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

      /// <summary>Only when the registry is on and the screen is not busy.</summary>
      public bool NewRootCommandAllowed() => CanAct;

      /// <summary>Changes the description and active status of the selected root.</summary>
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

      /// <summary>Only for the selected root.</summary>
      public bool EditRootCommandAllowed() => CanAct && SelectedRoot is not null;

      /// <summary>
      /// Deletes the selected root after confirmation. The server refuses (409) a root that still has folders,
      /// containers, or robot rights; its answer is shown as-is.
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

      /// <summary>Only for the selected root.</summary>
      public bool DeleteRootCommandAllowed() => CanAct && SelectedRoot is not null;

      #endregion

      #region Folder and container

      /// <summary>
      /// Asks for a name, then creates a new folder in the selected folder (or in the folder where the
      /// selected container sits, or in the root when nothing is selected), and selects it.
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

      /// <summary>Only for the selected root, and as long as the new folder does not pass the maximum depth.</summary>
      public bool NewFolderCommandAllowed() =>
         CanAct && SelectedRoot is not null && (CreationParent?.Depth ?? 0) < CtnInput.MaxFolderDepth;

      /// <summary>
      /// Asks for a name and description, then creates a new container in the selected folder (or in the
      /// root), and selects it. A container must exist before an image can be pushed to it.
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

      /// <summary>Only for the selected root.</summary>
      public bool NewImageCommandAllowed() => CanAct && SelectedRoot is not null;

      /// <summary>
      /// Renames the selected folder, or edits the description and active status of the selected container.
      /// The names of roots and containers cannot be changed: the contract has no rename for either.
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

      /// <summary>Only for the selected folder or container.</summary>
      public bool EditNodeCommandAllowed() => CanAct && SelectedNode is not null;

      /// <summary>
      /// Moves the selected folder or container through the folder picker dialog. The pull name does not
      /// change because folders are not part of the pull name.
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

      /// <summary>Only for the selected folder or container.</summary>
      public bool MoveNodeCommandAllowed() => CanAct && SelectedNode is not null;

      /// <summary>
      /// Deletes the selected folder (which must be empty) or the selected container, after confirmation.
      /// Deleting a container removes its manifests, tags, and blob links; the files on disk wait for garbage
      /// collection, and the confirmation says so.
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

      /// <summary>Only for the selected container, or a folder that is already empty.</summary>
      public bool DeleteNodeCommandAllowed() =>
         CanAct && SelectedNode is { } node && (!node.IsFolder || node.Children.Count == 0);

      #endregion

      #region Tag and manifest

      /// <summary>Deletes one tag of the selected container; its manifest stays.</summary>
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

      /// <summary>Whether the delete tag command may run now.</summary>
      public bool DeleteTagCommandAllowed(string? tag) => CanAct && SelectedNode?.Image is not null && !string.IsNullOrEmpty(tag);

      /// <summary>Deletes one manifest together with its tags. Refused by the server (409) while still referenced by an index.</summary>
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

      /// <summary>Whether the delete manifest command may run now.</summary>
      public bool DeleteManifestCommandAllowed(CtnManifestItem? manifest) => CanAct && SelectedNode?.Image is not null && manifest is not null;

      #endregion

      #region Garbage collection

      /// <summary>Opens the garbage collection review dialog; the storage is read again if GC is run.</summary>
      public async Task GarbageCollectionCommand() {
         var dialog = new CtnGcDialog(Service) { Owner = DialogOwner };
         dialog.ShowDialog();
         if (dialog.Vm.HasRun) await RefreshStorageCommand();
      }

      /// <summary>Whether the garbage collection command may run now.</summary>
      public bool GarbageCollectionCommandAllowed() => CanAct;

      #endregion

      #region Move

      // The destinations that may be chosen to move a node: the root itself and every folder, except where
      // that node already is, the node itself with its content (a folder must not go into itself), and
      // folders that would make the tree pass the maximum depth.
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

      // Number of folder levels from this folder downward, counting this folder itself.
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
      /// Accepts the drop of a tree node: onto a folder row (into that folder), onto the root row (out to the
      /// root's top level), or onto the empty space of the tree (same as the root row). A drop onto another
      /// root is not accepted because the server only moves within the same root.
      /// </summary>
      public Task DropCommand(object? payload) {
         var drop = ResolveDrop(payload);
         return drop.IsValid ? MoveAsync(drop.Node!, drop.TargetFolderId) : Task.CompletedTask;
      }

      /// <summary>Allowed when the drop is a valid move; the "not allowed" cursor appears otherwise.</summary>
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
