using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Test.Models
{
   /// <summary>A test item: simple master data to test CRUD, paging, UiModel, and data approval.</summary>
   [Table("ta_TestItem")]
   public class ta_TestItem
   {
      [Key]
      public string cTestItemId { get; set; } = string.Empty;

      public string cTestItemCode { get; set; } = string.Empty;

      public string cTestItemName { get; set; } = string.Empty;

      public int cTestItemQty { get; set; }

      public decimal cTestItemPrice { get; set; }

      public TestItemState cTestItemState { get; set; }

      public string? cTestItemNote { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }
   }
}
