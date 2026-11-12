using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A product type with display values.</summary>
[Table("vi_TenantProductType")]
public class vi_TenantProductType : ta_TenantProductType
{
   /// <summary>Number of products of this type, of every state.</summary>
   public int ccTenantProductTypeProductCount { get; set; }
}
