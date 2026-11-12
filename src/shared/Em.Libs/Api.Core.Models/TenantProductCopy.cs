namespace Em.Api.Core.Models;

/// <summary>Direct children of one product to copy under another product of the same tenant.</summary>
public sealed class TenantProductCopy
{
   /// <summary>Tenant of both products.</summary>
   public string TenantId { get; set; } = "";
   /// <summary>Product whose direct children are copied.</summary>
   public string SourceParentId { get; set; } = "";
   /// <summary>Product receiving the copies.</summary>
   public string TargetParentId { get; set; } = "";
   /// <summary>The children to copy, in the order they are shown, each with the code of its copy.</summary>
   public TenantProductCopyRow[] Rows { get; set; } = [];
}
