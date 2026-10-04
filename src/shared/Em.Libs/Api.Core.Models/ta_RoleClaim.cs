using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   [Table("ta_RoleClaim")]
   public class ta_RoleClaim
   {
      public string cRoleId { get; set; } = string.Empty;
      public string cClaimName { get; set; } = string.Empty;
   }
}