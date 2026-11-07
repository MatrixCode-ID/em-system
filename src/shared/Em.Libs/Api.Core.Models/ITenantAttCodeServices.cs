using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Attendance codes per tenant; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantAttCodeServices : IServices
{
   #region Tables
   /// <summary>Creates a code.</summary>
   Task<vi_TenantAttCode> PostGetTa_TenantAttCode_New(ta_TenantAttCode row);
   /// <summary>Saves a code, enforcing its update stamp. Setting the inactive state deactivates it; nothing is deleted.</summary>
   Task<vi_TenantAttCode> PostGetTa_TenantAttCode_Update(ta_TenantAttCode row);
   #endregion
   #region Views
   /// <summary>Lists all codes of one tenant, including inactive records, ordered by order and name.</summary>
   Task<vi_TenantAttCode[]> GetVi_TenantAttCodes(string tenantId);
   #endregion
   #region Meta's
   /// <summary>
   /// Copies active codes to another tenant as one-time, active copies. Codes the target already has are skipped;
   /// returns the codes created.
   /// </summary>
   Task<vi_TenantAttCode[]> PostGetMeta_TenantAttCode_Copy(TenantAttCodeCopy copy);
   #endregion
}
