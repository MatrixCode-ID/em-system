using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Product type proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantProductTypeClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantProductTypeServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantProductType> PostGetTa_TenantProductType_New(ta_TenantProductType row) { Prepare(); return PostAsync<vi_TenantProductType>(nameof(PostGetTa_TenantProductType_New), row); }
   /// <inheritdoc />
   public Task<vi_TenantProductType> PostGetTa_TenantProductType_Update(ta_TenantProductType row) { Prepare(); return PostAsync<vi_TenantProductType>(nameof(PostGetTa_TenantProductType_Update), row); }
   /// <inheritdoc />
   public Task<vi_TenantProductType[]> GetVi_TenantProductTypes(string tenantId) { Prepare(); return GetAsync<vi_TenantProductType[]>(nameof(GetVi_TenantProductTypes), tenantId); }
   /// <inheritdoc />
   public Task<vi_TenantProductType> PostGetMeta_TenantProductType_Ensure(string tenantId, string code, string name, ProductKind kind) { Prepare(); return PostAsync<vi_TenantProductType>(nameof(PostGetMeta_TenantProductType_Ensure), tenantId, code, name, kind); }
}
