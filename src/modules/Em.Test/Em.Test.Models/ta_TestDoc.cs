using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Test.Models
{
   /// <summary>A test document: a transaction document submitted through document approval and stamped onto a PDF.</summary>
   [Table("ta_TestDoc")]
   public class ta_TestDoc
   {
      [Key]
      public string cTestDocId { get; set; } = string.Empty;

      public string cTestDocNo { get; set; } = string.Empty;

      public string cTestDocTitle { get; set; } = string.Empty;

      public decimal cTestDocAmount { get; set; }

      public TestDocStatus cTestDocStatus { get; set; }

      /// <summary>The result of the QA Check step check, or empty when not yet checked.</summary>
      public bool? cTestDocQaPassed { get; set; }

      public string? cTestDocQaRemarks { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }
   }
}
