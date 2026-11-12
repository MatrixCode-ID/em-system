namespace Em.Api.Core.Models;

/// <summary>One product of a batch of new children.</summary>
public sealed class TenantProductBatchRow
{
   /// <summary>Product code, unique within the tenant.</summary>
   public string Code { get; set; } = "";
   /// <summary>Product name.</summary>
   public string Name { get; set; } = "";
}
