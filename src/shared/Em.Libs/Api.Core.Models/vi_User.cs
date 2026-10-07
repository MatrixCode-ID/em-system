using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of view <c>vi_User</c>.</summary>
   [Table("vi_User")]
   public class vi_User : ta_User
   {
      /// <summary>Contact full name.</summary>
      public string cContactFullName { get; set; } = string.Empty;

      /// <summary>Contact state.</summary>
      public ContactState cContactState { get; set; }

      /// <summary>Contact type.</summary>
      public ContactType cContactType { get; set; }

      /// <summary>Contact note.</summary>
      public string? cContactNote { get; set; }

      /// <summary>Address name.</summary>
      public string? cAddressName { get; set; }

      /// <summary>Address location.</summary>
      public string? cAddressLocation { get; set; }

      /// <summary>Address zip.</summary>
      public string? cAddressZip { get; set; }

      /// <summary>Comm type.</summary>
      public CommunationType cCommType { get; set; }

      /// <summary>Comm state.</summary>
      public ContactCommunicationState cCommState { get; set; }

      /// <summary>Comm value.</summary>
      public string cCommValue { get; set; } = string.Empty;

      /// <summary>Comm note.</summary>
      public string? cCommNote { get; set; } = string.Empty;

   }
}
