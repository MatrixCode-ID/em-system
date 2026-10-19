using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A tenant organization record with display values.</summary>
[Table("vi_TenantEmp")]
public class vi_TenantEmp : ta_TenantEmp
{
   /// <summary>Full name of the employee.</summary>
   public string cvContactFullName { get; set; } = "";
   /// <summary>Person type.</summary>
   public ContactType cvContactType { get; set; }
   /// <summary>Department code.</summary>
   public string? cvTenantDeptCode { get; set; }
   /// <summary>Department name.</summary>
   public string? cvTenantDeptName { get; set; }
   /// <summary>Whether this employee is the head of its department.</summary>
   public bool ccTenantEmpIsDeptHead { get; set; }
   /// <summary>Active user accounts associated with this person, separated by commas.</summary>
   public string? cvTenantEmpUserAccounts { get; set; }
}
