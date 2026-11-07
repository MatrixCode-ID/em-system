using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Attendance code proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantAttCodeClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantAttCodeServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantAttCode> PostGetTa_TenantAttCode_New(ta_TenantAttCode row) { Prepare(); return PostAsync<vi_TenantAttCode>(nameof(PostGetTa_TenantAttCode_New), row); }
   /// <inheritdoc />
   public Task<vi_TenantAttCode> PostGetTa_TenantAttCode_Update(ta_TenantAttCode row) { Prepare(); return PostAsync<vi_TenantAttCode>(nameof(PostGetTa_TenantAttCode_Update), row); }
   /// <inheritdoc />
   public Task<vi_TenantAttCode[]> GetVi_TenantAttCodes(string tenantId) { Prepare(); return GetAsync<vi_TenantAttCode[]>(nameof(GetVi_TenantAttCodes), tenantId); }
   /// <inheritdoc />
   public Task<vi_TenantAttCode[]> PostGetMeta_TenantAttCode_Copy(TenantAttCodeCopy copy) { Prepare(); return PostAsync<vi_TenantAttCode[]>(nameof(PostGetMeta_TenantAttCode_Copy), copy); }
}
