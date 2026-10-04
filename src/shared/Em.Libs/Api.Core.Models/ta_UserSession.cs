using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_UserSession")]
   public class ta_UserSession  {
      [Key]
      public string cUserSessionId { get; set; } = string.Empty;

      public string cUserId { get; set; } = string.Empty;

      public string cUserSessionHash { get; set; } = string.Empty;

      public SessionState cUserSessionState { get; set; }

      public DateTime cUserSessionExpiry { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }

   }
}
