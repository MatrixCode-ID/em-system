using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Maui.Core;

namespace Em.Api.Core
{
   [Module(Defaults.CredentialModuleName)]
   public class CredentialService(EmApp emApp) : ServiceMauiBase(emApp), ICredentialServices
   {
      #region Tables

      #region ta_User

      public Task<ta_User?> GetTa_User_ById(string cUserId) =>
         GetAsync<ta_User?>(nameof(GetTa_User_ById), cUserId);

      public Task<ta_User[]> GetTa_Users() => GetAsync<ta_User[]>(nameof(GetTa_Users));
      public Task<int> GetTa_Users_Count() => GetAsync<int>(nameof(GetTa_Users_Count));

      public Task<ta_User[]> GetTa_Users_ByContactId(string cContactId) =>
         GetAsync<ta_User[]>(nameof(GetTa_Users_ByContactId), cContactId);

      public Task<ta_User[]> GetTa_Users_InPage(int page, int pageSize) =>
         GetAsync<ta_User[]>(nameof(GetTa_Users_InPage), page, pageSize);

      public Task PostTa_User_New(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential> data) =>
         PostAsync(nameof(PostTa_User_New), data);

      public Task PostTa_User_NewBatch(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>[] datas) =>
         PostAsync(nameof(PostTa_User_NewBatch), (object)datas);

      public Task PostTa_User_Update(ta_User data) => PostAsync(nameof(PostTa_User_Update), data);

      public Task PostTa_User_UpdateBatch(ta_User[] datas) =>
         PostAsync(nameof(PostTa_User_UpdateBatch), (object)datas);

      public Task PostTa_User_Delete(ta_User data) => PostAsync(nameof(PostTa_User_Delete), data);

      public Task PostTa_User_DeleteBatch(ta_User[] datas) =>
         PostAsync(nameof(PostTa_User_DeleteBatch), (object)datas);

      #endregion

      #region ta_Role

      public Task<ta_Role?> GetTa_Role_ById(string cRoleId) =>
         GetAsync<ta_Role?>(nameof(GetTa_Role_ById), cRoleId);

      public Task<ta_Role[]> GetTa_Roles() => GetAsync<ta_Role[]>(nameof(GetTa_Roles));

      public Task<int> GetTa_Roles_Count() => GetAsync<int>(nameof(GetTa_Roles_Count));

      public Task<ta_Role[]> GetTa_Roles_InPage(int page, int pageSize) =>
         GetAsync<ta_Role[]>(nameof(GetTa_Roles_InPage), page, pageSize);

      public Task PostTa_Role_New(ta_Role data) => PostAsync(nameof(PostTa_Role_New), data);

      public Task PostTa_Role_NewBatch(ta_Role[] datas) =>
         PostAsync(nameof(PostTa_Role_NewBatch), (object)datas);

      public Task PostTa_Role_Update(ta_Role data) => PostAsync(nameof(PostTa_Role_Update), data);

      public Task PostTa_Role_UpdateBatch(ta_Role[] datas) =>
         PostAsync(nameof(PostTa_Role_UpdateBatch), (object)datas);

      public Task PostTa_Role_Delete(ta_Role data) => PostAsync(nameof(PostTa_Role_Delete), data);

      public Task PostTa_Role_DeleteBatch(ta_Role[] datas) =>
         PostAsync(nameof(PostTa_Role_DeleteBatch), (object)datas);

      #endregion

      #region ta_RoleClaim

      public Task<ta_RoleClaim[]> GetTa_RoleClaims_ByRoleId(string cRoleId) =>
         GetAsync<ta_RoleClaim[]>(nameof(GetTa_RoleClaims_ByRoleId), cRoleId);

      public Task PostTa_RoleClaim_New(ta_RoleClaim data) {
         EnsureClaimIsDeclared(data.cClaimName);
         return PostAsync(nameof(PostTa_RoleClaim_New), data);
      }

      public Task PostTa_RoleClaim_NewBatch(ta_RoleClaim[] datas) {
         foreach (var row in datas) EnsureClaimIsDeclared(row.cClaimName);
         return PostAsync(nameof(PostTa_RoleClaim_NewBatch), (object)datas);
      }

      public Task PostTa_RoleClaim_Delete(ta_RoleClaim data) =>
         PostAsync(nameof(PostTa_RoleClaim_Delete), data);

      public Task PostTa_RoleClaim_DeleteBatch(ta_RoleClaim[] datas) =>
         PostAsync(nameof(PostTa_RoleClaim_DeleteBatch), (object)datas);

      #endregion

      #region ta_UserRole

      public Task<ta_UserRole[]> GetTa_UserRoles_ByRoleId(string cRoleId) =>
         GetAsync<ta_UserRole[]>(nameof(GetTa_UserRoles_ByRoleId), cRoleId);

      public Task<ta_UserRole[]> GetTa_UserRoles_ByUserId(string cUserId) =>
         GetAsync<ta_UserRole[]>(nameof(GetTa_UserRoles_ByUserId), cUserId);

      public Task<int> GetTa_UserRoles_Count() => GetAsync<int>(nameof(GetTa_UserRoles_Count));

      public Task PostTa_UserRole_New(ta_UserRole data) =>
         PostAsync(nameof(PostTa_UserRole_New), data);

      public Task PostTa_UserRole_NewBatch(ta_UserRole[] datas) =>
         PostAsync(nameof(PostTa_UserRole_NewBatch), (object)datas);

      public Task PostTa_UserRole_Update(ta_UserRole data) =>
         PostAsync(nameof(PostTa_UserRole_Update), data);

      public Task PostTa_UserRole_Delete(ta_UserRole data) =>
         PostAsync(nameof(PostTa_UserRole_Delete), data);

      public Task PostTa_UserRole_DeleteBatch(ta_UserRole[] datas) =>
         PostAsync(nameof(PostTa_UserRole_DeleteBatch), (object)datas);

      #endregion

      #endregion

      #region Views

      #region vi_User

      public Task<vi_User?> GetVi_User_ById(string cUserId) =>
         GetAsync<vi_User?>(nameof(GetVi_User_ById), cUserId);
      
      public Task<vi_User?> GetVi_User_ByAccount(string cUserAccount) =>
         GetAsync<vi_User?>(nameof(GetVi_User_ByAccount), cUserAccount);

      public Task<vi_User[]> GetVi_Users() => GetAsync<vi_User[]>(nameof(GetVi_Users));

      public Task<vi_User[]> GetVi_Users_InPage(int page, int pageSize) =>
         GetAsync<vi_User[]>(nameof(GetVi_Users_InPage), page, pageSize);

      public Task<vi_User[]> GetVi_Users_ByContactId(string cContactId) =>
         GetAsync<vi_User[]>(nameof(GetVi_Users_ByContactId), cContactId);

      public Task<vi_User[]> GetVi_Users_ByRoleId(string cRoleId) =>
         GetAsync<vi_User[]>(nameof(GetVi_Users_ByRoleId), cRoleId);

      #endregion

      #region vi_Role

      public Task<vi_Role?> GetVi_Role_ById(string cRoleId) =>
         GetAsync<vi_Role?>(nameof(GetVi_Role_ById), cRoleId);

      public Task<vi_Role[]> GetVi_Roles() => GetAsync<vi_Role[]>(nameof(GetVi_Roles));

      public Task<vi_Role[]> GetVi_Roles_InPage(int page, int pageSize) =>
         GetAsync<vi_Role[]>(nameof(GetVi_Roles_InPage), page, pageSize);

      #endregion

      #endregion

      #region Meta's

      public Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password) =>
         PostAsync<TokenResult>(nameof(PostGetMeta_SignIn), cUserAccount, password);

      public Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken) =>
         PostAsync<TokenResult>(nameof(PostGetMeta_RefreshToken), refreshToken);

      public Task PostMeta_SignOut() => PostAsync(nameof(PostMeta_SignOut));

      public Task PostMeta_SignOutAll() => PostAsync(nameof(PostMeta_SignOutAll));

      public Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword) =>
         PostAsync(nameof(PostMeta_ChangeMyPassword), oldPassword, newPassword);

      public Task PostMeta_ResetPassword(string cUserId, string newPassword) =>
         PostAsync(nameof(PostMeta_ResetPassword), cUserId, newPassword);

      public Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId) =>
         PostAsync<ta_UserSession[]>(nameof(PostGetMeta_GetSessions), cUserId);

      public Task PostMeta_ResetAdminPassword(string newPassword) =>
         PostAsync(nameof(PostMeta_ResetAdminPassword), newPassword);

      public Task PostMeta_ChangeAdminPassword(string oldPassword, string newPassword) =>
         PostAsync(nameof(PostMeta_ChangeAdminPassword), oldPassword, newPassword);

      public Task<ClaimAction[]> GetMeta_AllClaimActions() =>
         GetAsync<ClaimAction[]>(nameof(GetMeta_AllClaimActions));

      public Task<ClaimAction[]> GetMeta_UserClaims(string cUserId) =>
         GetAsync<ClaimAction[]>(nameof(GetMeta_UserClaims), cUserId);

      public Task PostMeta_AddUserClaim(UserClaimPayload claim) {
         EnsureClaimIsDeclared(claim.cUserClaimName);
         return PostAsync(nameof(PostMeta_AddUserClaim), claim);
      }

      public Task PostMeta_RemoveUserClaim(UserClaimPayload claim) =>
         PostAsync(nameof(PostMeta_RemoveUserClaim), claim);

      public Task<ClaimAction[]> GetMeta_UserRoleClaims(string cUserId) =>
         GetAsync<ClaimAction[]>(nameof(GetMeta_UserRoleClaims), cUserId);

      public Task<RoleCounter[]> GetMeta_RoleCounters() =>
         GetAsync<RoleCounter[]>(nameof(GetMeta_RoleCounters));

      public Task<ClaimUsage[]> GetMeta_ClaimUsage() =>
         GetAsync<ClaimUsage[]>(nameof(GetMeta_ClaimUsage));

      #endregion

      #region Helpers

      // Satu tempat yang memutuskan sebuah kunci hak boleh dikirim atau tidak, dipakai baik oleh
      // pemberian langsung maupun oleh hak milik role. Dulu ini dikerjakan server; ia dipindah ke
      // sini karena hanya sisi ini yang mengenal katalog seutuhnya. Server hanya tahu hak yang
      // dideklarasikan module, sedangkan AllClaims di sini adalah gabungannya dengan hak milik layar
      // bawaan client - hak yang sah, yang tidak dimiliki module mana pun, dan yang akan ditolak
      // server kalau server yang memeriksanya.
      //
      // Diperiksa tetap perlu: dibiarkan lewat, satu salah ketik akan diam-diam menjadi hak yang
      // tidak pernah menyala dan tidak pernah kelihatan salah.
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