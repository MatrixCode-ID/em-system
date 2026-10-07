using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_RoleClaim</c>.</summary>
   [Table("ta_RoleClaim")]
   public class ta_RoleClaim
   {
      /// <summary>Key of the related <c>ta_Role</c> row.</summary>
      public string cRoleId { get; set; } = string.Empty;
      /// <summary>Claim name.</summary>
      public string cClaimName { get; set; } = string.Empty;
   }
}