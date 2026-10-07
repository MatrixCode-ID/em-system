using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_UserCredential</c>.</summary>
   [Table("ta_UserCredential")]
   public class ta_UserCredential  {
      /// <summary>Key: credential id.</summary>
      [Key]
      public string cCredentialId { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_User</c> row.</summary>
      public string cUserId { get; set; } = string.Empty;

      /// <summary>Credential type.</summary>
      public string cCredentialType { get; set; } = string.Empty;
      
      /// <summary>Credential state.</summary>
      public CredentialState cCredentialState { get; set; }
      
      /// <summary>Credential key.</summary>
      public string? cCredentialKey { get; set; }
      
      /// <summary>Credential secret.</summary>
      public string? cCredentialSecret { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }

   }
}
