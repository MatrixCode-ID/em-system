using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// Layar home bawaan: kotak pencarian dan pohon menu layar-layar yang didaftarkan module. Dipakai
   /// selama aplikasi tidak menyediakan home-nya sendiri lewat <c>EmAppBuilder.UseHomeNavigation</c>.
   /// </summary>
   /// <remarks>
   /// Identitas aplikasi, akun, server, tema, dan keluar tidak ada di sini - semuanya tinggal di panel
   /// account yang dibuka dari badge di bilah atas, supaya layar ini murni berisi jalan menuju layar
   /// lain. Nama aplikasinya sendiri sudah terbaca di judul bilah atas.
   /// </remarks>
   public partial class DefaultHomeControl : ContentView, INavigationBody
   {
      public DefaultHomeControl() {
         InitializeComponent();
      }

      /// <summary>View model layar ini, dibaca balik dari BindingContext yang dipasang di XAML.</summary>
      public DefaultHomeControlVm Vm => (DefaultHomeControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>
   /// Satu kartu layar di menu home. Membawa sendiri perintah bukanya, jadi template yang
   /// menggambarnya tidak perlu tahu view model siapa pun di atasnya - dan karena itu kartu yang
   /// sama bisa dipakai di daftar teratas maupun di dalam grup sedalam apa pun.
   /// </summary>
   public sealed class MenuNavigationVm : MvvmModelBase
   {
      public MenuNavigationVm(EmApp app, Navigation navigation) {
         EmApp = app;
         Navigation = navigation;
         // Judul dan keterangannya disalin sekali di sini, tidak diikat ke navigasinya: menu harus
         // tetap terbaca sama walau navigasinya berganti judul selagi menu ini terbuka.
         Title = navigation.Title;
         Subtitle = navigation.Subtitle;
         Description = navigation.Description;

         RegisterCommand(nameof(OpenCommand), OpenCommand);
      }

      /// <summary>Layar yang dibuka kartu ini.</summary>
      public Navigation Navigation { get; }

      /// <summary>Judul di kartu.</summary>
      public string Title { get; }

      /// <summary>Baris kedua di kartu.</summary>
      public string Subtitle { get; }

      /// <summary>Penjelasan panjang layar ini.</summary>
      public string Description { get; }

      /// <summary><c>true</c> kalau ada baris kedua yang perlu digambar.</summary>
      public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

      public Task OpenCommand() => EmApp!.NavigateTo(Navigation);
   }

   /// <summary>
   /// Satu tingkat di pohon menu home, hasil pemecahan <c>Navigation.MenuPath</c>. Sebuah grup bisa
   /// berisi kartu layar, grup lain, atau keduanya.
   /// </summary>
   public sealed class MenuGroup : MvvmModelBase
   {
      public MenuGroup() {
         RegisterCommand(nameof(ToggleCommand), ToggleCommand);
      }

      /// <summary>Nama grup, yaitu satu segmen dari <c>MenuPath</c>.</summary>
      public required string Header { get; init; }

      /// <summary>Kedalaman grup, dimulai dari 1 untuk grup teratas.</summary>
      public required int Level { get; init; }

      /// <summary>Grup anak di bawah grup ini.</summary>
      public ObservableCollection<MenuGroup> Groups { get; } = [];

      /// <summary>Kartu layar yang berada langsung di grup ini.</summary>
      public ObservableCollection<MenuNavigationVm> Items { get; } = [];

      /// <summary>
      /// Apakah isi grup ini sedang terbuka. Mulai terbuka: menu yang seluruhnya tertutup saat layar
      /// home dibuka menyembunyikan justru apa yang dicari penggunanya.
      /// </summary>
      public bool IsExpanded {
         get => Get<bool>(true);
         set => Set(value, _ => NotifyChanged(nameof(ChevronGlyph)));
      }

      /// <summary>Chevron di kepala grup: menunjuk ke bawah saat terbuka, ke kanan saat tertutup.</summary>
      public string ChevronGlyph => IsExpanded ? FontIcons.ChevronDown : FontIcons.ChevronRight;

      /// <summary>
      /// Geseran ke kanan yang menandakan kedalaman grup ini. Hanya satu tingkat yang digeser tiap
      /// kali, karena pohonnya bisa sedalam apa pun dan geseran besar akan kehabisan lebar layar.
      /// </summary>
      public Thickness Indent => new((Level - 1) * 12, 0, 0, 0);

      public void ToggleCommand() => IsExpanded = !IsExpanded;
   }

   /// <summary>View model <see cref="DefaultHomeControl"/>.</summary>
   public class DefaultHomeControlVm : MvvmModelBase
   {
      /// <summary>
      /// Daftar datar semua layar yang tampil di menu. Cukup isi koleksi ini:
      /// <see cref="RootAppMenus"/> dan <see cref="AppMenuGroups"/> dibangun ulang sendiri dari
      /// <c>Navigation.MenuPath</c> setiap kali isinya berubah.
      /// </summary>
      public ObservableCollection<MenuNavigationVm> AppMenus { get; } = [];

      /// <summary>Layar tanpa <c>MenuPath</c>, digambar langsung di atas tanpa grup.</summary>
      public ObservableCollection<MenuNavigationVm> RootAppMenus { get; } = [];

      /// <summary>Grup teratas hasil pemecahan <c>MenuPath</c>, masing-masing membawa isinya sendiri.</summary>
      public ObservableCollection<MenuGroup> AppMenuGroups { get; } = [];

      /// <summary><c>true</c> kalau belum ada satu pun layar module yang tampil.</summary>
      public bool IsModuleListEmpty => AppMenus.Count == 0;

      /// <summary>
      /// Membangun ulang isi layar: daftar layar dan pohon menunya. Dipanggil setiap kali layar ini
      /// dibuka atau diminta memuat ulang.
      /// </summary>
      public Task ReloadAsync() {
         if (EmApp is not { } app) return Task.CompletedTask;

         AppMenus.Clear();
         app.Navigations
            .Where(r => r.IsMenuVisible && app.CanOpen(r))
            .OrderBy(r => r.OrderIndex)
            .Select(r => new MenuNavigationVm(app, r))
            .EachOf(AppMenus.Add);

         RebuildMenuTree();
         NotifyChanged(nameof(IsModuleListEmpty));
         return Task.CompletedTask;
      }

      private void RebuildMenuTree() {
         RootAppMenus.Clear();
         AppMenuGroups.Clear();

         foreach (var menu in AppMenus) {
            var segments = SplitMenuPath(menu.Navigation.MenuPath);
            // Layar tanpa jalur menu bukan kesalahan - ia hanya duduk di atas pohon.
            if (segments.Length == 0) {
               RootAppMenus.Add(menu);
               continue;
            }

            ResolveGroup(segments).Items.Add(menu);
         }
      }

      // "SALES/Administration" -> ["SALES", "Administration"]. Segmen kosong dibuang supaya pemisah
      // yang terlanjur ganda atau menggantung tidak pernah melahirkan grup tanpa nama.
      private static string[] SplitMenuPath(MenuPath? path) {
         if (path == null || path.IsEmptyPath) return [];
         return path.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      }

      // Menyusuri jalur segmen demi segmen, membuat grup yang belum ada, lalu mengembalikan yang
      // paling dalam. Pembandingannya mengabaikan besar-kecil huruf supaya "Sales" dan "SALES" tetap
      // satu grup yang sama.
      private MenuGroup ResolveGroup(string[] segments) {
         var groups = AppMenuGroups;
         MenuGroup? current = null;

         for (var level = 0; level < segments.Length; level++) {
            var header = segments[level];
            current = groups.FirstOrDefault(r => string.Equals(r.Header, header, StringComparison.OrdinalIgnoreCase));
            if (current == null) {
               current = new MenuGroup { Header = header, Level = level + 1 };
               groups.Add(current);
            }

            groups = current.Groups;
         }

         return current!;
      }
   }
}
