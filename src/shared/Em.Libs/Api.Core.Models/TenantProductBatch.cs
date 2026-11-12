namespace Em.Api.Core.Models;

/// <summary>New children of one product, created together.</summary>
public sealed class TenantProductBatch
{
   /// <summary>Tenant of the parent.</summary>
   public string TenantId { get; set; } = "";
   /// <summary>Product receiving the children.</summary>
   public string ParentId { get; set; } = "";
   /// <summary>Product type of the children; empty takes the type of the parent.</summary>
   public string? TypeId { get; set; }
   /// <summary>Unit of the children; empty takes the unit of the parent.</summary>
   public string? UnitId { get; set; }
   /// <summary>The children, in the order they are shown.</summary>
   public TenantProductBatchRow[] Rows { get; set; } = [];
}
