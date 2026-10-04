namespace Em.Api.Core.Storage
{
   /// <summary>
   /// Penyimpanan isi berkas milik aplikasi: berkas disimpan di bawah sebuah kunci dan dibaca kembali
   /// lewat kunci itu. Tidak ada alamat publik ke isinya - yang mengambilnya adalah action pemakainya,
   /// yang lebih dulu memeriksa hak pemanggil.
   /// </summary>
   /// <remarks>
   /// Isi yang sudah tersimpan <b>tidak pernah ditimpa</b>: kunci yang sudah terpakai ditolak. Itu
   /// disengaja, karena pemakai pertamanya adalah berkas yang dibekukan - sesuatu yang ditandatangani
   /// orang harus tetap persis seperti saat ia melihatnya. Versi baru memakai kunci baru.
   /// <para>
   /// Kunci berbentuk seperti path (<c>bagian/bagian/nama.pdf</c>) supaya implementasi lain -
   /// penyimpanan objek di jaringan, misalnya - bisa memakai kunci yang sama tanpa mengubah
   /// pemakainya. Lihat <see cref="BinaryStorageKey"/> untuk aturan bentuknya.
   /// </para>
   /// </remarks>
   public interface IBinaryStorage
   {
      /// <summary>
      /// Menyimpan isi <paramref name="content"/> di bawah <paramref name="key"/>.
      /// </summary>
      /// <param name="key">Kunci tempat isinya disimpan. Lihat <see cref="BinaryStorageKey"/>.</param>
      /// <param name="content">Isi yang disimpan, dibaca dari posisinya saat ini sampai habis.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <returns>Kunci yang benar-benar dipakai, sudah dalam bentuk bakunya.</returns>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau <paramref name="key"/> tidak memenuhi aturan bentuk kunci.
      /// </exception>
      /// <exception cref="Em.Shared.ActionException">
      /// Dilempar kalau kunci itu sudah terpakai - penyimpanan ini tidak menimpa isi yang sudah ada.
      /// </exception>
      Task<string> PutAsync(string key, Stream content, CancellationToken cancellationToken = default);

      /// <summary>
      /// Membuka isi yang tersimpan di bawah sebuah kunci untuk dibaca. Pemanggil yang menutup
      /// stream-nya.
      /// </summary>
      /// <param name="key">Kunci isinya.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      /// <exception cref="Em.Shared.ActionException">
      /// Dilempar kalau tidak ada isi di bawah kunci itu.
      /// </exception>
      Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

      /// <summary>
      /// Membuang isi yang tersimpan di bawah sebuah kunci. Kunci yang memang tidak ada bukan
      /// kesalahan - dipanggil juga saat membereskan sisa pengajuan yang gagal, dan di situ yang
      /// penting hanya bahwa sesudahnya isinya tidak ada.
      /// </summary>
      /// <param name="key">Kunci isinya.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      Task DeleteAsync(string key, CancellationToken cancellationToken = default);

      /// <summary>Apakah ada isi tersimpan di bawah sebuah kunci.</summary>
      /// <param name="key">Kunci yang diperiksa.</param>
      /// <param name="cancellationToken">Token pembatalan.</param>
      Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
   }
}
