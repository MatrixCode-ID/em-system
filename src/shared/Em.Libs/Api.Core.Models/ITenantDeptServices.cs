using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Tenant organization actions; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantDeptServices : IServices
{
   #region Tables
   /// <summary>Creates a department without a head.</summary>
   Task<vi_TenantDept> PostGetTa_TenantDept_New(ta_TenantDept row);
   /// <summary>Saves a department, enforcing hierarchy, active membership and update stamp.</summary>
   Task<vi_TenantDept> PostGetTa_TenantDept_Update(ta_TenantDept row);
   #endregion
   #region Views
   /// <summary>Lists departments of one tenant, including inactive records.</summary>
   Task<vi_TenantDept[]> GetVi_TenantDepts(string tenantId);
   #endregion
}
