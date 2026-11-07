using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A working-hour pattern of a tenant. The end of a shift is calculated from its hours and break, never stored.</summary>
[Table("ta_TenantSchedTemplate")]
public class ta_TenantSchedTemplate
{
   /// <summary>Active record, offered to scheduling settings.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record, no longer offered but kept by the settings that use it.</summary>
   public const int StateInactive = -1;
   /// <summary>Longest template code.</summary>
   public const int CodeLength = 10;
   /// <summary>Template ULID; leave empty to let the server create one.</summary>
   [Key] [MaxLength(26)] public string cTenantSchedTemplateId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Short code, unique within its tenant.</summary>
   [MaxLength(CodeLength)] public string cTenantSchedTemplateCode { get; set; } = "";
   /// <summary>Template name.</summary>
   [MaxLength(255)] public string cTenantSchedTemplateName { get; set; } = "";
   /// <summary>Time of day a shift starts.</summary>
   public TimeSpan cTenantSchedTemplateStart { get; set; }
   /// <summary>Break in minutes, used by a weekday that sets none.</summary>
   public int cTenantSchedTemplateBreak { get; set; }
   /// <summary>Active or inactive state.</summary>
   public int cTenantSchedTemplateState { get; set; }
   /// <summary>Display order: lower comes first.</summary>
   public int cTenantSchedTemplateOrder { get; set; } = -1;
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantSchedTemplateNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
