using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_Address")]
   public class ta_Address  {
      [Key]
      public string cAddressId { get; set; } = string.Empty;

      public string cContactId { get; set; } = string.Empty;

      public string cAddressName { get; set; } = string.Empty;

      public AddressState cAddressState { get; set; }

      public string cAddressLocation { get; set; } = string.Empty;

      public string cAddressZip { get; set; } = string.Empty;

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }

   }
}