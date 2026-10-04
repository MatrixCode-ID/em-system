using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models
{
   [Table("ta_User")]
   public class ta_User  {
      [Key]
      public string cUserId { get; set; } = string.Empty;

      public string cUserAccount { get; set; } = string.Empty;

      public string cContactId { get; set; } = string.Empty;

      public UserState cUserState { get; set; }
      
      public bool cUserIsAdmin { get; set; }

      public DateTime ustamp { get; set; }

      public DateTime datestamp { get; set; }

      public string? json_object { get; set; }

   }
}
