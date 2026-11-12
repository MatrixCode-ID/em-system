using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A product type of a tenant. It carries the kind its products follow.</summary>
[Table("ta_TenantProductType")]
public class ta_TenantProductType
{
   /// <summary>Active record, offered to new products.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record, no longer offered but kept by the products that have it.</summary>
   public const int StateInactive = -1;
   /// <summary>Product type ULID; leave empty to let the server create one.</summary>
   [Key] [MaxLength(26)] public string cTenantProductTypeId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Product type code, unique within its tenant.</summary>
   [MaxLength(50)] public string cTenantProductTypeCode { get; set; } = "";
   /// <summary>Product type name.</summary>
   [MaxLength(255)] public string cTenantProductTypeName { get; set; } = "";
   /// <summary>Kind of the products of this type; cannot change once the type has products.</summary>
   public ProductKind cTenantProductTypeKind { get; set; }
   /// <summary>Active or inactive state.</summary>
   public int cTenantProductTypeState { get; set; }
   /// <summary>Display order among the product types of the tenant.</summary>
   public int cTenantProductTypeOrder { get; set; } = -1;
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantProductTypeNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
