using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Tenant organization proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantEmpClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantEmpServices
{
   /// <inheritdoc />
   public Task<TenantEmpPerson> GetMeta_TenantEmpPerson(string contactId) { Prepare(); return GetAsync<TenantEmpPerson>(nameof(GetMeta_TenantEmpPerson), contactId); }
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId) { Prepare(); return GetAsync<vi_TenantEmp[]>(nameof(GetVi_TenantEmps), tenantId); }
   /// <inheritdoc />
   public Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search) { Prepare(); return GetAsync<TenantEmpContactCandidate[]>(nameof(GetMeta_TenantEmpContacts), tenantId, search!); }
   /// <inheritdoc />
   public Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save) { Prepare(); return PostAsync<vi_TenantEmp>(nameof(PostGetMeta_TenantEmpSave), save); }
   /// <inheritdoc />
   public Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId) { Prepare(); return GetAsync<vi_TenantEmp?>(nameof(GetMeta_TenantEmpByUser), tenantId, userId); }
   /// <inheritdoc />
   public Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId) { Prepare(); return GetAsync<string[]>(nameof(GetMeta_TenantEmpUsers), tenantEmpId); }
   /// <inheritdoc />
   public Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId) { Prepare(); return GetAsync<vi_TenantEmp?>(nameof(GetMeta_TenantEmpHead), tenantEmpId); }
}
