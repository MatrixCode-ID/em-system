using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   // The key is the composite (cUserId, cRoleId) and cannot be declared with [Key], which only
   // understands single-column keys. It is registered in ApiCoreContext.OnModelCreating, next to
   // ta_RoleClaim, which has the same key shape.
   /// <summary>Row of table <c>ta_UserRole</c>.</summary>
   [Table("ta_UserRole")]
   public class ta_UserRole
   {
      /// <summary>Key of the related <c>ta_User</c> row.</summary>
      public string cUserId { get; set; } = string.Empty;
      /// <summary>Key of the related <c>ta_Role</c> row.</summary>
      public string cRoleId { get; set; } = string.Empty;
      /// <summary>Start.</summary>
      public DateTime? cUserRoleStart { get; set; }
      /// <summary>Expiry.</summary>
      public DateTime? cUserRoleExpiry { get; set; }
      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }
      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }
   }
}
