using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_UserCredential")]
   public class ta_UserCredential  {
      [Key]
      public string cCredentialId { get; set; } = string.Empty;

      public string cUserId { get; set; } = string.Empty;

      public string cCredentialType { get; set; } = string.Empty;
      
      public CredentialState cCredentialState { get; set; }
      
      public string? cCredentialKey { get; set; }
      
      public string? cCredentialSecret { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }

   }
}
