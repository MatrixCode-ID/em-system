using Em.Shared;
namespace Em.Api.Core.Models;
/// <summary>An employee save, optionally creating a person atomically.</summary>
public sealed class TenantEmpSave
{
   /// <summary>Employee values and last read update stamp.</summary>
   public ta_TenantEmp Row { get; set; } = new();
   /// <summary>Name of a new person when the contact ID is empty.</summary>
   public string? NewContactName { get; set; }
   /// <summary>Edited defaults of the shared person. Null preserves an existing person's details.</summary>
   public TenantEmpPerson? Person { get; set; }
   /// <summary>Type of the new person.</summary>
   public ContactType NewContactType { get; set; } = ContactType.Male;
   /// <summary>
   /// Whether the employee heads its department after this save. True replaces the current head and needs an
   /// active employee with a department; false clears the head when it is this employee; null leaves it unchanged.
   /// </summary>
   public bool? IsDeptHead { get; set; }
}
