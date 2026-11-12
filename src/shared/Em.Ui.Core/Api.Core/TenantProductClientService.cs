using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Product proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantProductClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantProductServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantProduct> PostGetTa_TenantProduct_New(ta_TenantProduct row) { Prepare(); return PostAsync<vi_TenantProduct>(nameof(PostGetTa_TenantProduct_New), row); }
   /// <inheritdoc />
   public Task<vi_TenantProduct> PostGetTa_TenantProduct_Update(ta_TenantProduct row) { Prepare(); return PostAsync<vi_TenantProduct>(nameof(PostGetTa_TenantProduct_Update), row); }
   /// <inheritdoc />
   public Task<vi_TenantProduct[]> GetVi_TenantProducts_Children(string tenantId, string? parentId, string? typeId) { Prepare(); return GetAsync<vi_TenantProduct[]>(nameof(GetVi_TenantProducts_Children), tenantId, parentId!, typeId!); }
   /// <inheritdoc />
   public Task<vi_TenantProduct[]> GetVi_TenantProducts_ByType(string tenantId, string typeId) { Prepare(); return GetAsync<vi_TenantProduct[]>(nameof(GetVi_TenantProducts_ByType), tenantId, typeId); }
   /// <inheritdoc />
   public Task<vi_TenantProduct?> GetVi_TenantProduct_ById(string productId) { Prepare(); return GetAsync<vi_TenantProduct?>(nameof(GetVi_TenantProduct_ById), productId); }
   /// <inheritdoc />
   public Task<TenantProductSearchHit[]> GetMeta_TenantProduct_Search(string tenantId, string text, string? typeId) { Prepare(); return GetAsync<TenantProductSearchHit[]>(nameof(GetMeta_TenantProduct_Search), tenantId, text, typeId!); }
   /// <inheritdoc />
   public Task<vi_TenantProduct[]> GetMeta_TenantProduct_Path(string productId) { Prepare(); return GetAsync<vi_TenantProduct[]>(nameof(GetMeta_TenantProduct_Path), productId); }
   /// <inheritdoc />
   public Task<string[]> PostGetMeta_TenantProduct_CodesExist(string tenantId, string[] codes) { Prepare(); return PostAsync<string[]>(nameof(PostGetMeta_TenantProduct_CodesExist), tenantId, codes); }
   /// <inheritdoc />
   public Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_AddChildren(TenantProductBatch batch) { Prepare(); return PostAsync<vi_TenantProduct[]>(nameof(PostGetMeta_TenantProduct_AddChildren), batch); }
   /// <inheritdoc />
   public Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_CopyChildren(TenantProductCopy copy) { Prepare(); return PostAsync<vi_TenantProduct[]>(nameof(PostGetMeta_TenantProduct_CopyChildren), copy); }
   /// <inheritdoc />
   public Task PostMeta_TenantProduct_EnsureSystem(string tenantId) { Prepare(); return PostAsync(nameof(PostMeta_TenantProduct_EnsureSystem), tenantId); }
}
