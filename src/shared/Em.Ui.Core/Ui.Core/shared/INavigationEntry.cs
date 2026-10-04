namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Satu tempat di sebuah stack navigasi: sebuah <see cref="INavigation"/> yang sedang terbuka,
   /// lengkap dengan body, data, dan judulnya sendiri. Setiap kali sebuah layar dibuka dengan judul
   /// yang belum ada, terciptalah satu entri baru - jadi "Edit User: ani" dan "Edit User: joni" adalah
   /// dua entri dari navigasi yang sama.
   /// <para>
   /// Body menerima entrinya sendiri lewat <see cref="NavigationEventArgs.Entry"/>, dan dari situ
   /// bisa mengganti judulnya (<see cref="SetTitle"/>), menutup dirinya (<see cref="Close"/>), atau
   /// membuka layar lain (<see cref="NavigateTo(string,object?)"/>).
   /// </para>
   /// </summary>
   public interface INavigationEntry
   {
      /// <summary>Definisi layar yang dibuka entri ini.</summary>
      INavigation Navigation { get; }

      /// <summary>
      /// Judul yang tampil, sekaligus kunci unik entri ini di seluruh aplikasi: satu judul hanya bisa
      /// tampil di satu tempat. Membuka layar dengan judul yang sudah ada tidak membuat entri baru,
      /// melainkan memindahkan tampilan ke entri yang sudah ada itu.
      /// </summary>
      string Title { get; }

      /// <summary>Parameter yang dipakai saat entri ini dibuka, atau <c>null</c> kalau tidak ada.</summary>
      object? Data { get; }

      /// <summary>Body milik entri ini. Setiap entri punya body-nya sendiri.</summary>
      INavigationBody Body { get; }

      /// <summary>Stack yang memegang entri ini.</summary>
      INavigationStack Stack { get; }

      /// <summary>
      /// Meminta body entri ini memuat ulang isinya dengan <see cref="Data"/> yang sedang dipegangnya.
      /// Tidak ada yang bisa menolak muat ulang.
      /// </summary>
      Task Reload();

      /// <summary>
      /// Mengganti judul entri ini, mis. dari "Create New User" menjadi "Edit User: ani" sesudah data
      /// barunya tersimpan. Judul baru tunduk pada aturan keunikan yang sama dengan judul awal.
      /// </summary>
      /// <param name="title">Judul pengganti.</param>
      /// <returns>
      /// <c>false</c> kalau judul itu sudah dipakai entri lain; judul lama dipertahankan.
      /// </returns>
      bool SetTitle(string title);

      /// <summary>
      /// Mengeluarkan entri ini dari stack-nya dan melepas body-nya. Body mendapat kesempatan menolak
      /// lewat <see cref="INavigationBody.OnNavigatingAway"/> lebih dulu - mis. karena masih ada
      /// perubahan yang belum disimpan - lalu <see cref="INavigationBody.OnRelease"/>. Kalau entri ini
      /// yang sedang tampil, tampilan pindah dulu ke entri sebelumnya.
      /// </summary>
      /// <returns><c>false</c> kalau penutupannya ditolak atau entri ini sudah tidak ada di stack.</returns>
      Task<bool> Close();

      /// <summary>
      /// Membuka navigasi bernama <paramref name="name"/> relatif terhadap stack entri ini. Inilah cara
      /// sebuah body membuka layar lain, supaya layar tujuannya tampil di tempat yang sama dengan
      /// body pemanggilnya.
      /// </summary>
      /// <param name="name">Nama navigasi tujuan.</param>
      /// <param name="data">Parameter untuk layar tujuan, atau <c>null</c> kalau tidak ada.</param>
      /// <returns>
      /// <c>false</c> kalau namanya tidak dikenal, user tidak berhak membukanya, atau perpindahannya
      /// ditolak.
      /// </returns>
      Task<bool> NavigateTo(string name, object? data = null);

      /// <inheritdoc cref="NavigateTo(string,object?)" />
      /// <param name="navigation">Navigasi tujuan.</param>
      /// <param name="data"><inheritdoc cref="NavigateTo(string,object?)" path="/param[@name='data']" /></param>
      Task<bool> NavigateTo(INavigation navigation, object? data = null);
   }
}
