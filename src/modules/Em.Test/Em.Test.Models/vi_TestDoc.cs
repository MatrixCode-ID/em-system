using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Test.Models
{
   /// <summary>The read view of a test document; its columns are the same as its table.</summary>
   [Table("vi_TestDoc")]
   public class vi_TestDoc : ta_TestDoc
   {
   }
}
