using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   // The session table of the accounts that have no row in ta_User - the administrator today, the
   // debugger and whatever comes after it later. Same shape as ta_UserSession minus the foreign key,
   // and the owner column deliberately references nothing: what it holds is an account id that no
   // table owns, so a constraint would have nowhere to point.
   //
   // Kept in the backend rather than in Em.Libs, unlike ta_UserSession: no row of this table is
   // ever handed to a client as it stands - PostGetMeta_GetSessions maps it first - so its shape
   // stays an implementation detail instead of becoming part of the published contract.
   [Table("ta_SystemSession")]
   internal class ta_SystemSession
   {
      [Key]
      public string cSystemSessionId { get; set; } = string.Empty;

      public string cSystemSessionAccountId { get; set; } = string.Empty;

      public string cSystemSessionHash { get; set; } = string.Empty;

      public SessionState cSystemSessionState { get; set; }

      public DateTime cSystemSessionExpiry { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }
   }
}
