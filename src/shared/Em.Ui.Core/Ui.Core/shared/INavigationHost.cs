namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Router navigasi tingkat aplikasi: mengenal seluruh navigasi yang terdaftar, memegang stack
   /// utama, dan menegakkan aturan "satu judul hanya tampil di satu tempat".
   /// <para>
   /// <see cref="NavigateTo(string,object?)"/> di sini membuka layar relatif ke
   /// <see cref="MainStack"/> - dipakai dari luar body, mis. menu home atau layar login. Dari dalam
   /// body, pakai <see cref="INavigationEntry.NavigateTo(string,object?)"/> milik entrinya sendiri.
   /// </para>
   /// </summary>
   public interface INavigationHost
   {
      /// <summary>Dipicu setiap kali sebuah perpindahan navigasi berhasil.</summary>
      event EventHandler<NavigationEventArgs>? Navigated;

      /// <summary>Seluruh navigasi yang dikenal aplikasi.</summary>
      IEnumerable<INavigation> Navigations { get; }

      /// <summary>Stack utama aplikasi, yang ditampilkan window atau halaman utamanya.</summary>
      INavigationStack MainStack { get; }

      /// <summary>Mencari entri berjudul <paramref name="title"/> di semua stack, home termasuk.</summary>
      /// <param name="title">Judul entri yang dicari.</param>
      /// <returns>Entrinya, atau <c>null</c> kalau judul itu belum dipakai di mana pun.</returns>
      INavigationEntry? FindEntry(string title);

      /// <summary>
      /// Membuka <paramref name="navigation"/> relatif ke <see cref="MainStack"/>. Kalau judul yang
      /// dihasilkan sudah dipakai sebuah entri, tampilan hanya dipindahkan ke entri itu - tanpa muat
      /// ulang dan tanpa mengganti datanya.
      /// </summary>
      /// <param name="navigation">Navigasi tujuan.</param>
      /// <param name="data">Parameter untuk layar tujuan, atau <c>null</c> kalau tidak ada.</param>
      /// <returns><c>false</c> kalau user tidak berhak membukanya, atau perpindahannya ditolak.</returns>
      Task<bool> NavigateTo(INavigation navigation, object? data = null);

      /// <inheritdoc cref="NavigateTo(INavigation,object?)" />
      /// <param name="navigationName">Nama navigasi tujuan.</param>
      /// <param name="data"><inheritdoc cref="NavigateTo(INavigation,object?)" path="/param[@name='data']" /></param>
      Task<bool> NavigateTo(string navigationName, object? data = null);
   }
}
