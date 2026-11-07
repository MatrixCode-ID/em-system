using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>One active step of a rotation, with its template.</summary>
public sealed class TenantSchedEffectiveStep
{
   /// <summary>Position in the rotation, from 1.</summary>
   public int Order { get; set; }
   /// <summary>Template of the step.</summary>
   public string TemplateId { get; set; } = "";
   /// <summary>Template code.</summary>
   public string TemplateCode { get; set; } = "";
   /// <summary>Template name.</summary>
   public string TemplateName { get; set; } = "";
   /// <summary>Whether the template is still active.</summary>
   public bool IsTemplateActive { get; set; } = true;
}

/// <summary>What applies to one level after inheritance. It is calculated on every read and never stored.</summary>
public sealed class TenantSchedEffective
{
   /// <summary>Whether scheduling applies. When false the schedule values below are not in use.</summary>
   public bool Enabled { get; set; }
   /// <summary>Department that set <see cref="Enabled"/>; empty when it comes from the tenant.</summary>
   public string? EnableSourceDeptId { get; set; }
   /// <summary>Name of the level that set <see cref="Enabled"/>.</summary>
   public string EnableSource { get; set; } = "";
   /// <summary>The schedule in use: built-in, template or rotation.</summary>
   public TenantSchedMode Mode { get; set; }
   /// <summary>Department that set the schedule; empty when it comes from the tenant.</summary>
   public string? ScheduleSourceDeptId { get; set; }
   /// <summary>Name of the level that set the schedule.</summary>
   public string ScheduleSource { get; set; } = "";
   /// <summary>Template of the template mode.</summary>
   public string? TemplateId { get; set; }
   /// <summary>Template code of the template mode.</summary>
   public string? TemplateCode { get; set; }
   /// <summary>Template name of the template mode.</summary>
   public string? TemplateName { get; set; }
   /// <summary>Active steps of the rotation, in order.</summary>
   public TenantSchedEffectiveStep[] Steps { get; set; } = [];
   /// <summary>Weeks per rotation step.</summary>
   public int? StepWeeks { get; set; }
   /// <summary>Day 1 of the rotation.</summary>
   public DateTime? CycleStart { get; set; }
   /// <summary>Length of an assignment period, always from the tenant level.</summary>
   public int PeriodDays { get; set; } = TenantSchedBuiltIn.DefaultPeriodDays;
   /// <summary>Whether the schedule refers to a template that was deactivated.</summary>
   public bool HasInactiveTemplate { get; set; }
   /// <summary>The result as a sentence, for display.</summary>
   public string Text { get; set; } = "";

   /// <summary>
   /// The rotation step that applies on a work date. Empty when this is not a rotation or when the date is before Day 1.
   /// </summary>
   /// <param name="workDate">The date a shift starts.</param>
   public TenantSchedEffectiveStep? StepOn(DateTime workDate) {
      if (Mode != TenantSchedMode.Rotation || CycleStart is not { } start || StepWeeks is not > 0 || Steps.Length == 0 || workDate.Date < start.Date) return null;
      var passedSteps = (workDate.Date - start.Date).Days / (StepWeeks.Value * 7);
      return Steps[passedSteps % Steps.Length];
   }
}
