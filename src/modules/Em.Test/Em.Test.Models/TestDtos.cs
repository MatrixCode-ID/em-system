namespace Em.Test.Models
{
   /// <summary>Isi uji echo dengan berbagai tipe parameter, supaya binding query dan body bisa dibandingkan.</summary>
   public class TestEchoRequest
   {
      public string Text { get; set; } = string.Empty;

      public int Number { get; set; }

      public decimal Amount { get; set; }

      public bool Flag { get; set; }

      public DateTime When { get; set; }

      public TestItemState State { get; set; }

      public Guid Token { get; set; }

      public string[] Tags { get; set; } = [];
   }

   /// <summary>Jawaban uji echo: isi yang diterima server beserta keterangan siapa pemanggilnya.</summary>
   public class TestEchoResult
   {
      public TestEchoRequest Received { get; set; } = new();

      public string Via { get; set; } = string.Empty;

      public string? CallerAccount { get; set; }

      public DateTime ServerTimeUtc { get; set; }
   }

   /// <summary>Keadaan pemanggil menurut server, dipakai membandingkan dengan keadaan menurut client.</summary>
   public class TestSessionInfo
   {
      public string? UserId { get; set; }

      public string? Account { get; set; }

      public bool IsAdmin { get; set; }

      public bool IsDebugRequest { get; set; }

      public string Source { get; set; } = string.Empty;

      public string[] Claims { get; set; } = [];
   }

   /// <summary>Payload header untuk unggahan stream uji.</summary>
   public class TestStreamRequest
   {
      public string Name { get; set; } = string.Empty;
   }

   /// <summary>Hasil unggahan stream uji: apa yang benar-benar sampai ke server.</summary>
   public class TestStreamResult
   {
      public string Name { get; set; } = string.Empty;

      public long Length { get; set; }

      public string Sha256 { get; set; } = string.Empty;
   }

   /// <summary>Permintaan menjalankan business task uji.</summary>
   public class TestTaskRequest
   {
      /// <summary>Lama pekerjaan dalam detik; progresnya dilaporkan tiap detik.</summary>
      public int Seconds { get; set; } = 10;

      /// <summary>Kalau true pekerjaan gagal di tengah jalan, untuk menguji status gagal.</summary>
      public bool Fail { get; set; }

      /// <summary>true = task global milik layar module; false = task personal milik pemulainya.</summary>
      public bool Global { get; set; }

      public TestTaskOutput Output { get; set; }
   }

   /// <summary>Syarat pencarian item beserta halamannya.</summary>
   public class TestItemQuery
   {
      public int Page { get; set; } = 1;

      public int PageSize { get; set; } = 10;

      public string? Search { get; set; }

      public TestItemState? State { get; set; }
   }

   /// <summary>Satu halaman hasil pencarian item.</summary>
   public class TestItemPage
   {
      public vi_TestItem[] Items { get; set; } = [];

      public int Total { get; set; }

      public int Page { get; set; }

      public int PageSize { get; set; }
   }

   /// <summary>Usulan perubahan data item yang diajukan lewat data approval.</summary>
   public class TestItemChange
   {
      public TestItemOperation Operation { get; set; }

      /// <summary>Id item yang diubah atau dihapus; kosong untuk item baru.</summary>
      public string? ItemId { get; set; }

      public string Code { get; set; } = string.Empty;

      public string Name { get; set; } = string.Empty;

      public int Qty { get; set; }

      public decimal Price { get; set; }

      public string? Note { get; set; }
   }

   /// <summary>Hasil mengajukan usulan perubahan data.</summary>
   public class TestSubmitResult
   {
      public string ApprovalRequestId { get; set; } = string.Empty;

      /// <summary>true kalau pemanggil memegang claim persetujuan sehingga perubahan langsung diterapkan.</summary>
      public bool AppliedImmediately { get; set; }
   }

   /// <summary>Isian langkah QA Check pada dokumen uji.</summary>
   public class TestQaPayload
   {
      public bool Passed { get; set; }

      public string? Remarks { get; set; }
   }
}
