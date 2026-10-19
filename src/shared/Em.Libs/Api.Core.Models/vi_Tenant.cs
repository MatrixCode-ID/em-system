using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A tenant together with its parent's name and code and the number of its direct children.</summary>
[Table("vi_Tenant")]
public class vi_Tenant : ta_Tenant
{
   /// <summary>Name of the parent tenant; <c>null</c> for a root tenant.</summary>
   public string? cvTenantParentName { get; set; }

   /// <summary>Code of the parent tenant; <c>null</c> for a root tenant.</summary>
   public string? cvTenantParentCode { get; set; }

   /// <summary>Number of direct child tenants, active or not.</summary>
   public int ccTenantChildCount { get; set; }
}
