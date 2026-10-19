using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Tenant organization proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantDeptClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantDeptServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantDept> PostGetTa_TenantDept_New(ta_TenantDept row) { Prepare(); return PostAsync<vi_TenantDept>(nameof(PostGetTa_TenantDept_New), row); }
   /// <inheritdoc />
   public Task<vi_TenantDept> PostGetTa_TenantDept_Update(ta_TenantDept row) { Prepare(); return PostAsync<vi_TenantDept>(nameof(PostGetTa_TenantDept_Update), row); }
   /// <inheritdoc />
   public Task<vi_TenantDept[]> GetVi_TenantDepts(string tenantId) { Prepare(); return GetAsync<vi_TenantDept[]>(nameof(GetVi_TenantDepts), tenantId); }
}
