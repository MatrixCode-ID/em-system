using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core;

/// <summary>Shared tenant proxy of the Business Module Manager that follows the current API connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantServices
{
   private void Prepare() => ApiClient = activeClient();

   /// <inheritdoc />
   public Task<vi_Tenant[]> GetVi_Tenants() { Prepare(); return GetAsync<vi_Tenant[]>(nameof(GetVi_Tenants)); }

   /// <inheritdoc />
   public Task<vi_Tenant> PostGetTa_Tenant_New(ta_Tenant row) { Prepare(); return PostAsync<vi_Tenant>(nameof(PostGetTa_Tenant_New), row); }

   /// <inheritdoc />
   public Task<vi_Tenant> PostGetTa_Tenant_Update(ta_Tenant row) { Prepare(); return PostAsync<vi_Tenant>(nameof(PostGetTa_Tenant_Update), row); }
}
