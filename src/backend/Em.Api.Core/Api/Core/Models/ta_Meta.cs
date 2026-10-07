using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>Row of the metadata table: one key-value setting of the server.</summary>
   [Table("ta_Meta")]
   public class ta_Meta
   {
      /// <summary>Key of the setting.</summary>
      [Key]
      public string cMetaKey { get; set; } = string.Empty;

      /// <summary>Value of the setting, as text.</summary>
      public string cMetaValue { get; set; } = string.Empty;

      /// <summary>Description of the setting.</summary>
      public string cMetaDescription { get; set; } = string.Empty;

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }
   }
}