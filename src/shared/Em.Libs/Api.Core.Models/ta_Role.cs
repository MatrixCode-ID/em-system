using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_Role")]
   public class ta_Role
   {
      [Key]
      public string cRoleId { get; set; } = string.Empty;
      public string cRoleName { get; set; } = string.Empty;
      public RoleState cRoleState { get; set; }
      public string? cRoleDescription { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }
}
