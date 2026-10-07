using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_UserSession</c>.</summary>
   [Table("ta_UserSession")]
   public class ta_UserSession  {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cUserSessionId { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_User</c> row.</summary>
      public string cUserId { get; set; } = string.Empty;

      /// <summary>Hash.</summary>
      public string cUserSessionHash { get; set; } = string.Empty;

      /// <summary>Record state.</summary>
      public SessionState cUserSessionState { get; set; }

      /// <summary>Expiry.</summary>
      public DateTime cUserSessionExpiry { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }

   }
}
