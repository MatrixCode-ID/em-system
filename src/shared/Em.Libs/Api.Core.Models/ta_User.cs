using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Row of table <c>ta_User</c>.</summary>
   [Table("ta_User")]
   public class ta_User  {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cUserId { get; set; } = string.Empty;

      /// <summary>Account.</summary>
      public string cUserAccount { get; set; } = string.Empty;

      /// <summary>Key of the related <c>ta_Contact</c> row.</summary>
      public string cContactId { get; set; } = string.Empty;

      /// <summary>Record state.</summary>
      public UserState cUserState { get; set; }
      
      /// <summary>Is admin.</summary>
      public bool cUserIsAdmin { get; set; }

      /// <summary>Last update time.</summary>
      public DateTime ustamp { get; set; }

      /// <summary>Creation time.</summary>
      public DateTime datestamp { get; set; }

      /// <summary>Additional data as JSON.</summary>
      public string? json_object { get; set; }

   }
}
