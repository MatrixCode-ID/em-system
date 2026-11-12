namespace Em.Api.Core.Models;

/// <summary>One product to copy, with the code its copy gets.</summary>
public sealed class TenantProductCopyRow
{
   /// <summary>The product being copied; a direct child of the source.</summary>
   public string SourceProductId { get; set; } = "";
   /// <summary>Code of the copy, unique within the tenant.</summary>
   public string Code { get; set; } = "";
}
