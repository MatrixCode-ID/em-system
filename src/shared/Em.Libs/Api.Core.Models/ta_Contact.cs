using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_Contact")]
   public class ta_Contact  {
      [Key]
      public string cContactId { get; set; } = string.Empty;

      public string cContactFullName { get; set; } = string.Empty;

      public ContactState cContactState { get; set; }

      public ContactType cContactType { get; set; }

      public string? cContactNote { get; set; }

      public string? cContactDefaultAddress_cAddressId { get; set; }

      public string? cContactDefaultComm_cCommId { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }

   }
}