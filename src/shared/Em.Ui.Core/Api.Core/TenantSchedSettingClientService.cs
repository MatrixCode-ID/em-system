using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Scheduling setting proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantSchedSettingClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantSchedSettingServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<TenantSchedSettingRow[]> GetMeta_TenantSchedSettings(string tenantId) { Prepare(); return GetAsync<TenantSchedSettingRow[]>(nameof(GetMeta_TenantSchedSettings), tenantId); }
   /// <inheritdoc />
   public Task<TenantSchedEffective> GetMeta_TenantSchedSetting_Effective(string tenantId, string? deptId) { Prepare(); return GetAsync<TenantSchedEffective>(nameof(GetMeta_TenantSchedSetting_Effective), tenantId, deptId); }
   /// <inheritdoc />
   public Task<vi_TenantSchedSetting> PostGetMeta_TenantSchedSetting_Save(TenantSchedSettingSave save) { Prepare(); return PostAsync<vi_TenantSchedSetting>(nameof(PostGetMeta_TenantSchedSetting_Save), save); }
   /// <inheritdoc />
   public Task<vi_TenantSchedSetting?> PostGetMeta_TenantSchedSetting_UseInherited(string tenantId, string deptId, DateTime ustamp) { Prepare(); return PostAsync<vi_TenantSchedSetting?>(nameof(PostGetMeta_TenantSchedSetting_UseInherited), tenantId, deptId, ustamp); }
}
