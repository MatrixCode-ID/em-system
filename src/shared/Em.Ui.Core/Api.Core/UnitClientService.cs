using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Unit proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class UnitClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), IUnitServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_Unit> PostGetTa_Unit_New(ta_Unit row) { Prepare(); return PostAsync<vi_Unit>(nameof(PostGetTa_Unit_New), row); }
   /// <inheritdoc />
   public Task<vi_Unit> PostGetTa_Unit_Update(ta_Unit row) { Prepare(); return PostAsync<vi_Unit>(nameof(PostGetTa_Unit_Update), row); }
   /// <inheritdoc />
   public Task<vi_Unit[]> GetVi_Units() { Prepare(); return GetAsync<vi_Unit[]>(nameof(GetVi_Units)); }
   /// <inheritdoc />
   public Task PostMeta_Unit_Delete(string unitId, DateTime ustamp) { Prepare(); return PostAsync(nameof(PostMeta_Unit_Delete), unitId, ustamp); }
}
