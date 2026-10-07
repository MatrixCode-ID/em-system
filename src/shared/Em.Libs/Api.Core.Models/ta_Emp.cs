using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Employment data of an employee: their employee number according to human resources, and the contact
   /// representing them in the application.
   /// </summary>
   /// <remarks>
   /// Bridge between the employee number - used by documents and legacy applications - and a person's
   /// identity in this application. Through it a document can name who must sign it by employee number,
   /// and the engine can still find the user concerned.
   /// </remarks>
   [Table("ta_Emp")]
   public class ta_Emp
   {
      /// <summary>Primary key (ULID).</summary>
      [Key]
      public string cEmpId { get; set; } = string.Empty;

      /// <summary>Contact representing this employee.</summary>
      public string cContactId { get; set; } = string.Empty;

      /// <summary>Current position of this employee.</summary>
      public string cEmpPositionCurent { get; set; } = string.Empty;
   }
}
