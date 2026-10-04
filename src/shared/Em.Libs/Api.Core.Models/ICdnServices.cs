using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Pengelolaan isi CDN: menelusuri folder, mengunggah file, membuat folder, menghapus, serta membuat
   /// dan membongkar file zip. Semua action di sini mensyaratkan claim <see cref="CdnClaim"/> di module
   /// <see cref="Defaults.AdministrativeToolsModuleName"/>, dan menjawab 404 kalau CDN tidak dinyalakan
   /// di server. Mengunduh isinya tidak lewat sini, melainkan lewat alamat publik <c>/cdn/...</c>.
   /// </summary>
   /// <remarks>
   /// Setiap parameter <c>path</c> adalah path relatif terhadap folder akar CDN, dipisah <c>/</c>;
   /// <c>null</c> atau string kosong berarti folder akar. Path yang mencoba keluar dari folder akar,
   /// atau yang menyebut nama berawalan titik, ditolak 400.
   /// <para>
   /// Membuat dan membongkar zip bisa lama, jadi keduanya berjalan sebagai business task global di
   /// server: action-nya langsung kembali, dan statusnya dipantau lewat action status di bawah. Setiap
   /// pemegang claim CDN - administrator maupun bukan - boleh melihat, membatalkan, dan membersihkan
   /// task itu, termasuk yang dimulai user lain. Hanya satu archive yang boleh berjalan di seluruh
   /// server, dan satu file zip tidak bisa dibongkar dua kali bersamaan.
   /// </para>
   /// </remarks>
   public interface ICdnServices : IServices
   {
      /// <summary>
      /// Nama claim yang membuka pengelolaan CDN, ditulis tanpa nama module-nya.
      /// </summary>
      const string CdnClaim = "CDN Manager Access";

      const string SettingsClaim = "CDN Settings Manage";
      Task<StorageFeatureStatus> GetMeta_CdnStatus();
      Task<StorageSettingsDetail> GetMeta_CdnSettings();
      Task<StorageDirectoryValidation> PostGetMeta_CdnValidateDirectory(StorageFeatureSettings settings);
      Task<StorageSettingsDetail> PostGetMeta_CdnSettingsSave(StorageSettingsSave request);

      #region Meta's

      /// <summary>Total ukuran file publik di seluruh CDN. Mengabaikan file internal/sementara,
      /// hidden/system dan symlink. Tidak mencakup overhead filesystem atau free space volume.</summary>
      Task<CdnStorageInfo> GetMeta_CdnStorageSize();

      /// <summary>Isi satu folder berikut batas unggahan dan alamat publiknya.</summary>
      Task<CdnFolderContent> GetMeta_CdnFolder(string? path);

      /// <summary>Jumlah file dan subfolder di dalam sebuah folder, dihitung sampai yang terdalam.</summary>
      Task<CdnItemCount> GetMeta_CdnItemCount(string path);

      /// <summary>
      /// Seluruh isi sebuah folder sampai subfolder terdalam, dalam satu daftar datar: setiap folder
      /// tampil sebelum isinya. Dipakai untuk menyalin satu folder utuh ke luar CDN.
      /// </summary>
      Task<CdnEntry[]> GetMeta_CdnTree(string path);

      /// <summary>
      /// Mengunggah satu file ke sebuah folder. Isi file dikirim sebagai stream, jadi ukurannya tidak
      /// dibatasi memori maupun batas waktu request - yang membatasi hanya batas ukuran unggahan CDN.
      /// Ditolak 409 kalau nama itu sudah ada dan <see cref="CdnUploadRequest.Overwrite"/> <c>false</c>,
      /// 400 kalau nama atau foldernya tidak sah, dan 413 kalau melebihi batas ukuran unggahan. Yang
      /// ditolak karena nama atau konflik dijawab sebelum isi filenya dibaca; yang terputus di tengah
      /// jalan tidak meninggalkan apa-apa di CDN.
      /// </summary>
      /// <param name="request">Folder tujuan, nama file, dan pilihan timpa.</param>
      /// <param name="content">Isi file, dibaca dari awal sampai habis.</param>
      Task<CdnEntry> PostGetMeta_CdnUpload(CdnUploadRequest request, Stream content);

      /// <summary>
      /// Memindahkan sebuah file atau folder ke folder lain dengan nama yang sama. Ditolak 409 kalau
      /// nama itu sudah dipakai di folder tujuan - untuk file, kecuali <paramref name="overwrite"/>
      /// <c>true</c>; folder tidak pernah ditimpa. Folder tidak bisa dipindah ke dalam dirinya sendiri
      /// (400). Mengembalikan isi CDN di tempatnya yang baru.
      /// </summary>
      Task<CdnEntry> PostGetMeta_CdnMove(string path, string? targetFolder, bool overwrite);

      /// <summary>Membuat subfolder baru; ditolak 409 kalau nama itu sudah ada.</summary>
      Task<CdnEntry> PostGetMeta_CdnCreateFolder(string? path, string folderName);

      /// <summary>
      /// Menghapus sebuah file, atau sebuah folder beserta seluruh isinya. Folder akar tidak bisa
      /// dihapus (400); yang tidak ada dijawab 404.
      /// </summary>
      Task PostMeta_CdnDelete(string path);

      /// <summary>
      /// Mulai membuat file zip dari item yang dipilih. Nama, sumber, dan konflik diperiksa sebelum task
      /// dimulai: 400 untuk nama yang tidak sah, 404 untuk sumber yang tidak ada, dan 409 kalau nama zip
      /// sudah ada tanpa <see cref="CdnArchiveRequest.Overwrite"/>, atau kalau archive lain masih berjalan
      /// (pesannya menyebut siapa yang memulainya). Folder berawalan titik di dalam sumber tidak ikut.
      /// </summary>
      /// <returns>Potret task yang baru dimulai.</returns>
      Task<BusinessTaskInfo> PostGetMeta_CdnArchive(CdnArchiveRequest request);

      /// <summary>
      /// Task archive yang masih hidup, atau yang gagal dan belum di-clear; <c>null</c> kalau tidak ada.
      /// </summary>
      Task<BusinessTaskInfo?> GetMeta_CdnArchiveTask();

      /// <summary>Membatalkan archive yang sedang berjalan. 404 kalau tidak ada, 409 kalau sudah selesai.</summary>
      Task PostMeta_CdnArchiveCancel();

      /// <summary>Membersihkan archive yang sudah selesai (biasanya yang gagal). 409 kalau masih berjalan.</summary>
      Task PostMeta_CdnArchiveClear();

      /// <summary>
      /// Memeriksa sebuah file zip sebelum dibongkar di folder tempatnya berada, lalu mengembalikan path
      /// file yang akan tertimpa. Zip yang isinya mencoba keluar dari folder, memakai nama berawalan titik
      /// atau nama yang tidak sah, terlalu banyak isinya, atau isinya bentrok dengan folder yang sudah ada,
      /// ditolak 400.
      /// </summary>
      /// <param name="path">Path file zip.</param>
      Task<string[]> GetMeta_CdnExtractConflicts(string path);

      /// <summary>
      /// Mulai membongkar file zip di folder tempatnya berada. Pemeriksaannya sama dengan
      /// <see cref="GetMeta_CdnExtractConflicts"/>; ditambah 409 kalau ada file yang akan tertimpa tapi
      /// <paramref name="overwrite"/> <c>false</c>, atau kalau zip yang sama sedang dibongkar. Setiap isi
      /// dibatasi batas ukuran unggahan CDN, dan total isinya sepuluh kali batas itu. Yang dibatalkan atau
      /// gagal tidak meninggalkan apa-apa di CDN.
      /// </summary>
      /// <returns>Potret task yang baru dimulai.</returns>
      Task<BusinessTaskInfo> PostGetMeta_CdnExtract(string path, bool overwrite);

      /// <summary>
      /// Task bongkar zip untuk file zip yang berada langsung di <paramref name="folder"/>, yang masih
      /// hidup atau yang gagal dan belum di-clear. <c>null</c> atau string kosong berarti folder akar.
      /// </summary>
      Task<BusinessTaskInfo[]> GetMeta_CdnExtractTasks(string? folder);

      /// <summary>Membatalkan pembongkaran file zip di <paramref name="path"/>.</summary>
      Task PostMeta_CdnExtractCancel(string path);

      /// <summary>Membersihkan task bongkar zip di <paramref name="path"/> yang sudah selesai.</summary>
      Task PostMeta_CdnExtractClear(string path);

      #endregion
   }
}
