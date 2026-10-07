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
   /// The container registry manager screen: roots, folders, and containers (three panels). Robots and
   /// their rights are managed in UserManager. A server whose registry is not turned on answers 404; this
   /// screen then only shows the "not turned on" state.
   /// </summary>
   public partial class ContainerManager : UserControl, INavigationBody
   {
      /// <summary>
      /// Creates the container registry manager screen for application <paramref name="app"/>.
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

      /// <summary>The view model of this screen.</summary>
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

   /// <summary>One root in the left list of the Containers tab.</summary>
   public class CtnRootItem
   {
      internal CtnRootItem(CtnRootInfo info) {
         Info = info;
      }

      /// <summary>The root data as-is from the server.</summary>
      public CtnRootInfo Info { get; }

      /// <summary>Id root.</summary>
      public string Id => Info.Id;

      /// <summary>Name of the root, the first part of the pull name.</summary>
      public string Name => Info.Name;

      /// <summary>Description; an empty string when there is none.</summary>
      public string Description => Info.Description ?? "";

      /// <summary><c>true</c> when there is a description.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Info.Description);

      /// <summary>Active status.</summary>
      public bool IsActive => Info.IsActive;

      /// <summary>Number of containers, for the badge in the list.</summary>
      public int ImageCount => Info.ImageCount;

      /// <summary>Number of folders and containers, e.g. <c>"3 folders · 12 containers"</c>.</summary>
      public string Summary =>
         $"{Info.FolderCount:N0} folder{(Info.FolderCount == 1 ? "" : "s")} · {Info.ImageCount:N0} container{(Info.ImageCount == 1 ? "" : "s")}";

      /// <summary>Creation time in local time.</summary>
      public string CreatedCaption => ContainerManagerVm.LocalTime(Info.CreatedAt);
   }

   /// <summary>
   /// One node of the tree in the middle of the Containers tab: a folder or a container. The server sends a
   /// flat tree; <see cref="ContainerManagerVm"/> arranges it into these nodes. This node is also the drag
   /// payload for moving a folder or container to another folder.
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

      /// <summary>The folder data; <c>null</c> for a container node.</summary>
      public CtnFolderInfo? Folder { get; }

      /// <summary>The container data; <c>null</c> for a folder node.</summary>
      public CtnImageInfo? Image { get; }

      /// <summary>The parent folder; <c>null</c> when directly under the root.</summary>
      public CtnTreeNode? Parent { get; }

      /// <summary>Content of a folder node: folders first, then containers, each sorted by name.</summary>
      public ObservableCollection<CtnTreeNode> Children { get; } = [];

      /// <summary><c>true</c> for a folder.</summary>
      public bool IsFolder => Folder is not null;

      /// <summary>Id of the folder or container.</summary>
      public string Id => Folder?.Id ?? Image!.Id;

      /// <summary>Name of the folder or container.</summary>
      public string Name => Folder?.Name ?? Image!.Name;

      /// <summary>Description of the container; an empty string for a folder or when there is none.</summary>
      public string Description => Image?.Description ?? "";

      /// <summary><c>true</c> when <see cref="Description"/> has content.</summary>
      public bool HasDescription => !string.IsNullOrWhiteSpace(Image?.Description);

      /// <summary>Creation time of the container in local time; empty for a folder.</summary>
      public string CreatedCaption => Image is null ? "" : ContainerManagerVm.LocalTime(Image.CreatedAt);

      /// <summary>Whether the folder or container is active; a folder is always active.</summary>
      public bool IsActive => Image?.IsActive ?? true;

      /// <summary>Icon of the node: a folder or a container box.</summary>
      public EFontAwesomeIcon Icon => IsFolder
         ? (IsExpanded ? EFontAwesomeIcon.Solid_FolderOpen : EFontAwesomeIcon.Solid_Folder)
         : EFontAwesomeIcon.Solid_Box;

      /// <summary>Caption to the right of the name: the container's tag count, or the folder's content.</summary>
      public string Caption => IsFolder
         ? (Children.Count == 0 ? "empty" : $"{Children.Count:N0} item{(Children.Count == 1 ? "" : "s")}")
         : $"{Image!.TagCount:N0} tag{(Image.TagCount == 1 ? "" : "s")}";

      /// <summary>Depth of the node; a folder directly under the root is 1.</summary>
      public int Depth => Parent is null ? 1 : Parent.Depth + 1;

      /// <summary>Path of the folder from the root, e.g. <c>"services / api"</c>.</summary>
      public string Path => Parent is null ? Name : $"{Parent.Path} / {Name}";

      /// <summary>All nodes below this node, not including itself.</summary>
      public IEnumerable<CtnTreeNode> Descendants() {
         foreach (var child in Children) {
            yield return child;
            foreach (var deeper in child.Descendants()) yield return deeper;
         }
      }

      /// <summary><c>true</c> when <paramref name="other"/> is below this node, at any depth.</summary>
      public bool Contains(CtnTreeNode other) {
         for (var node = other.Parent; node is not null; node = node.Parent) {
            if (node == this) return true;
         }

         return false;
      }

      /// <summary>Whether the folder is open or closed.</summary>
      public bool IsExpanded {
         get => Get<bool>();
         set => Set(value, _ => NotifyChanged(nameof(Icon)));
      }

      /// <summary>
      /// <c>true</c> when this container is proven to have a blob that is missing from the server storage.
      /// Only known after the container's manifests are read (when it is selected), and gone again when the
      /// tree is read again.
      /// </summary>
      public bool HasMissingBlobs {
         get => Get<bool>();
         internal set => Set(value);
      }

      /// <summary>This node is the selected one; set two-way by the TreeView item style.</summary>
      public bool IsSelected {
         get => Get<bool>();
         set => Set(value, selected => {
            if (selected) _owner.OnNodeSelected(this);
            else _owner.OnNodeDeselected(this);
         });
      }

      internal void RefreshCaption() => NotifyChanged(nameof(Caption));
   }

   /// <summary>One manifest in the container details: tag, digest, media type, size, and who pushed it.</summary>
   public class CtnManifestItem
   {
      internal CtnManifestItem(CtnManifestInfo info) {
         Info = info;
      }

      /// <summary>The manifest data as-is from the server.</summary>
      public CtnManifestInfo Info { get; }

      /// <summary>Digest lengkap.</summary>
      public string Digest => Info.Digest;

      /// <summary>The digest shortened for display.</summary>
      public string ShortDigest => CtnInput.ShortDigest(Info.Digest);

      /// <summary>Media type manifest.</summary>
      public string MediaType => Info.MediaType;

      /// <summary>The tags that point to this manifest.</summary>
      public string[] Tags => Info.Tags;

      /// <summary><c>true</c> when a tag points to this manifest.</summary>
      public bool HasTags => Info.Tags.Length > 0;

      /// <summary>
      /// Size of the manifest itself - not the size of the image or its layers, which the server does not report.
      /// </summary>
      public string SizeCaption => $"Manifest size {CdnManagerVm.FormatSize(Info.Size)}";

      /// <summary><c>true</c> when a blob of this manifest has no file in the server storage.</summary>
      public bool HasMissingBlobs => Info.MissingBlobCount > 0;

      /// <summary>Warning about a missing blob; empty when all blobs exist.</summary>
      public string MissingBlobCaption => Info.MissingBlobCount switch {
         0 => "",
         var missing => $"{missing} of {Info.BlobCount} blob(s) missing from server storage - this image cannot be pulled " +
                        "until the files are restored. The database may be shared with a server whose storage holds them."
      };

      /// <summary>One caption line: manifest size, push time, and the pusher.</summary>
      public string MetaCaption => $"{SizeCaption} · pushed {PushedCaption} · {PushedByCaption}";

      /// <summary>Push time in local time.</summary>
      public string PushedCaption => ContainerManagerVm.LocalTime(Info.PushedAt);

      /// <summary>Who pushed it: the robot's name, or "deleted robot" when the robot has been deleted.</summary>
      public string PushedByCaption => Info.PushedBy is { Length: > 0 } robot ? robot : "deleted robot";
   }

   /// <summary>
   /// View model for <see cref="ContainerManager"/>. Data and selection are in Containers.cs, mutations in
   /// Containers.Edit.cs; this file holds the state and the handling of server answers.
   /// </summary>
   public partial class ContainerManagerVm : MvvmModelBase
   {
      /// <summary>
      /// Creates a new view model and registers all commands of the screen.
      /// </summary>
      public ContainerManagerVm() {
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
         RegisterContainerCommands();
      }

      private ICtnServices Service => EmApp!.ServiceProvider.GetRequiredService<ICtnServices>();

      /// <summary>The storage caption.</summary>
      public string StorageCaption {
         get => Get<string>() ?? "Storage: not checked";
         private set => Set(value);
      }

      /// <summary>The storage detail.</summary>
      public string StorageDetail {
         get => Get<string>() ?? "";
         private set => Set(value);
      }

      #region Data

      /// <summary>
      /// <c>true</c> when the server answers that its container registry is not turned on. While that holds,
      /// all commands except Refresh are off and the screen only shows its message.
      /// </summary>
      public bool IsRegistryDisabled {
         get => Get<bool>();
         private set => Set(value, _ => NotifyChanged(nameof(IsRegistryAvailable)));
      }

      /// <summary>The opposite of <see cref="IsRegistryDisabled"/>, for binding.</summary>
      public bool IsRegistryAvailable => !IsRegistryDisabled;

      /// <summary>
      /// <c>true</c> since the first answer from the server was received - the root list or the news that the
      /// registry is off. Before that all commands except Refresh are off.
      /// </summary>
      public bool IsLoaded {
         get => Get<bool>();
         private set => Set(value);
      }

      /// <summary>
      /// The registry's <c>host[:port]</c> for <c>docker</c> commands, taken from the active connection - the
      /// server does not send it. Empty when there is no active connection yet.
      /// </summary>
      public string RegistryHost => CtnInput.RegistryHost(EmApp?.ActiveConnection?.Host);

      /// <summary>
      /// <c>true</c> when the server address uses plain HTTP to something other than <c>localhost</c>: Docker
      /// refuses <c>docker login</c> to such an address, so the screen shows a note.
      /// </summary>
      public bool IsInsecureHost => CtnInput.IsInsecureRemote(EmApp?.ActiveConnection?.Host);

      #endregion

      #region Commands

      /// <summary>Reads the whole screen again from the server.</summary>
      public Task RefreshCommand() => ReloadAsync();

      /// <summary>Always allowed while the screen is not busy, including when the registry is off.</summary>
      public bool RefreshCommandAllowed() => IsNotBusy;

      #endregion

      #region Methods

      /// <summary>
      /// Reads the roots and the tree of the selected root again. Called by the host every time the screen is
      /// opened through navigation and from the Refresh button.
      /// </summary>
      public Task ReloadAsync() => RunBusyAsync("Loading...", ReadAllAsync);

      private bool CanAct => IsNotBusy && IsLoaded && IsRegistryAvailable;

      private async Task ReadAllAsync() {
         NotifyChanged(nameof(RegistryHost));
         NotifyChanged(nameof(IsInsecureHost));
         NotifyChanged(nameof(DetailPullName));
         if (!await ReadRootsAsync()) return;
      }

      /// <summary>Indicates storage refreshing.</summary>
      public bool IsStorageRefreshing { get => Get<bool>(); private set { Set(value); RaiseCommandsChanged(); } }
      /// <summary>Whether the refresh storage command may run now.</summary>
      public bool RefreshStorageCommandAllowed() => !IsStorageRefreshing;
      /// <summary>Runs the refresh storage command.</summary>
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

      /// <summary>UTC time from the server in local time, for display.</summary>
      internal static string LocalTime(DateTime utc) =>
         utc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);

      // The "Server Error: " prefix is removed from the server's message so only the sentence is shown.
      internal static string ServerMessage(ActionException x) => x.Message.Replace("Server Error: ", "");

      // One change on the server. 400 (invalid name), 404 (the item was already deleted by someone else), and
      // 409 (conflict) are everyday answers: shown as a notice, not an error, then the affected part of the
      // screen is read again through reread - whether it succeeded or not, because what is shown may already
      // be stale. Other answers fall to AlertError of RunBusyAsync.
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

      // showOverlay: false keeps IsBusy - all commands stay off - without the wait layer, for work that shows
      // its own progress.
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

      // All commands here depend on the same facts - busy, registry off, selected row - so they are
      // re-evaluated as one set.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
