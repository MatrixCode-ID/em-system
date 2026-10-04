using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   // Ini adalah temporary claim per user
   [Table("ta_UserClaim")]
   public class ta_UserClaim
   {
      [Key] 
      public string cUserClaimId { get; set; } = string.Empty;
      public string cUserId { get; set; } = string.Empty;
      public string cUserClaimName { get; set; } = string.Empty;
      public DateTime cUserClaimStart { get; set; }
      public DateTime cUserClaimExpiry { get; set; }
      public DateTime datestamp { get; set; }
   }
}
