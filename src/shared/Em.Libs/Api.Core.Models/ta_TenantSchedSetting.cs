using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>
/// Scheduling setting of one level: the tenant or a department. A level without a row inherits; a tenant without a row
/// is disabled. Effective values are calculated, not stored.
/// </summary>
[Table("ta_TenantSchedSetting")]
public class ta_TenantSchedSetting
{
   /// <summary>Setting ULID.</summary>
   [Key] [MaxLength(26)] public string cTenantSchedSettingId { get; set; } = "";
   /// <summary>Tenant owning this record.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Department of this level; empty on the tenant row.</summary>
   [MaxLength(26)] public string? cTenantDeptId { get; set; }
   /// <summary>The department id, or the tenant id on the tenant row. It keeps one row per level on every database.</summary>
   [MaxLength(26)] public string cTenantSchedSettingKey { get; set; } = "";
   /// <summary>Whether scheduling applies at this level.</summary>
   public TenantSchedEnable cTenantSchedSettingEnable { get; set; }
   /// <summary>Where the default schedule of this level comes from.</summary>
   public TenantSchedMode cTenantSchedSettingMode { get; set; }
   /// <summary>Template of the template mode.</summary>
   [MaxLength(26)] public string? cTenantSchedTemplateId { get; set; }
   /// <summary>Weeks per rotation step.</summary>
   public int? cTenantSchedSettingStepWeeks { get; set; }
   /// <summary>Day 1 of the rotation; dates before it have no rotation result.</summary>
   [Column(TypeName = "date")] public DateTime? cTenantSchedSettingCycleStart { get; set; }
   /// <summary>Length of an assignment period, tenant row only; empty means 14 days.</summary>
   public int? cTenantSchedSettingPeriodDays { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
