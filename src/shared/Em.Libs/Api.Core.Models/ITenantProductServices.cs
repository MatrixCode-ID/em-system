using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Product tree per tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantProductServices : IServices
{
   #region Tables
   /// <summary>
   /// Creates a product. Its order is taken from the row when set, otherwise it goes after the last product with
   /// the same parent. A product of the system type cannot be created.
   /// </summary>
   Task<vi_TenantProduct> PostGetTa_TenantProduct_New(ta_TenantProduct row);
   /// <summary>
   /// Saves a product, enforcing its update stamp. The tenant and the parent never change. Setting the inactive
   /// state deactivates it without touching its children; nothing is deleted. The system product only accepts a
   /// new name and note.
   /// </summary>
   Task<vi_TenantProduct> PostGetTa_TenantProduct_Update(ta_TenantProduct row);
   #endregion
   #region Views
   /// <summary>
   /// Lists the direct children of a product, or the root products when <paramref name="parentId"/> is empty,
   /// ordered by order and code. <paramref name="typeId"/> keeps only products of that type.
   /// </summary>
   Task<vi_TenantProduct[]> GetVi_TenantProducts_Children(string tenantId, string? parentId, string? typeId);
   /// <summary>Lists every product of one type as a flat list, including inactive records, ordered by code.</summary>
   Task<vi_TenantProduct[]> GetVi_TenantProducts_ByType(string tenantId, string typeId);
   /// <summary>Returns one product, or <c>null</c> when it does not exist.</summary>
   Task<vi_TenantProduct?> GetVi_TenantProduct_ById(string productId);
   #endregion
   #region Meta's
   /// <summary>
   /// Finds products whose code or name contains the text, at most 200, ordered by code. Each hit carries the ids of
   /// its parents so a tree can be opened down to it. <paramref name="typeId"/> keeps only products of that type.
   /// </summary>
   Task<TenantProductSearchHit[]> GetMeta_TenantProduct_Search(string tenantId, string text, string? typeId);
   /// <summary>Returns the way to a product: its parents from the root down, then the product itself.</summary>
   Task<vi_TenantProduct[]> GetMeta_TenantProduct_Path(string productId);
   /// <summary>Returns which of the codes the tenant already uses, including codes of inactive products.</summary>
   Task<string[]> PostGetMeta_TenantProduct_CodesExist(string tenantId, string[] codes);
   /// <summary>
   /// Creates the children of one product in one transaction, as active products ordered as listed. A code that is
   /// repeated in the batch or already used in the tenant answers a conflict and nothing is created.
   /// </summary>
   Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_AddChildren(TenantProductBatch batch);
   /// <summary>
   /// Copies direct children of one product under another product in one transaction. Each copy takes the name,
   /// unit, type and note of its source and the code given for it, and is saved as active, ordered as listed.
   /// Grandchildren are not copied. A code conflict answers a conflict and nothing is created.
   /// </summary>
   Task<vi_TenantProduct[]> PostGetMeta_TenantProduct_CopyChildren(TenantProductCopy copy);
   /// <summary>Makes sure the tenant has the system product type and the system product. Safe to call again.</summary>
   Task PostMeta_TenantProduct_EnsureSystem(string tenantId);
   #endregion
}
