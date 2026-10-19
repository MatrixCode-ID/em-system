using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A tenant organization record with display values.</summary>
[Table("vi_TenantDept")]
public class vi_TenantDept : ta_TenantDept
{
   /// <summary>Parent department name.</summary>
   public string? cvTenantDeptParentName { get; set; }
   /// <summary>Parent department code.</summary>
   public string? cvTenantDeptParentCode { get; set; }
   /// <summary>Full name of the head.</summary>
   public string? cvTenantDeptHeadName { get; set; }
   /// <summary>Employee number of the head.</summary>
   public string? cvTenantDeptHeadCode { get; set; }
   /// <summary>Number of direct child departments.</summary>
   public int ccTenantDeptChildCount { get; set; }
   /// <summary>Number of active employees in this department.</summary>
   public int ccTenantDeptEmpCount { get; set; }
}
