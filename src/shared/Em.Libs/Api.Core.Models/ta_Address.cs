using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_Address</c>.</summary>
   [Table("ta_Address")]
   public class ta_Address  {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cAddressId { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_Contact</c> row.</summary>
      public string cContactId { get; set; } = string.Empty;

      /// <summary>Name.</summary>
      public string cAddressName { get; set; } = string.Empty;

      /// <summary>Record state.</summary>
      public AddressState cAddressState { get; set; }

      /// <summary>Location.</summary>
      public string cAddressLocation { get; set; } = string.Empty;

      /// <summary>Zip.</summary>
      public string cAddressZip { get; set; } = string.Empty;

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }

   }
}