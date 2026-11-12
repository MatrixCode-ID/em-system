namespace Em.Api.Core.Models;

/// <summary>A product found by a search, with the way to it from the root.</summary>
public sealed class TenantProductSearchHit
{
   /// <summary>The product found.</summary>
   public vi_TenantProduct Row { get; set; } = new();
   /// <summary>Ids of its parents, from the root product down to its direct parent; empty for a root product.</summary>
   public string[] ParentIds { get; set; } = [];
}
