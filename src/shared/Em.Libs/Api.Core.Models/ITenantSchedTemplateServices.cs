using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Schedule templates per tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantSchedTemplateServices : IServices
{
   #region Views
   /// <summary>Lists all templates of one tenant, including inactive records, ordered by order and name.</summary>
   Task<vi_TenantSchedTemplate[]> GetVi_TenantSchedTemplates(string tenantId);
   #endregion
   #region Meta's
   /// <summary>Reads a template with its seven days.</summary>
   Task<TenantSchedTemplateSet> GetMeta_TenantSchedTemplate(string templateId);
   /// <summary>
   /// Creates or updates a template and its days in one transaction, enforcing the update stamp. Setting the inactive
   /// state deactivates it; settings that use it keep it.
   /// </summary>
   Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Save(TenantSchedTemplateSet set);
   /// <summary>Copies a template as an active one: the name gets " copy", the code a number.</summary>
   Task<TenantSchedTemplateSet> PostGetMeta_TenantSchedTemplate_Duplicate(string templateId);
   #endregion
}
