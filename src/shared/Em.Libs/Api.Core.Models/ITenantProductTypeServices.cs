using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Product types per tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantProductTypeServices : IServices
{
   #region Tables
   /// <summary>Creates a product type. The generic transaction kind cannot be chosen.</summary>
   Task<vi_TenantProductType> PostGetTa_TenantProductType_New(ta_TenantProductType row);
   /// <summary>
   /// Saves a product type, enforcing its update stamp. Setting the inactive state deactivates it without touching
   /// its products; nothing is deleted. The kind cannot change once the type has products, and the system type
   /// only accepts a new name and note.
   /// </summary>
   Task<vi_TenantProductType> PostGetTa_TenantProductType_Update(ta_TenantProductType row);
   #endregion
   #region Views
   /// <summary>
   /// Lists the product types of one tenant, including inactive records, ordered by order and name. The system
   /// type is created first when the tenant does not have it yet.
   /// </summary>
   Task<vi_TenantProductType[]> GetVi_TenantProductTypes(string tenantId);
   #endregion
   #region Meta's
   /// <summary>
   /// Returns the product type with this code, creating it as active when the tenant does not have it. An existing
   /// type is returned as it is, whatever its name, kind or state. Meant for an application that needs a fixed
   /// type of its own; the generic transaction kind is refused.
   /// </summary>
   Task<vi_TenantProductType> PostGetMeta_TenantProductType_Ensure(string tenantId, string code, string name, ProductKind kind);
   #endregion
}
