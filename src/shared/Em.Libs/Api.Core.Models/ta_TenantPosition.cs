using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A position employees of a tenant can hold.</summary>
[Table("ta_TenantPosition")]
public class ta_TenantPosition
{
   /// <summary>Active record, offered to employees.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record, no longer offered but kept by the employees that hold it.</summary>
   public const int StateInactive = -1;
   /// <summary>Position ULID; leave empty to let the server create one.</summary>
   [Key] [MaxLength(26)] public string cTenantPositionId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Position code, unique within its tenant.</summary>
   [MaxLength(50)] public string cTenantPositionCode { get; set; } = "";
   /// <summary>Position name.</summary>
   [MaxLength(255)] public string cTenantPositionName { get; set; } = "";
   /// <summary>Active or inactive state.</summary>
   public int cTenantPositionState { get; set; }
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantPositionNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
