using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>What the Scheduling settings page saves for one level.</summary>
public sealed class TenantSchedSettingSave
{
   /// <summary>Tenant of the level.</summary>
   public string TenantId { get; set; } = "";
   /// <summary>Department of the level; empty for the tenant level.</summary>
   public string? DeptId { get; set; }
   /// <summary>Whether scheduling applies. The tenant level takes enabled or disabled only.</summary>
   public TenantSchedEnable Enable { get; set; }
   /// <summary>Where the default schedule comes from.</summary>
   public TenantSchedMode Mode { get; set; }
   /// <summary>Template of the template mode.</summary>
   public string? TemplateId { get; set; }
   /// <summary>Template of each rotation step, in order.</summary>
   public string[] StepTemplateIds { get; set; } = [];
   /// <summary>Weeks per rotation step.</summary>
   public int? StepWeeks { get; set; }
   /// <summary>Day 1 of the rotation.</summary>
   public DateTime? CycleStart { get; set; }
   /// <summary>Length of an assignment period, tenant level only; empty means 14 days.</summary>
   public int? PeriodDays { get; set; }
   /// <summary>Stamp of the row as read; empty when the level has no row yet.</summary>
   public DateTime? ustamp { get; set; }
}
