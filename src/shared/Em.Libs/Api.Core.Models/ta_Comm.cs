using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_Comm</c>.</summary>
   [Table("ta_Comm")]
   public class ta_Comm  {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cCommId { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_Contact</c> row.</summary>
      public string cContactId { get; set; } = string.Empty;

      /// <summary>Type.</summary>
      public CommunationType cCommType { get; set; }

      /// <summary>Record state.</summary>
      public ContactCommunicationState cCommState { get; set; }

      /// <summary>Value.</summary>
      public string cCommValue { get; set; } = string.Empty;

      /// <summary>Note.</summary>
      public string? cCommNote { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }

   }
}