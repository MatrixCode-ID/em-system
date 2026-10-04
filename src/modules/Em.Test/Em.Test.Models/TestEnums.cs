namespace Em.Test.Models
{
   /// <summary>Keadaan sebuah item uji.</summary>
   public enum TestItemState
   {
      /// <summary>Item aktif dan boleh dipakai.</summary>
      Active = 0,

      /// <summary>Item dinonaktifkan.</summary>
      Disabled = 1
   }

   /// <summary>Tahap hidup sebuah dokumen uji terhadap approval.</summary>
   public enum TestDocStatus
   {
      /// <summary>Draf: masih bisa diubah dan belum diajukan.</summary>
      Draft = 0,

      /// <summary>Sudah diajukan dan menunggu keputusan.</summary>
      InApproval = 1,

      /// <summary>Seluruh langkah approval selesai.</summary>
      Approved = 2,

      /// <summary>Ditolak oleh salah satu langkah.</summary>
      Rejected = 3
   }

   /// <summary>Jenis hasil sebuah business task uji.</summary>
   public enum TestTaskOutput
   {
      /// <summary>Tanpa hasil yang diambil.</summary>
      None = 0,

      /// <summary>Hasil berupa data JSON.</summary>
      Json = 1,

      /// <summary>Hasil berupa berkas teks yang diunduh.</summary>
      File = 2
   }

   /// <summary>Operasi yang diusulkan sebuah perubahan data item.</summary>
   public enum TestItemOperation
   {
      /// <summary>Item baru.</summary>
      Create = 1,

      /// <summary>Mengubah item yang ada.</summary>
      Update = 2,

      /// <summary>Menghapus item.</summary>
      Delete = 3
   }
}
