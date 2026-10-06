using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Pengelolaan container registry: root, folder, dan container. Semua
   /// action di sini mensyaratkan claim <see cref="CtnClaim"/> di module
   /// <see cref="Defaults.AdministrativeToolsModuleName"/>, dan menjawab 404 kalau registry tidak
   /// dinyalakan di server. Push dan pull image tidak lewat sini, melainkan lewat jalur <c>/v2</c>.
   /// </summary>
   /// <remarks>
   /// Push tidak pernah membuat root, folder, atau nama image baru: semuanya dibuat dulu lewat layanan
   /// ini. Nama pull selalu dua segmen (<c>host/root/nama</c>); folder murni pengelompokan dan tidak
   /// muncul di nama pull.
   /// <para>
   /// Identitas robot dan pemberian hak dikelola melalui <see cref="IRobotServices"/> dengan claim
   /// User Manager Access.
   /// </para>
   /// </remarks>
   public interface ICtnServices : IServices
   {
      /// <summary>Nama claim yang membuka pengelolaan registry, ditulis tanpa nama module-nya.</summary>
      const string CtnClaim = "Container Manager Access";

      /// <summary>Ukuran blob unik tersimpan dan manifest seluruh registry, termasuk blob yatim.
      /// Tidak mencakup upload sementara atau overhead database/filesystem; berdasarkan metadata registry.</summary>
      Task<CtnStorageInfo> GetMeta_CtnStorageSize();

      const string SettingsClaim = "Container Registry Settings Manage";
      Task<StorageFeatureStatus> GetMeta_CtnStatus();
      Task<StorageSettingsDetail> GetMeta_CtnSettings();
      Task<StorageDirectoryValidation> PostGetMeta_CtnValidateDirectory(StorageFeatureSettings settings);
      Task<StorageSettingsDetail> PostGetMeta_CtnSettingsSave(StorageSettingsSave request);

      #region Root

      /// <summary>Seluruh root berikut jumlah folder dan image-nya.</summary>
      Task<CtnRootInfo[]> GetMeta_CtnRoots();

      /// <summary>Membuat root. 400 untuk nama tidak sah, 409 kalau nama sudah dipakai.</summary>
      Task<CtnRootInfo> PostGetMeta_CtnRootCreate(string name, string? description);

      /// <summary>Mengubah deskripsi dan status aktif sebuah root.</summary>
      Task PostMeta_CtnRootUpdate(string rootId, string? description, bool isActive);

      /// <summary>Menghapus root. 409 kalau masih punya folder, image, atau hak robot.</summary>
      Task PostMeta_CtnRootDelete(string rootId);

      #endregion

      #region Folder dan container

      /// <summary>Seluruh folder dan image sebuah root.</summary>
      Task<CtnTree> GetMeta_CtnTree(string rootId);

      /// <summary>Membuat folder di root (<paramref name="parentFolderId"/> kosong) atau di dalam folder lain.</summary>
      Task<CtnFolderInfo> PostGetMeta_CtnFolderCreate(string rootId, string? parentFolderId, string name);

      /// <summary>Mengganti nama folder; 409 kalau saudaranya sudah memakai nama itu.</summary>
      Task<CtnFolderInfo> PostGetMeta_CtnFolderRename(string folderId, string name);

      /// <summary>
      /// Memindahkan folder berikut isinya ke folder lain di root yang sama (atau ke root-nya bila
      /// <paramref name="targetParentFolderId"/> kosong). Nama pull tidak berubah.
      /// </summary>
      Task<CtnFolderInfo> PostGetMeta_CtnFolderMove(string folderId, string? targetParentFolderId);

      /// <summary>Menghapus folder; 409 kalau masih berisi folder atau image.</summary>
      Task PostMeta_CtnFolderDelete(string folderId);

      /// <summary>
      /// Membuat container bernama di root dan folder tujuan. Setelah ini baru bisa di-push. 400 untuk nama
      /// yang tidak sah atau terlalu panjang, 409 kalau nama sudah dipakai di root itu.
      /// </summary>
      Task<CtnImageInfo> PostGetMeta_CtnImageCreate(string rootId, string? folderId, string name, string? description);

      /// <summary>Memindahkan container ke folder lain di root yang sama. Tidak menyalin blob dan tidak mengubah nama pull.</summary>
      Task<CtnImageInfo> PostGetMeta_CtnImageMove(string imageId, string? targetFolderId);

      /// <summary>Mengubah deskripsi dan status aktif sebuah container.</summary>
      Task PostMeta_CtnImageUpdate(string imageId, string? description, bool isActive);

      /// <summary>
      /// Menghapus container beserta manifest, tag, dan tautan blob-nya. Berkas blob di disk dibersihkan
      /// lewat <see cref="PostGetMeta_CtnGcRun"/>.
      /// </summary>
      Task PostMeta_CtnImageDelete(string imageId);

      /// <summary>Manifest sebuah container berikut tag-nya, yang terbaru lebih dulu.</summary>
      Task<CtnManifestInfo[]> GetMeta_CtnImageManifests(string imageId);

      /// <summary>
      /// Menghapus satu tag. Manifest-nya tetap ada dan masih bisa di-pull lewat digest. 404 bila tag tidak ada.
      /// </summary>
      Task PostMeta_CtnTagDelete(string imageId, string tag);

      /// <summary>
      /// Menghapus satu manifest beserta tag yang menunjuknya. Hanya metadata; blob-nya menjadi kandidat
      /// garbage collection. 404 bila tidak ada, 409 bila masih dirujuk manifest list/index di container yang sama.
      /// </summary>
      Task PostMeta_CtnManifestDelete(string imageId, string manifestId);

      #endregion

      #region Garbage collection

      /// <summary>Masa tenggang bawaan (jam) untuk garbage collection.</summary>
      const int GcDefaultGraceHours = 24;

      /// <summary>Masa tenggang terbesar yang diterima (jam).</summary>
      const int GcMaxGraceHours = 720;

      /// <summary>
      /// Dry run: apa yang akan dihapus garbage collection dengan masa tenggang <paramref name="graceHours"/>
      /// (1..<see cref="GcMaxGraceHours"/>, selain itu 400). Tidak mengubah apa pun.
      /// </summary>
      Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours);

      /// <summary>
      /// Menjalankan garbage collection: blob yatim, upload basi, dan berkas tanpa metadata yang lebih tua dari
      /// masa tenggang. Laporan berisi yang benar-benar dihapus. 409 bila GC lain sedang berjalan.
      /// </summary>
      Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours);

      #endregion


   }
}
