using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A product of a tenant. Products form a tree through their parent.</summary>
[Table("ta_TenantProduct")]
public class ta_TenantProduct
{
   /// <summary>Active record.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record; its children keep their own state.</summary>
   public const int StateInactive = -1;
   /// <summary>Product ULID; leave empty to let the server create one.</summary>
   [Key] [MaxLength(26)] public string cTenantProductId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Product type of the same tenant.</summary>
   [MaxLength(26)] public string cTenantProductTypeId { get; set; } = "";
   /// <summary>Parent product of the same tenant; empty for a root product. Cannot change after creation.</summary>
   [MaxLength(26)] public string? cTenantProductParentId { get; set; }
   /// <summary>Unit of measure.</summary>
   [MaxLength(50)] public string cUnitId { get; set; } = "";
   /// <summary>Product code, entered by hand and unique within its tenant.</summary>
   [MaxLength(50)] public string cTenantProductCode { get; set; } = "";
   /// <summary>Product name.</summary>
   [MaxLength(255)] public string cTenantProductName { get; set; } = "";
   /// <summary>Active or inactive state.</summary>
   public int cTenantProductState { get; set; }
   /// <summary>Display order among the products with the same parent.</summary>
   public int cTenantProductOrder { get; set; } = -1;
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantProductNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Extension data owned by the application that builds on the product; the manager screens never edit it.</summary>
   public string? json_object { get; set; }
}
