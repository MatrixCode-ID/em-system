using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A tenant position with display values.</summary>
[Table("vi_TenantPosition")]
public class vi_TenantPosition : ta_TenantPosition
{
   /// <summary>Number of active employees holding this position.</summary>
   public int ccTenantPositionEmpCount { get; set; }
}
