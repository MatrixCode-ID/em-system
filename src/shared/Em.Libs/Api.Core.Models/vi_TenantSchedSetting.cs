using System.ComponentModel.DataAnnotations.Schema;

namespace Em.Api.Core.Models;

/// <summary>A scheduling setting with department and template names.</summary>
[Table("vi_TenantSchedSetting")]
public class vi_TenantSchedSetting : ta_TenantSchedSetting
{
   /// <summary>Department code; empty on the tenant row.</summary>
   public string? cvTenantDeptCode { get; set; }
   /// <summary>Department name; empty on the tenant row.</summary>
   public string? cvTenantDeptName { get; set; }
   /// <summary>Template code of the template mode.</summary>
   public string? cvTenantSchedTemplateCode { get; set; }
   /// <summary>Template name of the template mode.</summary>
   public string? cvTenantSchedTemplateName { get; set; }
}
