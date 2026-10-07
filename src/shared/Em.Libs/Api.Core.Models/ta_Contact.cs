using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_Contact</c>.</summary>
   [Table("ta_Contact")]
   public class ta_Contact  {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cContactId { get; set; } = string.Empty;

      /// <summary>Full name.</summary>
      public string cContactFullName { get; set; } = string.Empty;

      /// <summary>Record state.</summary>
      public ContactState cContactState { get; set; }

      /// <summary>Type.</summary>
      public ContactType cContactType { get; set; }

      /// <summary>Note.</summary>
      public string? cContactNote { get; set; }

      /// <summary>Default address, referencing <c>cAddressId</c>.</summary>
      public string? cContactDefaultAddress_cAddressId { get; set; }

      /// <summary>Default comm, referencing <c>cCommId</c>.</summary>
      public string? cContactDefaultComm_cCommId { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }

   }
}