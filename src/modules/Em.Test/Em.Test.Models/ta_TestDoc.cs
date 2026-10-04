using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Test.Models
{
   /// <summary>Dokumen uji: dokumen transaksi yang diajukan lewat document approval dan di-stamp ke PDF.</summary>
   [Table("ta_TestDoc")]
   public class ta_TestDoc
   {
      [Key]
      public string cTestDocId { get; set; } = string.Empty;

      public string cTestDocNo { get; set; } = string.Empty;

      public string cTestDocTitle { get; set; } = string.Empty;

      public decimal cTestDocAmount { get; set; }

      public TestDocStatus cTestDocStatus { get; set; }

      /// <summary>Hasil pemeriksaan langkah QA Check, atau kosong kalau belum diperiksa.</summary>
      public bool? cTestDocQaPassed { get; set; }

      public string? cTestDocQaRemarks { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }
   }
}
