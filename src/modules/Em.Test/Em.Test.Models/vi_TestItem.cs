using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Test.Models
{
   /// <summary>The read view of a test item; its columns are the same as its table.</summary>
   [Table("vi_TestItem")]
   public class vi_TestItem : ta_TestItem
   {
   }
}
