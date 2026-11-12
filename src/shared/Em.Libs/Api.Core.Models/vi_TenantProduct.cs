using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A product with display values.</summary>
[Table("vi_TenantProduct")]
public class vi_TenantProduct : ta_TenantProduct
{
   /// <summary>Code of the product type.</summary>
   public string cvTenantProductTypeCode { get; set; } = "";
   /// <summary>Name of the product type.</summary>
   public string cvTenantProductTypeName { get; set; } = "";
   /// <summary>Kind of the product, taken from its product type.</summary>
   public ProductKind cvTenantProductTypeKind { get; set; }
   /// <summary>Symbol of the unit.</summary>
   public string cvUnitSymbol { get; set; } = "";
   /// <summary>Number of direct children, of every state.</summary>
   public int ccTenantProductChildCount { get; set; }
}
