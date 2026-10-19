using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>
/// One tenant (company) of the business module manager. Tenants form an optional hierarchy through
/// <see cref="cTenantParent_cTenantId"/> and are never deleted: they are deactivated instead.
/// </summary>
[Table("ta_Tenant")]
public class ta_Tenant
{
   /// <summary>Tenant state of an active tenant.</summary>
   public const int StateActive = 0;

   /// <summary>Tenant state of a deactivated tenant; it stays in the hierarchy and can be activated again.</summary>
   public const int StateInactive = -1;

   /// <summary>Tenant ULID. Left empty on create, the server fills it.</summary>
   [Key, MaxLength(26)] public string cTenantId { get; set; } = "";

   /// <summary>Parent tenant ID; <c>null</c> = a root tenant.</summary>
   [MaxLength(26)] public string? cTenantParent_cTenantId { get; set; }

   /// <summary>Display name of the tenant.</summary>
   [MaxLength(255)] public string cTenantName { get; set; } = "";

   /// <summary>Stable short key chosen by the administrator, unique across all tenants.</summary>
   [MaxLength(100)] public string cTenantCode { get; set; } = "";

   /// <summary><see cref="StateActive"/> or <see cref="StateInactive"/>.</summary>
   public int cTenantState { get; set; } = StateActive;

   /// <summary>Display order among siblings; lower comes first, ties are ordered by name.</summary>
   public int cTenantOrder { get; set; } = -1;

   /// <summary>Optional administrator note.</summary>
   [MaxLength(500)] public string? cTenantNote { get; set; }

   /// <summary>Last update time in UTC. An update sends back the value it read; a changed value is rejected.</summary>
   public DateTime ustamp { get; set; }

   /// <summary>Creation time in UTC.</summary>
   public DateTime datestamp { get; set; }

   /// <summary>Optional extension data.</summary>
   public string? json_object { get; set; }
}
