using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class DefaultHomeControl : UserControl, INavigationBody
   {
      private readonly EmApp _app;

      /// <summary>Creates a new instance of <see cref="DefaultHomeControl"/>.</summary>
      public DefaultHomeControl(EmApp app) {
         _app = app;
         InitializeComponent();

         // BodyActivator hands the view model the same application object, but only once this
         // constructor has returned - and the connections card is already bound by then.
         Vm.AttachApp(app);

         // The card binds straight to EmApp.UIConnections. That collection is rebuilt whole when
         // the profiles are re-read and changes again whenever one is saved or deleted from the
         // Connection Config dialog, so the pick has to be re-resolved every time it moves.
         _app.UIConnections.CollectionChanged += OnApiConnectionsChanged;
      }

      /// <summary>The vm.</summary>
      public DefaultHomeControlVm Vm => (DefaultHomeControlVm)DataContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public async Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         await _app.ServiceProvider.GetRequiredService<ApprovalAccessCatalog>().LoadAsync();
         Vm.RefreshDebugState();
         RenderStaticItems();
         RenderMainItems();
         await Task.WhenAll(Vm.StaticToolMenus.Where(m => m.HasStorageRefresh).Select(LoadCardStorageAsync));
      }

      private async Task LoadCardStorageAsync(MenuNavigation tile) {
         if (tile.IsStorageRefreshing) return;
         tile.IsStorageRefreshing = true;
         tile.RefreshCardCommand?.RaiseCanExecuteChanged();
         tile.StorageCaption = "Storage: checking...";
         try {
            if (tile.Navigation?.Name == "admin.cdn") {
               var service = _app.ServiceProvider.GetRequiredService<ICdnServices>();
               var status = await service.GetMeta_CdnStatus();
               var pending = status.RequiresRestart ? " · restart pending" : "";
               if (!status.ActiveEnabled) tile.StorageCaption = "CDN disabled" + pending;
               else {
                  var storage = await service.GetMeta_CdnStorageSize();
                  tile.StorageCaption = $"CDN storage: {CdnManagerVm.FormatSize(storage.TotalBytes)} · {storage.FileCount:N0} files" + pending;
               }
            }
            else if (tile.Navigation?.Name == "admin.nupak") {
               var service = _app.ServiceProvider.GetRequiredService<INuPakServices>();
               var size = await service.GetMeta_NuPakStorageSize();
               tile.StorageCaption = $"NuGet storage: {CdnManagerVm.FormatSize(size.TotalBytes)} · {size.Packages:N0} packages";
            }
            else {
               var service = _app.ServiceProvider.GetRequiredService<ICtnServices>();
               var status = await service.GetMeta_CtnStatus();
               var pending = status.RequiresRestart ? " · restart pending" : "";
               if (!status.ActiveEnabled) tile.StorageCaption = "Registry disabled" + pending;
               else {
                  var storage = await service.GetMeta_CtnStorageSize();
                  tile.StorageCaption = $"Image storage: {CdnManagerVm.FormatSize(storage.TotalBytes)}" + pending;
               }
            }
         }
         catch (Exception) {
            tile.StorageCaption = "Storage: unavailable";
         }
         finally {
            tile.IsStorageRefreshing = false;
            tile.RefreshCardCommand?.RaiseCanExecuteChanged();
         }
      }

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         _app.UIConnections.CollectionChanged -= OnApiConnectionsChanged;
         return Task.CompletedTask;
      }

      private void OnApiConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
         Vm.SyncSelectedConnection();


      // The list itself is the application's, shared with the Tools menu of the multi-tab window;
      // this screen only turns it into tiles.
      void RenderStaticItems() {
         Vm.StaticToolMenus.Clear();
         foreach (var tool in _app.GetStaticTools()) {
            if (tool.Navigation != null) {
               AddStaticItems(tool.Navigation);
               continue;
            }

            AddCustomStaticItem(tool.Name, tool.Title, tool.Subtitle, tool.Icon,
               _ => tool.Invoke?.Invoke(Window.GetWindow(this) ?? _app.MainWindow),
               description: tool.Description);
         }
      }

      // Every menu-visible navigation goes in as a flat item; the view model turns MenuPath into
      // the group hierarchy, so nothing here has to know about levels.
      void RenderMainItems() {
         Vm.AppMenus.Clear();
         _app.Navigations
            .Where(r => r.IsMenuVisible && _app.CanOpen(r))
            .EachOf(AddMenuItems);
      }

      // The text and the icon are copied out of the navigation here, not bound to it: the menu
      // has to keep reading the same after a navigation re-titles itself while it is open.
      // A tool the user may not open is left out, by the same rule the application menu follows.
      void AddStaticItems(Navigation nav) {
         if (!_app.CanOpen(nav)) return;

         var tile = new MenuNavigation() {
            Navigation = nav,
            Title = nav.Title,
            SubTitle = nav.Subtitle,
            Description = nav.Description,
            Icon = nav.NavigationIcon,
            NavigateCommand = new UiCommandAsync($"{nav.Name}.navigate", _ => _app.NavigateTo(nav), _ => true)
         };
         if (nav.Name is "admin.container" or "admin.cdn" or "admin.nupak") {
            tile.RefreshCardCommand = new UiCommandAsync($"{nav.Name}.refreshCard",
               _ => LoadCardStorageAsync(tile), _ => !tile.IsStorageRefreshing);
         }
         Vm.StaticToolMenus.Add(tile);
      }

      // A tool that is not a navigation: it carries its own text, icon and command, so it lands
      // in the same collection and renders through the same template as the rest.
      void AddCustomStaticItem(string commandName, string title, string subtitle, ImageSource icon,
         Action<object?> action, Func<object?, bool>? allowedHandler = null, string description = "") {
         var nav = new MenuNavigation {
            Navigation = null,
            Title = title,
            SubTitle = subtitle,
            Description = description,
            Icon = icon,
            NavigateCommand = new UiCommand(commandName, action, allowedHandler),
         };
         Vm.StaticToolMenus.Add(nav);
      }

      void AddMenuItems(Navigation nav) {
         Vm.AppMenus.Add(new MenuNavigation() {
            Navigation = nav,
            Title = nav.Title,
            SubTitle = nav.Subtitle,
            Description = nav.Description,
            Icon = nav.NavigationIcon,
            NavigateCommand = new UiCommandAsync($"{nav.Name}.navigate", _ => _app.NavigateTo(nav), _ => true)
         });
      }
   }

   /// <summary>View model of the default home screen.</summary>
   public class DefaultHomeControlVm : MvvmModelBase
   {
      /// <summary>Creates a new instance of <see cref="DefaultHomeControlVm"/>.</summary>
      public DefaultHomeControlVm() {
         // AppMenus is the only thing a caller fills. Everything the view binds to - the loose
         // items and the group tree - is rebuilt from it here, so no rendering code is needed
         // outside the view model.
         AppMenus.CollectionChanged += (_, _) => RebuildMenuTree();

         WaiterText = "Loading...";
         RegisterCommand(nameof(TestConnectionCommand), TestConnectionCommand,
            () => SelectedConnection is not null);
      }

      /// <summary>
      /// The flat list of all application menus. Just fill this collection: <see cref="RootAppMenus"/> and
      /// <see cref="AppMenuGroups"/> are rebuilt automatically from <c>Navigation.MenuPath</c>.
      /// </summary>
      public ObservableCollection<MenuNavigation> AppMenus { get; } = [];

      /// <summary>
      /// Menus without a <c>MenuPath</c>, shown directly at the very top without a group.
      /// </summary>
      public ObservableCollection<MenuNavigation> RootAppMenus { get; } = [];

      /// <summary>
      /// The top level menu groups resulting from splitting <c>MenuPath</c>; each group holds its own
      /// sub-groups and menus.
      /// </summary>
      public ObservableCollection<MenuGroup> AppMenuGroups { get; } = [];

      /// <summary>The static tool menus.</summary>
      public ObservableCollection<MenuNavigation> StaticToolMenus { get; } = [];

      #region API Connections

      /// <summary>
      /// The list of API connection profiles shown by the API Connections card. This is the collection of
      /// <see cref="Core.EmApp.UIConnections"/> as-is - not a copy of it - so a profile added or removed
      /// through the Connection Config dialog is immediately visible on the card. Null-safe because XAML
      /// creates this view model before <see cref="MvvmModelBase.EmApp"/> could be set.
      /// </summary>
      public ObservableCollection<ApiConnection>? ApiConnections => EmApp?.UIConnections;

      /// <summary>
      /// <c>true</c> while the debug features are on (<see cref="Core.EmApp.IsDebugActive"/>). The API
      /// Connections card only appears then: outside debug, and while a login is being simulated, the
      /// connection is decided on the login screen, not chosen by the user from the home screen.
      /// </summary>
      public bool IsDebugMode => EmApp?.IsDebugActive ?? false;

      /// <summary>
      /// The profile currently chosen on the API Connections card. Setting it also makes it the application's
      /// active connection (<see cref="Core.EmApp.ActiveConnection"/>), so this card decides which server is
      /// used - just like the connection combobox in the multi-tab toolbar.
      /// </summary>
      public ApiConnection? SelectedConnection {
         get => Get<ApiConnection?>();
         set => Set(value, OnSelectedConnectionChanged);
      }

      /// <summary>
      /// The status text of the last connection test, shown on the card. Default: <c>"Not tested"</c>.
      /// </summary>
      public string StatusText {
         get => Get<string>() ?? "Not tested";
         set => Set(value);
      }

      /// <summary>
      /// The color of the status indicator of the last connection test. Default: gray (no test yet).
      /// </summary>
      public Brush StatusBrush {
         get => Get<Brush>() ?? Brushes.Gray;
         set => Set(value);
      }

      /// <summary>
      /// Tests the chosen connection through the handshake: the server must be able to sign a random nonce
      /// with the private key of the public key it returns. If it succeeds, that public key is stored as the
      /// active server key.
      /// </summary>
      public async Task TestConnectionCommand() {
         var connection = SelectedConnection;
         if (connection is null) return;

         try {
            InWaiting = IsBusy = true;
            StatusText = WaiterText = "Testing...";
            StatusBrush = Brushes.Gray;
            await Task.Yield();
            using var api = connection.CreateApiClient();
            await api.HandshakeAsync();
            StatusText = "Connected. Server key verified.";
            StatusBrush = Brushes.Green;
            InWaiting = IsBusy = false;
            WaiterText = "Loading...";
         }
         catch (Exception x) {
            InWaiting = IsBusy = false;
            StatusText = "Connection test failed.";
            StatusBrush = Brushes.Red;
            WaiterText = "Loading...";
            AlertError(x);
         }
      }

      /// <summary>
      /// Connects this view model to the application, then tells the UI to re-evaluate the API Connections
      /// card bindings. The notification is required: XAML creates this view model together with all its
      /// bindings before <see cref="MvvmModelBase.EmApp"/> could be set, so without it
      /// <see cref="IsDebugMode"/> would be read as <c>false</c> too early and the card would never appear.
      /// </summary>
      /// <param name="app">The application object that owns this view model.</param>
      public void AttachApp(EmApp app) {
         EmApp = app;
         NotifyChanged(nameof(IsDebugMode));
         NotifyChanged(nameof(ApiConnections));
         SyncSelectedConnection();
      }

      // Simulate Login turns the debug features off and on again while this screen stays alive, so the
      // card is told to read the switch again every time the home screen reloads.
      internal void RefreshDebugState() => NotifyChanged(nameof(IsDebugMode));

      /// <summary>
      /// Aligns the choice on the card with the latest content of <see cref="ApiConnections"/>. Priority
      /// order: the profile chosen on this card earlier, then the connection active in the application
      /// (<see cref="Core.EmApp.ActiveConnection"/>) - so the user's choice survives even if the home control
      /// is created again when the user leaves and comes back to home - then the default debug connection, and
      /// lastly the first profile available.
      /// </summary>
      public void SyncSelectedConnection() {
         if (EmApp is null) return;

         // Outside the debug features the card is hidden and the login screen owns the pick, so the
         // card only mirrors the active connection and never moves it.
         if (!EmApp.IsDebugActive) {
            SelectedConnection = FindConnection(EmApp.ActiveConnection?.ProfileName);
            return;
         }

         SelectedConnection =
            FindConnection(_selectedProfileName)
            ?? FindConnection(EmApp.ActiveConnection?.ProfileName)
            ?? EmApp.DefaultDebugConnection
            ?? EmApp.UIConnections.FirstOrDefault();
      }

      // Looking the pick up by profile name rather than by reference: rebuilding the profile list
      // replaces every stored entry with a brand new ApiConnection object, so a reference held from
      // before the rebuild - ActiveConnection included - is no longer in the collection.
      private ApiConnection? FindConnection(string? profileName) =>
         profileName is null
            ? null
            : EmApp!.UIConnections.FirstOrDefault(r => r.ProfileName == profileName);

      // The profile the user last picked on this card, remembered by name for the reason above.
      private string? _selectedProfileName;

      private void OnSelectedConnectionChanged(ApiConnection? connection) {
         // A rebuild empties the selection for a moment. The remembered name is deliberately left
         // untouched then, so SyncSelectedConnection can put the same profile back afterwards.
         if (connection is not null) _selectedProfileName = connection.ProfileName;

         if (EmApp is { IsDebugActive: true } && !ReferenceEquals(EmApp.ActiveConnection, connection))
            EmApp.ActiveConnection = connection;

         // The result belongs to the profile that was tested, so picking another one drops it.
         StatusText = "Not tested";
         StatusBrush = Brushes.Gray;

         Commands[nameof(TestConnectionCommand)]?.RaiseCanExecuteChanged();
      }

      #endregion

      private void RebuildMenuTree() {
         RootAppMenus.Clear();
         AppMenuGroups.Clear();

         foreach (var menu in AppMenus) {
            var segments = MenuPaths.Split(menu.Navigation?.MenuPath);
            // A navigation without a usable path is not an error - it simply sits above the tree.
            if (segments.Length == 0) {
               RootAppMenus.Add(menu);
               continue;
            }

            ResolveGroup(segments).Items.Add(menu);
         }
      }

      // Walks the path segment by segment, creating the groups that do not exist yet, and returns
      // the deepest one. Matching is case-insensitive so "Sales" and "SALES" stay one group.
      private MenuGroup ResolveGroup(string[] segments) {
         var groups = AppMenuGroups;
         MenuGroup? current = null;

         for (var level = 0; level < segments.Length; level++) {
            var header = segments[level];
            current = groups.FirstOrDefault(r => string.Equals(r.Header, header, MenuPaths.SegmentComparison));
            if (current == null) {
               current = new MenuGroup { Header = header, Level = level + 1 };
               groups.Add(current);
            }

            groups = current.Groups;
         }

         return current!;
      }
   }

   #region Support Objects

   /// <summary>
   /// One level of the application menu tree, resulting from splitting <c>Navigation.MenuPath</c>.
   /// </summary>
   public class MenuGroup : MvvmModelBase
   {
      /// <summary>
      /// The group name, that is, one segment of <c>MenuPath</c>.
      /// </summary>
      public required string Header { get; init; }

      /// <summary>
      /// The depth of the group, starting from 1 for the top group. Used by the view to choose a display style.
      /// </summary>
      public required int Level { get; init; }

      /// <summary>
      /// The child groups under this group.
      /// </summary>
      public ObservableCollection<MenuGroup> Groups { get; } = [];

      /// <summary>
      /// The menus that sit directly in this group.
      /// </summary>
      public ObservableCollection<MenuNavigation> Items { get; } = [];
   }

   /// <summary>One tile of the home menu.</summary>
   public class MenuNavigation : MvvmModelBase
   {
      /// <summary>The refresh card command.</summary>
      public UiCommandBase? RefreshCardCommand { get; set; }

      /// <summary>Indicates there is storage refresh.</summary>
      public bool HasStorageRefresh => RefreshCardCommand is not null;

      /// <summary>The storage description.</summary>
      public string? StorageDescription => Navigation?.Name switch {
         "admin.cdn" => "Total public file sizes across all CDN folders. Excludes internal/temporary, hidden/system files, symlinks and filesystem overhead.",
         "admin.nupak" => "Stored packages including recycled versions; excludes database/filesystem overhead.",
         "admin.container" => "Stored unique blobs plus manifests, including retained blobs. Excludes temporary uploads and database/filesystem overhead. Based on registry metadata.",
         _ => null
      };

      /// <summary>Indicates storage refreshing.</summary>
      public bool IsStorageRefreshing {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>The storage caption.</summary>
      public string StorageCaption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>
      /// The navigation this item comes from, or <c>null</c> for an item that was added manually (a static
      /// tool that opens no navigation).
      /// </summary>
      public Navigation? Navigation { get; init; }

      /// <summary>
      /// The title shown on the tile. Copied once when the item is created, so it does not change if the
      /// navigation's title is replaced while the application runs.
      /// </summary>
      public required string Title { get; init; }

      /// <summary>
      /// The second line on the tile. Leave it empty when not needed - the line is hidden automatically.
      /// </summary>
      public required string SubTitle { get; init; }

      /// <summary>
      /// The tooltip text of the tile. Leave it empty when not needed - the tooltip does not appear
      /// automatically.
      /// </summary>
      public string Description { get; init; } = string.Empty;

      /// <summary>
      /// Ikon tile.
      /// </summary>
      public required ImageSource Icon { get; init; }

      /// <summary>
      /// The command that runs when the tile is clicked.
      /// </summary>
      // UiCommandBase, not UiCommand: opening a navigation is asynchronous now, so an item that leads to a
      // navigation uses UiCommandAsync, while a tool that only opens a dialog stays synchronous. The two meet
      // in this base class.
      public required UiCommandBase NavigateCommand { get; init; }
   }

   #endregion
}
