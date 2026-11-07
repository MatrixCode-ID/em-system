namespace Em.Api.Core.Models;

/// <summary>A schedule template with its seven weekdays.</summary>
public sealed class TenantSchedTemplateSet
{
   /// <summary>The template; an empty id creates a new one.</summary>
   public vi_TenantSchedTemplate Template { get; set; } = new();
   /// <summary>Exactly the weekdays 1 to 7.</summary>
   public ta_TenantSchedTemplateDay[] Days { get; set; } = [];
}
