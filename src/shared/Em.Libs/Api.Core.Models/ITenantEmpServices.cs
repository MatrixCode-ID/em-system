using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Tenant organization actions; all HTTP calls require the Business Module Manager claim.</summary>
public interface ITenantEmpServices : IServices
{
   #region Views
   /// <summary>Lists employees of one tenant, ordered by person and employee code.</summary>
   Task<vi_TenantEmp[]> GetVi_TenantEmps(string tenantId);
   #endregion
   #region Meta's
   /// <summary>Finds up to 50 people by name or active user account, marking existing employees.</summary>
   Task<TenantEmpContactCandidate[]> GetMeta_TenantEmpContacts(string tenantId, string? search);
   /// <summary>Saves employment and optionally a new person atomically; clears the old head on transfer or deactivation.</summary>
   Task<vi_TenantEmp> PostGetMeta_TenantEmpSave(TenantEmpSave save);
   /// <summary>Finds active employment for an active user in a tenant.</summary>
   Task<vi_TenantEmp?> GetMeta_TenantEmpByUser(string tenantId, string userId);
   /// <summary>Lists active user IDs associated with an employee.</summary>
   Task<string[]> GetMeta_TenantEmpUsers(string tenantEmpId);
   /// <summary>Gets the active head of the employee department, without searching parent departments.</summary>
   Task<vi_TenantEmp?> GetMeta_TenantEmpHead(string tenantEmpId);
   #endregion
}
