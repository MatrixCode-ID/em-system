namespace Em.Api.Core.Models
{
   /// <summary>Ukuran payload registry berdasarkan metadata, bukan pemakaian volume disk.</summary>
   public class CtnStorageInfo
   {
      public long BlobBytes { get; set; }
      public long ManifestBytes { get; set; }
      public long TotalBytes => BlobBytes + ManifestBytes;
   }

   // Semua waktu di DTO registry adalah UTC; layar yang menampilkannya mengubah ke waktu lokal.

   /// <summary>Satu root namespace container registry (mis. <c>server</c>, <c>acme</c>).</summary>
   public class CtnRootInfo
   {
      public string Id { get; set; } = "";

      /// <summary>Nama root, bagian pertama nama pull (<c>host/root/nama</c>).</summary>
      public string Name { get; set; } = "";

      public string? Description { get; set; }

      /// <summary><c>false</c> = nonaktif: semua push dan pull di root ini ditolak.</summary>
      public bool IsActive { get; set; }

      public int FolderCount { get; set; }
      public int ImageCount { get; set; }
      public DateTime CreatedAt { get; set; }
   }

   /// <summary>Folder pengelompokan di dalam sebuah root. Tidak muncul di nama pull.</summary>
   public class CtnFolderInfo
   {
      public string Id { get; set; } = "";
      public string RootId { get; set; } = "";

      /// <summary>Folder induk; <c>null</c> kalau langsung di root.</summary>
      public string? ParentId { get; set; }

      public string Name { get; set; } = "";
   }

   /// <summary>Satu container bernama (<c>root/nama</c>), daun tree.</summary>
   public class CtnImageInfo
   {
      public string Id { get; set; } = "";
      public string RootId { get; set; } = "";
      public string RootName { get; set; } = "";

      /// <summary>Folder tempatnya; <c>null</c> kalau langsung di root.</summary>
      public string? FolderId { get; set; }

      public string Name { get; set; } = "";

      /// <summary>Nama pull tanpa host: <c>root/nama</c>.</summary>
      public string FullName { get; set; } = "";

      public string? Description { get; set; }
      public bool IsActive { get; set; }
      public int TagCount { get; set; }
      public int ManifestCount { get; set; }
      public DateTime CreatedAt { get; set; }
   }

   /// <summary>Seluruh isi tree sebuah root: folder dan image dalam daftar datar.</summary>
   public class CtnTree
   {
      public CtnFolderInfo[] Folders { get; set; } = [];
      public CtnImageInfo[] Images { get; set; } = [];
   }

   /// <summary>Satu manifest sebuah image berikut tag yang menunjuknya.</summary>
   public class CtnManifestInfo
   {
      public string Id { get; set; } = "";
      public string Digest { get; set; } = "";
      public string MediaType { get; set; } = "";

      /// <summary>Ukuran manifest itu sendiri, bukan ukuran layer-nya.</summary>
      public long Size { get; set; }

      public DateTime PushedAt { get; set; }

      /// <summary>Nama robot yang mengirimnya; <c>null</c> kalau robotnya sudah dihapus.</summary>
      public string? PushedBy { get; set; }

      public string[] Tags { get; set; } = [];

      /// <summary>Jumlah blob (config dan layer) yang dirujuk manifest ini.</summary>
      public int BlobCount { get; set; }

      /// <summary>
      /// Jumlah blob yang tercatat di database tetapi berkasnya tidak ada di storage server (atau ukurannya
      /// berbeda). Image dengan nilai di atas nol tidak bisa di-pull sampai berkasnya dipulihkan.
      /// </summary>
      public int MissingBlobCount { get; set; }
   }

   /// <summary>
   /// Hasil review (dry run) atau eksekusi garbage collection registry. Semua ukuran dalam byte;
   /// <see cref="Blobs"/> dibatasi <see cref="MaxListedBlobs"/> baris, jumlah sebenarnya di <see cref="BlobCount"/>.
   /// </summary>
   public class CtnGcReport
   {
      public const int MaxListedBlobs = 1000;

      /// <summary><c>true</c> = review saja, tidak ada yang dihapus.</summary>
      public bool DryRun { get; set; }

      public int GraceHours { get; set; }

      /// <summary>Batas waktu UTC: hanya yang lebih tua dari ini yang dihapus.</summary>
      public DateTime CutoffUtc { get; set; }

      public CtnGcBlob[] Blobs { get; set; } = [];
      public int BlobCount { get; set; }
      public long BlobBytes { get; set; }

      public int StaleUploadCount { get; set; }
      public long StaleUploadBytes { get; set; }

      /// <summary>Berkas di folder blob tanpa baris metadata.</summary>
      public int OrphanBlobFileCount { get; set; }
      public long OrphanBlobFileBytes { get; set; }

      /// <summary>Berkas di folder upload tanpa baris metadata.</summary>
      public int OrphanUploadFileCount { get; set; }
      public long OrphanUploadFileBytes { get; set; }

      public long TotalBytes => BlobBytes + StaleUploadBytes + OrphanBlobFileBytes + OrphanUploadFileBytes;

      /// <summary>Hal yang dilewati atau gagal dihapus, dalam kalimat pendek berbahasa Inggris.</summary>
      public string[] Warnings { get; set; } = [];
   }

   /// <summary>Satu blob yatim dalam <see cref="CtnGcReport"/>.</summary>
   public class CtnGcBlob
   {
      public string Digest { get; set; } = "";
      public long Size { get; set; }
      public DateTime CreatedAt { get; set; }

      /// <summary>Container (<c>root/nama</c>) yang masih menautkannya lewat tautan lama; kosong bila tidak ada.</summary>
      public string[] LinkedImages { get; set; } = [];
   }
}
