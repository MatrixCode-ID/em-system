using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core
{
   [Table("ta_Robot")]
   public class ta_Robot
   {
      [Key] public string cRobotId { get; set; } = string.Empty;
      public string cRobotName { get; set; } = string.Empty;
      public string? cRobotOwner_cUserId { get; set; }
      public int cRobotState { get; set; }
      public string? cRobotDescription { get; set; }

      // SHA-256 hex dari token; tokennya sendiri tidak pernah disimpan.
      public string cRobotTokenHash { get; set; } = string.Empty;
      public string cRobotTokenPrefix { get; set; } = string.Empty;
      public DateTime? cRobotTokenExpiry { get; set; }
      public DateTime? cRobotTokenLastUsed { get; set; }
      public DateTime ustamp { get; set; }
      public DateTime datestamp { get; set; }
      public string? json_object { get; set; }
   }

}
