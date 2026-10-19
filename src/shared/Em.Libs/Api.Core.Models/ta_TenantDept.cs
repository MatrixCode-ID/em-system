using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A tenant department.</summary>
[Table("ta_TenantDept")]
public class ta_TenantDept
{
   /// <summary>Active record.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record, retained for reference.</summary>
   public const int StateInactive = -1;
   /// <summary>Department ULID; the server assigns it when empty on create.</summary>
   [Key] [MaxLength(26)] public string cTenantDeptId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>cTenantDeptParent_Department ULID; the server assigns it when empty on create.</summary>
   [MaxLength(26)] public string? cTenantDeptParent_cTenantDeptId { get; set; }
   /// <summary>Department code, unique within its tenant.</summary>
   [MaxLength(50)] public string cTenantDeptCode { get; set; } = "";
   /// <summary>Department display name.</summary>
   [MaxLength(255)] public string cTenantDeptName { get; set; } = "";
   /// <summary>Optional abbreviation.</summary>
   [MaxLength(10)] public string? cTenantDeptAbv { get; set; }
   /// <summary>Active or inactive state.</summary>
   public int cTenantDeptState { get; set; }
   /// <summary>Sort order among siblings.</summary>
   public int cTenantDeptOrder { get; set; } = -1;
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantDeptNote { get; set; }
   /// <summary>Optional active employee of this department who acts as its head.</summary>
   [MaxLength(26)] public string? cTenantDeptHead_cTenantEmpId { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
