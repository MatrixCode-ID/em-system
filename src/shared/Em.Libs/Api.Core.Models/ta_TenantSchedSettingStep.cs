using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>One step of a rotation. Surplus steps go to the inactive state and are reused by order.</summary>
[Table("ta_TenantSchedSettingStep")]
public class ta_TenantSchedSettingStep
{
   /// <summary>Active step of the rotation.</summary>
   public const int StateActive = 0;
   /// <summary>Surplus step, kept for reuse.</summary>
   public const int StateInactive = -1;
   /// <summary>Step ULID.</summary>
   [Key] [MaxLength(26)] public string cTenantSchedSettingStepId { get; set; } = "";
   /// <summary>Setting owning this step.</summary>
   [MaxLength(26)] public string cTenantSchedSettingId { get; set; } = "";
   /// <summary>Tenant owning this record.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Position in the rotation, from 1.</summary>
   public int cTenantSchedSettingStepOrder { get; set; }
   /// <summary>Template of this step.</summary>
   [MaxLength(26)] public string cTenantSchedTemplateId { get; set; } = "";
   /// <summary>Active or inactive state.</summary>
   public int cTenantSchedSettingStepState { get; set; }
   /// <summary>Last update time in UTC.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
