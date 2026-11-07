using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Tenant position proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantPositionClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantPositionServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantPosition> PostGetTa_TenantPosition_New(ta_TenantPosition row) { Prepare(); return PostAsync<vi_TenantPosition>(nameof(PostGetTa_TenantPosition_New), row); }
   /// <inheritdoc />
   public Task<vi_TenantPosition> PostGetTa_TenantPosition_Update(ta_TenantPosition row) { Prepare(); return PostAsync<vi_TenantPosition>(nameof(PostGetTa_TenantPosition_Update), row); }
   /// <inheritdoc />
   public Task<vi_TenantPosition[]> GetVi_TenantPositions(string tenantId) { Prepare(); return GetAsync<vi_TenantPosition[]>(nameof(GetVi_TenantPositions), tenantId); }
   /// <inheritdoc />
   public Task<vi_TenantPosition[]> PostGetMeta_TenantPosition_Copy(TenantPositionCopy copy) { Prepare(); return PostAsync<vi_TenantPosition[]>(nameof(PostGetMeta_TenantPosition_Copy), copy); }
}
