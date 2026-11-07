using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>An attendance code as read by the screens.</summary>
[Table("vi_TenantAttCode")]
public class vi_TenantAttCode : ta_TenantAttCode;
