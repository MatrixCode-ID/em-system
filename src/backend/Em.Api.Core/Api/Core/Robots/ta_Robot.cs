using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core
{
   /// <summary>Row of a robot: a non-human identity that signs in with a token.</summary>
   [Table("ta_Robot")]
   public class ta_Robot
   {
      /// <summary>Key of the robot.</summary>
      [Key] public string cRobotId { get; set; } = string.Empty;
      /// <summary>Name of the robot, which is also its sign-in user name.</summary>
      public string cRobotName { get; set; } = string.Empty;
      /// <summary>The user who owns the robot; ownership is metadata and grants no rights.</summary>
      public string? cRobotOwner_cUserId { get; set; }
      /// <summary>State of the robot: active or inactive.</summary>
      public int cRobotState { get; set; }
      /// <summary>Description of the robot.</summary>
      public string? cRobotDescription { get; set; }

      // SHA-256 hex of the token; the token itself is never stored.
      /// <summary>SHA-256 hash of the token.</summary>
      public string cRobotTokenHash { get; set; } = string.Empty;
      /// <summary>Leading characters of the token stored in the clear to recognize it on screen.</summary>
      public string cRobotTokenPrefix { get; set; } = string.Empty;
      /// <summary>When the token expires, or <c>null</c> for no expiry.</summary>
      public DateTime? cRobotTokenExpiry { get; set; }
      /// <summary>When the token was last used.</summary>
      public DateTime? cRobotTokenLastUsed { get; set; }
      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }
      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }
      /// <summary>Extra data as JSON.</summary>
      public string? json_object { get; set; }
   }

}
