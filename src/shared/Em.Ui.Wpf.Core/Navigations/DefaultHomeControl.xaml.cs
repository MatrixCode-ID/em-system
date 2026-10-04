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

      public DefaultHomeControlVm Vm => (DefaultHomeControlVm)DataContext;

      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      public async Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         await _app.ServiceProvider.GetRequiredService<ApprovalAccessCatalog>().LoadAsync();
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

   public class DefaultHomeControlVm : MvvmModelBase
   {
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
      /// Daftar datar semua menu aplikasi. Cukup isi koleksi ini: <see cref="RootAppMenus"/> dan
      /// <see cref="AppMenuGroups"/> otomatis dibangun ulang dari <c>Navigation.MenuPath</c>.
      /// </summary>
      public ObservableCollection<MenuNavigation> AppMenus { get; } = [];

      /// <summary>
      /// Menu tanpa <c>MenuPath</c>, ditampilkan langsung di paling atas tanpa group.
      /// </summary>
      public ObservableCollection<MenuNavigation> RootAppMenus { get; } = [];

      /// <summary>
      /// Group menu level teratas hasil pemecahan <c>MenuPath</c>; tiap group menyimpan
      /// sub-group dan menu miliknya sendiri.
      /// </summary>
      public ObservableCollection<MenuGroup> AppMenuGroups { get; } = [];

      public ObservableCollection<MenuNavigation> StaticToolMenus { get; } = [];

      #region API Connections

      /// <summary>
      /// Daftar profil koneksi API yang ditampilkan kartu API Connections. Ini koleksi milik
      /// <see cref="Core.EmApp.UIConnections"/> apa adanya - bukan salinannya - jadi profil yang
      /// ditambah atau dihapus lewat dialog Connection Config langsung ikut terlihat di kartu.
      /// Null-safe karena XAML membuat ViewModel ini sebelum <see cref="MvvmModelBase.EmApp"/>
      /// sempat di-set.
      /// </summary>
      public ObservableCollection<ApiConnection>? ApiConnections => EmApp?.UIConnections;

      /// <summary>
      /// <c>true</c> kalau aplikasi sedang berjalan dalam mode debug. Kartu API Connections hanya
      /// tampil pada mode ini: di luar debug, koneksi ditentukan aplikasi, bukan dipilih user dari
      /// layar home.
      /// </summary>
      public bool IsDebugMode => EmApp?.IsDebugMode ?? false;

      /// <summary>
      /// Profil yang sedang dipilih di kartu API Connections. Menyetelnya sekaligus menjadikannya
      /// koneksi aktif aplikasi (<see cref="Core.EmApp.ActiveConnection"/>), jadi kartu inilah yang
      /// menentukan server mana yang dipakai - sama seperti combobox koneksi di toolbar multi-tab.
      /// </summary>
      public ApiConnection? SelectedConnection {
         get => Get<ApiConnection?>();
         set => Set(value, OnSelectedConnectionChanged);
      }

      /// <summary>
      /// Teks status hasil tes koneksi terakhir, ditampilkan di kartu. Default: <c>"Not tested"</c>.
      /// </summary>
      public string StatusText {
         get => Get<string>() ?? "Not tested";
         set => Set(value);
      }

      /// <summary>
      /// Warna indikator status hasil tes koneksi. Default: abu-abu (belum ada tes).
      /// </summary>
      public Brush StatusBrush {
         get => Get<Brush>() ?? Brushes.Gray;
         set => Set(value);
      }

      /// <summary>
      /// Menguji koneksi yang sedang dipilih lewat handshake: server harus bisa menandatangani nonce
      /// acak dengan private key dari public key yang dikembalikannya. Kalau berhasil, public key
      /// tersebut disimpan sebagai key server aktif.
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
      /// Menyambungkan ViewModel ini ke aplikasi, lalu memberi tahu UI supaya binding kartu API
      /// Connections dievaluasi ulang. Notifikasinya wajib: XAML sudah membuat ViewModel ini berikut
      /// seluruh binding-nya sebelum <see cref="MvvmModelBase.EmApp"/> sempat di-set, jadi tanpa
      /// ini <see cref="IsDebugMode"/> keburu terbaca <c>false</c> dan kartunya tidak pernah muncul.
      /// </summary>
      /// <param name="app">Objek aplikasi pemilik ViewModel ini.</param>
      public void AttachApp(EmApp app) {
         EmApp = app;
         NotifyChanged(nameof(IsDebugMode));
         NotifyChanged(nameof(ApiConnections));
         SyncSelectedConnection();
      }

      /// <summary>
      /// Menyamakan pilihan di kartu dengan isi <see cref="ApiConnections"/> yang terbaru. Urutan
      /// prioritasnya: profil yang tadi dipilih di kartu ini, lalu koneksi yang sedang aktif di
      /// aplikasi (<see cref="Core.EmApp.ActiveConnection"/>) - supaya pilihan user tetap bertahan
      /// walau control home dibuat ulang saat user pergi lalu kembali ke home - lalu koneksi debug
      /// bawaan, dan terakhir profil pertama yang tersedia.
      /// </summary>
      public void SyncSelectedConnection() {
         if (EmApp is null) return;

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

         if (EmApp is not null) EmApp.ActiveConnection = connection;

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
   /// Satu level pada pohon menu aplikasi, hasil pemecahan <c>Navigation.MenuPath</c>.
   /// </summary>
   public class MenuGroup : MvvmModelBase
   {
      /// <summary>
      /// Nama group, yaitu satu segmen dari <c>MenuPath</c>.
      /// </summary>
      public required string Header { get; init; }

      /// <summary>
      /// Kedalaman group, dimulai dari 1 untuk group teratas. Dipakai view untuk memilih gaya tampilan.
      /// </summary>
      public required int Level { get; init; }

      /// <summary>
      /// Group anak di bawah group ini.
      /// </summary>
      public ObservableCollection<MenuGroup> Groups { get; } = [];

      /// <summary>
      /// Menu yang berada langsung di group ini.
      /// </summary>
      public ObservableCollection<MenuNavigation> Items { get; } = [];
   }

   public class MenuNavigation : MvvmModelBase
   {
      public UiCommandBase? RefreshCardCommand { get; set; }

      public bool HasStorageRefresh => RefreshCardCommand is not null;

      public string? StorageDescription => Navigation?.Name switch {
         "admin.cdn" => "Total public file sizes across all CDN folders. Excludes internal/temporary, hidden/system files, symlinks and filesystem overhead.",
         "admin.nupak" => "Stored packages including recycled versions; excludes database/filesystem overhead.",
         "admin.container" => "Stored unique blobs plus manifests, including retained blobs. Excludes temporary uploads and database/filesystem overhead. Based on registry metadata.",
         _ => null
      };

      public bool IsStorageRefreshing {
         get => Get<bool>();
         set => Set(value);
      }

      public string StorageCaption {
         get => Get<string>() ?? "";
         set => Set(value);
      }

      /// <summary>
      /// Navigasi asal item ini, atau <c>null</c> untuk item yang ditambahkan manual (tool statis
      /// yang tidak membuka navigasi apa pun).
      /// </summary>
      public Navigation? Navigation { get; init; }

      /// <summary>
      /// Judul yang tampil di tile. Disalin sekali saat item dibuat, jadi tidak ikut berubah
      /// kalau judul navigasinya diganti saat aplikasi berjalan.
      /// </summary>
      public required string Title { get; init; }

      /// <summary>
      /// Baris kedua pada tile. Kosongkan bila tidak perlu - barisnya otomatis disembunyikan.
      /// </summary>
      public required string SubTitle { get; init; }

      /// <summary>
      /// Teks tooltip tile. Kosongkan bila tidak perlu - tooltip-nya otomatis tidak muncul.
      /// </summary>
      public string Description { get; init; } = string.Empty;

      /// <summary>
      /// Ikon tile.
      /// </summary>
      public required ImageSource Icon { get; init; }

      /// <summary>
      /// Perintah yang dijalankan saat tile diklik.
      /// </summary>
      // UiCommandBase, bukan UiCommand: membuka sebuah navigasi itu asynchronous sekarang, jadi
      // item yang menuju navigasi memakai UiCommandAsync, sedangkan tool yang hanya membuka dialog
      // tetap sinkron. Keduanya bertemu di base class ini.
      public required UiCommandBase NavigateCommand { get; init; }
   }

   #endregion
}
