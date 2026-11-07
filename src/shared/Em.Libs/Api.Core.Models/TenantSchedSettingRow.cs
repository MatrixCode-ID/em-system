namespace Em.Api.Core.Models;

/// <summary>One level of the Scheduling settings tree: its own values, its steps and what applies after inheritance.</summary>
public sealed class TenantSchedSettingRow
{
   /// <summary>Tenant of the level.</summary>
   public string TenantId { get; set; } = "";
   /// <summary>Department of the level; empty for the tenant level.</summary>
   public string? DeptId { get; set; }
   /// <summary>Parent department; empty for the tenant level and for a top department.</summary>
   public string? ParentDeptId { get; set; }
   /// <summary>Tenant or department code.</summary>
   public string Code { get; set; } = "";
   /// <summary>Tenant or department name.</summary>
   public string Name { get; set; } = "";
   /// <summary>Whether the department is deactivated.</summary>
   public bool IsInactive { get; set; }
   /// <summary>Display order among the departments with the same parent.</summary>
   public int Order { get; set; }
   /// <summary>The level's own setting; empty when it has no row.</summary>
   public vi_TenantSchedSetting? Setting { get; set; }
   /// <summary>Active steps of the level's own rotation, in order.</summary>
   public TenantSchedEffectiveStep[] Steps { get; set; } = [];
   /// <summary>What applies to this level.</summary>
   public TenantSchedEffective Effective { get; set; } = new();
}
