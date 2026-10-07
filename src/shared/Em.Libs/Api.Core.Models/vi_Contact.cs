using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of view <c>vi_Contact</c>.</summary>
   [Table("vi_Contact")]
   public class vi_Contact : ta_Contact
   {
      /// <summary>Address name.</summary>
      public string? cAddressName { get; set; }

      /// <summary>Address location.</summary>
      public string? cAddressLocation { get; set; }

      /// <summary>Address zip.</summary>
      public string? cAddressZip { get; set; }

      /// <summary>Comm type.</summary>
      public int? cCommType { get; set; }

      /// <summary>Comm state.</summary>
      public ContactCommunicationState? cCommState { get; set; }

      /// <summary>Comm value.</summary>
      public string? cCommValue { get; set; }

      /// <summary>Comm note.</summary>
      public string? cCommNote { get; set; }

   }
}
