using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   // Kuncinya gabungan (cUserId, cRoleId) dan tidak bisa ditulis dengan [Key] - anotasi itu hanya
   // mengerti kunci satu kolom. Pendaftarannya ada di ApiCoreContext.OnModelCreating, bersebelahan
   // dengan ta_RoleClaim yang bentuk kuncinya sama.
   [Table("ta_UserRole")]
   public class ta_UserRole
   {
      public string cUserId { get; set; } = string.Empty;
      public string cRoleId { get; set; } = string.Empty;
      public DateTime? cUserRoleStart { get; set; }
      public DateTime? cUserRoleExpiry { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
   }
}
