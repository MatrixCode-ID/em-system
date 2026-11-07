using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>A status code a tenant records for a day. The engine keeps the list; its meaning for payroll belongs to a module.</summary>
[Table("ta_TenantAttCode")]
public class ta_TenantAttCode
{
   /// <summary>Active record, offered for use.</summary>
   public const int StateActive = 0;
   /// <summary>Inactive record, no longer offered.</summary>
   public const int StateInactive = -1;
   /// <summary>Code ULID; leave empty to let the server create one.</summary>
   [Key] [MaxLength(26)] public string cTenantAttCodeId { get; set; } = "";
   /// <summary>Tenant owning this record; cannot change after creation.</summary>
   [MaxLength(26)] public string cTenantId { get; set; } = "";
   /// <summary>Code, unique within its tenant.</summary>
   [MaxLength(50)] public string cTenantAttCodeCode { get; set; } = "";
   /// <summary>Code name.</summary>
   [MaxLength(255)] public string cTenantAttCodeName { get; set; } = "";
   /// <summary>Kind of day the code records.</summary>
   public TenantAttCodeCategory cTenantAttCodeCategory { get; set; } = TenantAttCodeCategory.Present;
   /// <summary>Active or inactive state.</summary>
   public int cTenantAttCodeState { get; set; }
   /// <summary>Display order: lower comes first.</summary>
   public int cTenantAttCodeOrder { get; set; } = -1;
   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantAttCodeNote { get; set; }
   /// <summary>Last update time in UTC, used for optimistic concurrency.</summary>
   public DateTime ustamp { get; set; }
   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }
   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
