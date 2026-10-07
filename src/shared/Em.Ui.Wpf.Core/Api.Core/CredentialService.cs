using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   /// <summary>Client-side implementation of the user, role, claim, sign-in, and session actions.</summary>
   [Module(Defaults.CredentialModuleName)]
   public class CredentialService(EmApp emApp) : ServiceWpfBase(emApp), ICredentialServices
   {
      #region Tables

      #region ta_User

      /// <inheritdoc />
      public Task<ta_User?> GetTa_User_ById(string cUserId) =>
         GetAsync<ta_User?>(nameof(GetTa_User_ById), cUserId);

      /// <inheritdoc />
      public Task<ta_User[]> GetTa_Users() => GetAsync<ta_User[]>(nameof(GetTa_Users));
      /// <inheritdoc />
      public Task<int> GetTa_Users_Count() => GetAsync<int>(nameof(GetTa_Users_Count));

      /// <inheritdoc />
      public Task<ta_User[]> GetTa_Users_ByContactId(string cContactId) =>
         GetAsync<ta_User[]>(nameof(GetTa_Users_ByContactId), cContactId);

      /// <inheritdoc />
      public Task<ta_User[]> GetTa_Users_InPage(int page, int pageSize) =>
         GetAsync<ta_User[]>(nameof(GetTa_Users_InPage), page, pageSize);

      /// <inheritdoc />
      public Task PostTa_User_New(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential> data) =>
         PostAsync(nameof(PostTa_User_New), data);

      /// <inheritdoc />
      public Task PostTa_User_NewBatch(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>[] datas) =>
         PostAsync(nameof(PostTa_User_NewBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_User_Update(ta_User data) => PostAsync(nameof(PostTa_User_Update), data);

      /// <inheritdoc />
      public Task PostTa_User_UpdateBatch(ta_User[] datas) =>
         PostAsync(nameof(PostTa_User_UpdateBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_User_Delete(ta_User data) => PostAsync(nameof(PostTa_User_Delete), data);

      /// <inheritdoc />
      public Task PostTa_User_DeleteBatch(ta_User[] datas) =>
         PostAsync(nameof(PostTa_User_DeleteBatch), (object)datas);

      #endregion

      #region ta_Role

      /// <inheritdoc />
      public Task<ta_Role?> GetTa_Role_ById(string cRoleId) =>
         GetAsync<ta_Role?>(nameof(GetTa_Role_ById), cRoleId);

      /// <inheritdoc />
      public Task<ta_Role[]> GetTa_Roles() => GetAsync<ta_Role[]>(nameof(GetTa_Roles));

      /// <inheritdoc />
      public Task<int> GetTa_Roles_Count() => GetAsync<int>(nameof(GetTa_Roles_Count));

      /// <inheritdoc />
      public Task<ta_Role[]> GetTa_Roles_InPage(int page, int pageSize) =>
         GetAsync<ta_Role[]>(nameof(GetTa_Roles_InPage), page, pageSize);

      /// <inheritdoc />
      public Task PostTa_Role_New(ta_Role data) => PostAsync(nameof(PostTa_Role_New), data);

      /// <inheritdoc />
      public Task PostTa_Role_NewBatch(ta_Role[] datas) =>
         PostAsync(nameof(PostTa_Role_NewBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_Role_Update(ta_Role data) => PostAsync(nameof(PostTa_Role_Update), data);

      /// <inheritdoc />
      public Task PostTa_Role_UpdateBatch(ta_Role[] datas) =>
         PostAsync(nameof(PostTa_Role_UpdateBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_Role_Delete(ta_Role data) => PostAsync(nameof(PostTa_Role_Delete), data);

      /// <inheritdoc />
      public Task PostTa_Role_DeleteBatch(ta_Role[] datas) =>
         PostAsync(nameof(PostTa_Role_DeleteBatch), (object)datas);

      #endregion

      #region ta_RoleClaim

      /// <inheritdoc />
      public Task<ta_RoleClaim[]> GetTa_RoleClaims_ByRoleId(string cRoleId) =>
         GetAsync<ta_RoleClaim[]>(nameof(GetTa_RoleClaims_ByRoleId), cRoleId);

      /// <inheritdoc />
      public Task PostTa_RoleClaim_New(ta_RoleClaim data) {
         EnsureClaimIsDeclared(data.cClaimName);
         return PostAsync(nameof(PostTa_RoleClaim_New), data);
      }

      /// <inheritdoc />
      public Task PostTa_RoleClaim_NewBatch(ta_RoleClaim[] datas) {
         foreach (var row in datas) EnsureClaimIsDeclared(row.cClaimName);
         return PostAsync(nameof(PostTa_RoleClaim_NewBatch), (object)datas);
      }

      /// <inheritdoc />
      public Task PostTa_RoleClaim_Delete(ta_RoleClaim data) =>
         PostAsync(nameof(PostTa_RoleClaim_Delete), data);

      /// <inheritdoc />
      public Task PostTa_RoleClaim_DeleteBatch(ta_RoleClaim[] datas) =>
         PostAsync(nameof(PostTa_RoleClaim_DeleteBatch), (object)datas);

      #endregion

      #region ta_UserRole

      /// <inheritdoc />
      public Task<ta_UserRole[]> GetTa_UserRoles_ByRoleId(string cRoleId) =>
         GetAsync<ta_UserRole[]>(nameof(GetTa_UserRoles_ByRoleId), cRoleId);

      /// <inheritdoc />
      public Task<ta_UserRole[]> GetTa_UserRoles_ByUserId(string cUserId) =>
         GetAsync<ta_UserRole[]>(nameof(GetTa_UserRoles_ByUserId), cUserId);

      /// <inheritdoc />
      public Task<int> GetTa_UserRoles_Count() => GetAsync<int>(nameof(GetTa_UserRoles_Count));

      /// <inheritdoc />
      public Task PostTa_UserRole_New(ta_UserRole data) =>
         PostAsync(nameof(PostTa_UserRole_New), data);

      /// <inheritdoc />
      public Task PostTa_UserRole_NewBatch(ta_UserRole[] datas) =>
         PostAsync(nameof(PostTa_UserRole_NewBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_UserRole_Update(ta_UserRole data) =>
         PostAsync(nameof(PostTa_UserRole_Update), data);

      /// <inheritdoc />
      public Task PostTa_UserRole_Delete(ta_UserRole data) =>
         PostAsync(nameof(PostTa_UserRole_Delete), data);

      /// <inheritdoc />
      public Task PostTa_UserRole_DeleteBatch(ta_UserRole[] datas) =>
         PostAsync(nameof(PostTa_UserRole_DeleteBatch), (object)datas);

      #endregion

      #endregion

      #region Views

      #region vi_User

      /// <inheritdoc />
      public Task<vi_User?> GetVi_User_ById(string cUserId) =>
         GetAsync<vi_User?>(nameof(GetVi_User_ById), cUserId);
      
      /// <inheritdoc />
      public Task<vi_User?> GetVi_User_ByAccount(string cUserAccount) =>
         GetAsync<vi_User?>(nameof(GetVi_User_ByAccount), cUserAccount);

      /// <inheritdoc />
      public Task<vi_User[]> GetVi_Users() => GetAsync<vi_User[]>(nameof(GetVi_Users));

      /// <inheritdoc />
      public Task<vi_User[]> GetVi_Users_InPage(int page, int pageSize) =>
         GetAsync<vi_User[]>(nameof(GetVi_Users_InPage), page, pageSize);

      /// <inheritdoc />
      public Task<vi_User[]> GetVi_Users_ByContactId(string cContactId) =>
         GetAsync<vi_User[]>(nameof(GetVi_Users_ByContactId), cContactId);

      /// <inheritdoc />
      public Task<vi_User[]> GetVi_Users_ByRoleId(string cRoleId) =>
         GetAsync<vi_User[]>(nameof(GetVi_Users_ByRoleId), cRoleId);

      #endregion

      #region vi_Role

      /// <inheritdoc />
      public Task<vi_Role?> GetVi_Role_ById(string cRoleId) =>
         GetAsync<vi_Role?>(nameof(GetVi_Role_ById), cRoleId);

      /// <inheritdoc />
      public Task<vi_Role[]> GetVi_Roles() => GetAsync<vi_Role[]>(nameof(GetVi_Roles));

      /// <inheritdoc />
      public Task<vi_Role[]> GetVi_Roles_InPage(int page, int pageSize) =>
         GetAsync<vi_Role[]>(nameof(GetVi_Roles_InPage), page, pageSize);

      #endregion

      #endregion

      #region Meta's

      /// <inheritdoc />
      public Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password) =>
         PostAsync<TokenResult>(nameof(PostGetMeta_SignIn), cUserAccount, password);

      /// <inheritdoc />
      public Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken) =>
         PostAsync<TokenResult>(nameof(PostGetMeta_RefreshToken), refreshToken);

      /// <inheritdoc />
      public Task PostMeta_SignOut() => PostAsync(nameof(PostMeta_SignOut));

      /// <inheritdoc />
      public Task PostMeta_SignOutAll() => PostAsync(nameof(PostMeta_SignOutAll));

      /// <inheritdoc />
      public Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword) =>
         PostAsync(nameof(PostMeta_ChangeMyPassword), oldPassword, newPassword);

      /// <inheritdoc />
      public Task PostMeta_ResetPassword(string cUserId, string newPassword) =>
         PostAsync(nameof(PostMeta_ResetPassword), cUserId, newPassword);

      /// <inheritdoc />
      public Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId) =>
         PostAsync<ta_UserSession[]>(nameof(PostGetMeta_GetSessions), cUserId);

      /// <inheritdoc />
      public Task PostMeta_ResetAdminPassword(string newPassword) =>
         PostAsync(nameof(PostMeta_ResetAdminPassword), newPassword);

      /// <inheritdoc />
      public Task PostMeta_ChangeAdminPassword(string oldPassword, string newPassword) =>
         PostAsync(nameof(PostMeta_ChangeAdminPassword), oldPassword, newPassword);

      /// <inheritdoc />
      public Task<ClaimAction[]> GetMeta_AllClaimActions() =>
         GetAsync<ClaimAction[]>(nameof(GetMeta_AllClaimActions));

      /// <inheritdoc />
      public Task<ClaimAction[]> GetMeta_UserClaims(string cUserId) =>
         GetAsync<ClaimAction[]>(nameof(GetMeta_UserClaims), cUserId);

      /// <inheritdoc />
      public Task PostMeta_AddUserClaim(UserClaimPayload claim) {
         EnsureClaimIsDeclared(claim.cUserClaimName);
         return PostAsync(nameof(PostMeta_AddUserClaim), claim);
      }

      /// <inheritdoc />
      public Task PostMeta_RemoveUserClaim(UserClaimPayload claim) =>
         PostAsync(nameof(PostMeta_RemoveUserClaim), claim);

      /// <inheritdoc />
      public Task<ClaimAction[]> GetMeta_UserRoleClaims(string cUserId) =>
         GetAsync<ClaimAction[]>(nameof(GetMeta_UserRoleClaims), cUserId);

      /// <inheritdoc />
      public Task<RoleCounter[]> GetMeta_RoleCounters() =>
         GetAsync<RoleCounter[]>(nameof(GetMeta_RoleCounters));

      /// <inheritdoc />
      public Task<ClaimUsage[]> GetMeta_ClaimUsage() =>
         GetAsync<ClaimUsage[]>(nameof(GetMeta_ClaimUsage));

      #endregion

      #region Helpers

      // The one place that decides whether a right key may be sent or not, used both by direct granting and
      // by the rights of a role. This used to be done by the server; it moved here because only this side
      // knows the whole catalog. The server only knows the rights declared by modules, while AllClaims here is
      // their union with the rights of the client's built-in screens - valid rights that belong to no module,
      // and that the server would refuse if it were the one checking.
      //
      // Checking is still needed: if it were let through, a single typo would quietly become a right that
      // never turns on and never looks wrong.
      private void EnsureClaimIsDeclared(string claimKey) {
         if (App.AllClaims.Any(r => string.Equals(r.Key, claimKey, StringComparison.OrdinalIgnoreCase))) return;

         throw new InvalidOperationException(
            $"Claim '{claimKey}' is not declared. Check the spelling, or declare it - on the server " +
            "with 'AddClaims' for a claim a module owns, on the client with 'AddInternalClaim' for a " +
            "claim one of the built-in screens owns.");
      }

      #endregion
   }
}