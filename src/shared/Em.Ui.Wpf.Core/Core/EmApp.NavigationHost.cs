using System.IO;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp : INavigationHost
   {
      private readonly List<Navigation> _allNavigations = [];
      private NavigationStack? _mainStack;

      /// <summary>
      /// Dipicu setiap kali sebuah perpindahan navigasi berhasil, di stack mana pun. Pengirimnya adalah
      /// navigasi yang ditinggalkan.
      /// </summary>
      public event EventHandler<NavigationEventArgs>? Navigated;

      #region Explicit INavigationHost Implementation

      IEnumerable<INavigation> INavigationHost.Navigations => Navigations;
      INavigationStack INavigationHost.MainStack => MainStack;
      INavigationEntry? INavigationHost.FindEntry(string title) => FindEntry(title);
      Task<bool> INavigationHost.NavigateTo(string navigationName, object? data) => NavigateTo(navigationName, data);

      Task<bool> INavigationHost.NavigateTo(INavigation navigation, object? data) =>
         NavigateTo((Navigation)navigation, data);

      #endregion

      #region Properties

      /// <summary>Seluruh navigasi yang dikenal aplikasi.</summary>
      public IEnumerable<Navigation> Navigations => _allNavigations;

      /// <summary>
      /// Stack utama aplikasi, yang ditampilkan window utama. Di layout satu halaman stack ini punya
      /// home dan ditampilkan sebagai satu jalur maju-mundur. Di layout multi-tab stack ini tidak punya
      /// home: setiap entrinya adalah satu tab window utama, atau layar login selama belum ada yang
      /// masuk.
      /// </summary>
      public NavigationStack MainStack => _mainStack!;

      #endregion

      #region Methods

      private void AddNavigation(Navigation navigation) {
         // The home navigation is registered explicitly, and an application is free to hand that
         // same instance to the builder as well, so registering twice has to be harmless.
         if (_allNavigations.Contains(navigation)) return;

         navigation.EmApp ??= this;
         _allNavigations.Add(navigation);
      }

      internal void RaiseNavigated(object sender, NavigationEventArgs args) => Navigated?.Invoke(sender, args);

      /// <summary>
      /// Mencari entri berjudul <paramref name="title"/> di semua stack - stack utama dan stack setiap
      /// window lain - home termasuk. Huruf besar dan kecil tidak dibedakan: judul adalah kunci, dan
      /// dua judul yang hanya beda huruf menunjuk dokumen yang sama.
      /// </summary>
      /// <param name="title">Judul entri yang dicari.</param>
      /// <returns>Entrinya, atau <c>null</c> kalau judul itu belum dipakai di mana pun.</returns>
      public NavigationEntry? FindEntry(string title) {
         foreach (var stack in AllStacks) {
            if (stack.FindEntry(title) is { } entry) return entry;
         }

         return null;
      }

      /// <summary>
      /// Membuka navigasi bernama <paramref name="name"/> relatif ke <see cref="MainStack"/>. Dari dalam
      /// body, pakai <see cref="NavigationEntry.NavigateTo(string,object?)"/> milik entrinya sendiri.
      /// </summary>
      /// <param name="name">Nama navigasi tujuan.</param>
      /// <param name="data">Parameter untuk layar tujuan, atau <c>null</c> kalau tidak ada.</param>
      /// <returns>
      /// <c>false</c> kalau namanya tidak dikenal, user tidak berhak membukanya, atau perpindahannya
      /// ditolak.
      /// </returns>
      public Task<bool> NavigateTo(string name, object? data = null) => NavigateTo(name, data, MainStack);

      /// <summary>
      /// Membuka <paramref name="targetNav"/> relatif ke <see cref="MainStack"/>. Kalau judul yang
      /// dihasilkan sudah dipakai sebuah entri, tampilan hanya dipindahkan ke entri itu - tanpa muat
      /// ulang dan tanpa mengganti datanya. Dari dalam body, pakai
      /// <see cref="NavigationEntry.NavigateTo(Navigation,object?)"/> milik entrinya sendiri.
      /// </summary>
      /// <param name="targetNav">Navigasi tujuan.</param>
      /// <param name="data">Parameter untuk layar tujuan, atau <c>null</c> kalau tidak ada.</param>
      /// <returns><c>false</c> kalau user tidak berhak membukanya, atau perpindahannya ditolak.</returns>
      public Task<bool> NavigateTo(Navigation targetNav, object? data = null) => NavigateTo(targetNav, data, MainStack);

      /// <summary>
      /// Membuka PDF di viewer bawaan aplikasi, relatif ke <see cref="MainStack"/>. Dari dalam body, pakai
      /// <see cref="NavigationEntry.ViewPdf"/> milik entrinya sendiri supaya viewer terbuka di window body itu.
      /// </summary>
      /// <param name="title">
      /// Judul viewer, sekaligus kunci unik entrinya: sertakan penanda unik dokumennya (mis. nomor dokumen).
      /// Membuka judul yang sudah terbuka hanya memindahkan tampilan ke viewer itu, tanpa memuat ulang.
      /// </param>
      /// <param name="loader">
      /// Pengambil isi PDF, biasanya satu action server. Dipanggil saat viewer dibuka dan setiap kali
      /// Refresh ditekan; stream-nya ditutup oleh viewer. Viewer tidak memeriksa hak apa pun, jadi hak
      /// atas dokumennya harus dijaga oleh action yang dipanggil di sini.
      /// </param>
      /// <param name="fileName">Nama file bawaan saat PDF disimpan, atau <c>null</c> untuk memakai judulnya.</param>
      /// <returns><c>false</c> kalau viewer tidak bisa dibuka, mis. perpindahannya ditolak layar yang sedang tampil.</returns>
      public Task<bool> ViewPdf(string title, Func<CancellationToken, Task<Stream>> loader, string? fileName = null) =>
         NavigateTo(PdfViewerNavigationName, new PdfViewerNavigationPayload(title, loader, fileName));

      internal Task<bool> NavigateTo(string name, object? data, NavigationStack origin) {
         var nav = _allNavigations.FirstOrDefault(r => r.Name == name);
         return nav != null ? NavigateTo(nav, data, origin) : Task.FromResult(false);
      }

      // The one place every navigation request ends up, whichever stack it was asked from. A title is
      // a key: if it is already shown anywhere, that entry is only brought back into view - its window
      // included, when it lives in another one - otherwise a new entry is opened on the stack the
      // request came from.
      internal async Task<bool> NavigateTo(Navigation targetNav, object? data, NavigationStack origin) {
         if (!CanOpen(targetNav)) return false;

         var title = ResolveTitle(targetNav, data);
         if (FindEntry(title) is { } existing) {
            // Brought up before the move, so a body asked to let go of its input is seen asking.
            if (existing.Stack != origin) BringToFront(existing.Stack);
            return await existing.Stack.MoveTo(existing);
         }

         // A manager lives only in the main window, or as the root of a detached one: asked for from
         // any other window - a detached one, or one a tab was torn off into - even by a body there,
         // it opens in the main window instead, which is then brought up. What those windows gain
         // from their own bodies is editors. On a tabbed stack, Open means a new tab on the right.
         if (targetNav.Kind == NavigationKind.Manager && origin != MainStack) {
            origin = MainStack;
            BringToFront(origin);
         }

         return await origin.Open(targetNav, data, title);
      }

      private static string ResolveTitle(Navigation navigation, object? data) =>
         (data as NavigationPayloadBase)?.Title is { Length: > 0 } title ? title : navigation.Title;

      /// <inheritdoc cref="NavigateToRoot(Navigation,object?)" />
      /// <param name="name">Nama navigasi tujuan.</param>
      /// <param name="data"><inheritdoc cref="NavigateToRoot(Navigation,object?)" path="/param[@name='data']" /></param>
      public Task<bool> NavigateToRoot(string name, object? data = null) {
         var nav = _allNavigations.FirstOrDefault(r => r.Name == name);
         return nav != null ? NavigateToRoot(nav, data) : Task.FromResult(false);
      }

      /// <summary>
      /// Membuka <paramref name="targetNav"/> di <see cref="MainStack"/> lalu menjadikannya satu-satunya
      /// isi stack itu, sehingga tidak ada jalan kembali ke apa pun yang tadi terbuka. Dipakai untuk
      /// perpindahan yang memulai ulang alur aplikasi - layar login saat aplikasi dibuka dan saat sesi
      /// berakhir.
      /// <para>
      /// Bedanya dengan <see cref="NavigationStack.NavigateHome"/>: home tidak ikut dipasang. Sebelum
      /// ada yang masuk, body home memang belum boleh dibangun sama sekali.
      /// </para>
      /// </summary>
      /// <param name="targetNav">Navigasi yang akan menjadi akar stack yang baru.</param>
      /// <param name="data">Parameter untuk navigasi tujuan, atau <c>null</c> kalau tidak ada.</param>
      /// <returns><c>false</c> kalau perpindahannya dibatalkan; stack dibiarkan apa adanya.</returns>
      public Task<bool> NavigateToRoot(Navigation targetNav, object? data = null) =>
         MainStack.NavigateToRoot(targetNav, data);

      #endregion
   }
}
