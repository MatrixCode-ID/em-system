using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("vi_Contact")]
   public class vi_Contact : ta_Contact
   {
      public string? cAddressName { get; set; }

      public string? cAddressLocation { get; set; }

      public string? cAddressZip { get; set; }

      public int? cCommType { get; set; }

      public ContactCommunicationState? cCommState { get; set; }

      public string? cCommValue { get; set; }

      public string? cCommNote { get; set; }

   }
}
