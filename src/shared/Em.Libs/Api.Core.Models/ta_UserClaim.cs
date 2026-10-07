using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   // Ini adalah temporary claim per user
   /// <summary>Row of table <c>ta_UserClaim</c>.</summary>
   [Table("ta_UserClaim")]
   public class ta_UserClaim
   {
      /// <summary>Primary key (ULID).</summary>
      [Key] 
      public string cUserClaimId { get; set; } = string.Empty;
      /// <summary>Key of the related <c>ta_User</c> row.</summary>
      public string cUserId { get; set; } = string.Empty;
      /// <summary>Name.</summary>
      public string cUserClaimName { get; set; } = string.Empty;
      /// <summary>Start.</summary>
      public DateTime cUserClaimStart { get; set; }
      /// <summary>Expiry.</summary>
      public DateTime cUserClaimExpiry { get; set; }
      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }
   }
}
