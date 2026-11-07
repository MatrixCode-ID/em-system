namespace Em.Api.Core.Models;

/// <summary>Attendance codes to copy from one tenant to another.</summary>
public sealed class TenantAttCodeCopy
{
   /// <summary>Tenant the codes are read from.</summary>
   public string SourceTenantId { get; set; } = "";
   /// <summary>Tenant receiving the copies.</summary>
   public string TargetTenantId { get; set; } = "";
   /// <summary>Active codes of the source tenant to copy.</summary>
   public string[] CodeIds { get; set; } = [];
}
