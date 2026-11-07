using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>The shift that starts on one weekday of a template. A template always has seven of these.</summary>
[Table("ta_TenantSchedTemplateDay")]
public class ta_TenantSchedTemplateDay
{
   /// <summary>Weekday row ULID.</summary>
   [Key] [MaxLength(26)] public string cTenantSchedTemplateDayId { get; set; } = "";
   /// <summary>Template owning this row.</summary>
   [MaxLength(26)] public string cTenantSchedTemplateId { get; set; } = "";
   /// <summary>Weekday the shift starts on: 1 Monday to 7 Sunday.</summary>
   public int cTenantSchedTemplateDayWeekday { get; set; }
   /// <summary>Working hours; 0 is a day off.</summary>
   [Column(TypeName = "decimal(4,2)")] public decimal cTenantSchedTemplateDayHours { get; set; }
   /// <summary>Break in minutes for this weekday; empty uses the template's break.</summary>
   public int? cTenantSchedTemplateDayBreak { get; set; }
   /// <summary>Last update time in UTC.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
