using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_Comm")]
   public class ta_Comm  {
      [Key]
      public string cCommId { get; set; } = string.Empty;

      public string cContactId { get; set; } = string.Empty;

      public CommunationType cCommType { get; set; }

      public ContactCommunicationState cCommState { get; set; }

      public string cCommValue { get; set; } = string.Empty;

      public string? cCommNote { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }

   }
}