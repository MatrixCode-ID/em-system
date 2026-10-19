using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>
/// Tenant actions of the Business Module Manager. Every action requires <see cref="ManagerClaim"/> in module
/// <see cref="Defaults.BusinessModuleName"/>; administrators may always call them.
/// </summary>
public interface ITenantServices : IServices
{
   /// <summary>Claim name of the Business Module Manager, written without its module name.</summary>
   const string ManagerClaim = "Business Module Manager Access";

   #region Views

   /// <summary>Every tenant, active and inactive, ordered by display order then name. The client builds the tree.</summary>
   Task<vi_Tenant[]> GetVi_Tenants();

   #endregion

   #region Tables

   /// <summary>
   /// Creates a tenant and returns it as read back. An empty ID is filled by the server. Fails with 400 when
   /// the parent does not exist, and with 409 when the code is already in use.
   /// </summary>
   Task<vi_Tenant> PostGetTa_Tenant_New(ta_Tenant row);

   /// <summary>
   /// Saves name, code, parent, state, order, note and extension data of a tenant and returns it as read back.
   /// <see cref="ta_Tenant.ustamp"/> must be the value last read: 409 when the tenant changed meanwhile or the
   /// code is in use, 404 when it does not exist, 400 when the parent is the tenant itself, one of its
   /// descendants, or does not exist.
   /// </summary>
   Task<vi_Tenant> PostGetTa_Tenant_Update(ta_Tenant row);

   #endregion
}
