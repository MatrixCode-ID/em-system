using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A schedule template with display values.</summary>
[Table("vi_TenantSchedTemplate")]
public class vi_TenantSchedTemplate : ta_TenantSchedTemplate
{
   /// <summary>Working hours of the whole week.</summary>
   [Column(TypeName = "decimal(6,2)")] public decimal ccTenantSchedTemplateWeekHours { get; set; }
   /// <summary>Whether a shift of this template ends on the next day.</summary>
   public bool ccTenantSchedTemplateEndsNextDay { get; set; }
}
