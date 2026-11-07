using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Scheduling settings per level; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantSchedSettingServices : IServices
{
   #region Meta's
   /// <summary>Lists the tenant and its departments with their own values, steps and effective result.</summary>
   Task<TenantSchedSettingRow[]> GetMeta_TenantSchedSettings(string tenantId);
   /// <summary>Calculates what applies to one level, for in-process use by a host.</summary>
   /// <param name="tenantId">Tenant of the level.</param>
   /// <param name="deptId">Department of the level; empty for the tenant level.</param>
   Task<TenantSchedEffective> GetMeta_TenantSchedSetting_Effective(string tenantId, string? deptId);
   /// <summary>Creates or updates one level and its rotation steps, enforcing the update stamp.</summary>
   Task<vi_TenantSchedSetting> PostGetMeta_TenantSchedSetting_Save(TenantSchedSettingSave save);
   /// <summary>Sets a department back to inherit and keeps its row. A department without a row stays as it is and answers empty.</summary>
   Task<vi_TenantSchedSetting?> PostGetMeta_TenantSchedSetting_UseInherited(string tenantId, string deptId, DateTime ustamp);
   #endregion
}
