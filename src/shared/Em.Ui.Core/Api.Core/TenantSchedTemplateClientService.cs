using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
namespace Em.Ui.Core;
/// <summary>Schedule template proxy following the active connection.</summary>
[Module(Defaults.BusinessModuleName)]
public sealed class TenantSchedTemplateClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ITenantSchedTemplateServices
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<vi_TenantSchedTemplate[]> GetVi_TenantSchedTemplates(string tenantId) { Prepare(); return GetAsync<vi_TenantSchedTemplate[]>(nameof(GetVi_TenantSchedTemplates), tenantId); }
   /// <inheritdoc />
   public Task<TenantSchedTemplateSet> GetMeta_TenantSchedTemplate(string templateId) { Prepare(); return GetAsync<TenantSchedTemplateSet>(nameof(GetMeta_TenantSchedTemplate), templateId); }
   /// <inheritdoc />
   public Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Save(TenantSchedTemplateSet set) { Prepare(); return PostAsync<TenantSchedTemplateSet>(nameof(PostGetMeta_TenantSchedTemplate_Save), set); }
   /// <inheritdoc />
   public Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Duplicate(string templateId) { Prepare(); return PostAsync<TenantSchedTemplateSet>(nameof(PostGetMeta_TenantSchedTemplate_Duplicate), templateId); }
}
