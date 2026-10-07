using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_Role</c>.</summary>
   [Table("ta_Role")]
   public class ta_Role
   {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cRoleId { get; set; } = string.Empty;
      /// <summary>Name.</summary>
      public string cRoleName { get; set; } = string.Empty;
      /// <summary>Record state.</summary>
      public RoleState cRoleState { get; set; }
      /// <summary>Description.</summary>
      public string? cRoleDescription { get; set; }
      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }
      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }
      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }
   }
}
