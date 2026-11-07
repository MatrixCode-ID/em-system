using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Position list per tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantPositionServices : IServices
{
   #region Tables
   /// <summary>Creates a position.</summary>
   Task<vi_TenantPosition> PostGetTa_TenantPosition_New(ta_TenantPosition row);
   /// <summary>Saves a position, enforcing its update stamp. Setting the inactive state deactivates it; nothing is deleted.</summary>
   Task<vi_TenantPosition> PostGetTa_TenantPosition_Update(ta_TenantPosition row);
   #endregion
   #region Views
   /// <summary>Lists positions of one tenant, including inactive records, ordered by name.</summary>
   Task<vi_TenantPosition[]> GetVi_TenantPositions(string tenantId);
   #endregion
   #region Meta's
   /// <summary>
   /// Copies active positions to another tenant as one-time, active copies. Codes the target already has are
   /// skipped; returns the positions created.
   /// </summary>
   Task<vi_TenantPosition[]> PostGetMeta_TenantPosition_Copy(TenantPositionCopy copy);
   #endregion
}
