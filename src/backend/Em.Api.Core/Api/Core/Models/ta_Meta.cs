using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   [Table("ta_Meta")]
   public class ta_Meta
   {
      [Key]
      public string cMetaKey { get; set; } = string.Empty;

      public string cMetaValue { get; set; } = string.Empty;

      public string cMetaDescription { get; set; } = string.Empty;

      public DateTime ustamp { get; set; }
   }
}