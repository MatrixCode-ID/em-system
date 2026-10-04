using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("vi_User")]
   public class vi_User : ta_User
   {
      public string cContactFullName { get; set; } = string.Empty;

      public ContactState cContactState { get; set; }

      public ContactType cContactType { get; set; }

      public string? cContactNote { get; set; }

      public string? cAddressName { get; set; }

      public string? cAddressLocation { get; set; }

      public string? cAddressZip { get; set; }

      public CommunationType cCommType { get; set; }

      public ContactCommunicationState cCommState { get; set; }

      public string cCommValue { get; set; } = string.Empty;

      public string? cCommNote { get; set; } = string.Empty;

   }
}
