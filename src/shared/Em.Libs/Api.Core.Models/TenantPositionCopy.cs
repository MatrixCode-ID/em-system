namespace Em.Api.Core.Models;

/// <summary>Positions to copy from one tenant to another.</summary>
public sealed class TenantPositionCopy
{
   /// <summary>Tenant the positions are read from.</summary>
   public string SourceTenantId { get; set; } = "";
   /// <summary>Tenant receiving the copies.</summary>
   public string TargetTenantId { get; set; } = "";
   /// <summary>Active positions of the source tenant to copy.</summary>
   public string[] PositionIds { get; set; } = [];
}
