using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A tenant employee.</summary>
[Table("ta_TenantEmp")]
public class ta_TenantEmp
{
   /// <summary>Active record.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record, retained for reference.</summary>
   public const int StateInactive = -1;
   /// <summary>Employee ULID; leave empty to create employment.</summary>
   [Key] [MaxLength(26)] public string cTenantEmpId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Person associated with employment; cannot change after creation.</summary>
   [MaxLength(26)] public string cContactId { get; set; } = "";
   /// <summary>Optional department in the same tenant.</summary>
   [MaxLength(26)] public string? cTenantDeptId { get; set; }
   /// <summary>Employee number, unique within its tenant.</summary>
   [MaxLength(50)] public string cTenantEmpCode { get; set; } = "";
   /// <summary>Optional position title.</summary>
   [MaxLength(255)] public string? cTenantEmpPosition { get; set; }
   /// <summary>Active or inactive employment state.</summary>
   public int cTenantEmpState { get; set; }
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantEmpNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
