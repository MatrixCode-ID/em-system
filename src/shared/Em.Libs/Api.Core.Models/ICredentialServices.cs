using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Users, roles, claims, sign-in and sessions.</summary>
   public interface ICredentialServices : IServices
   {
      #region Tables

      #region ta_User

      /// <summary>Gets one <c>ta_User</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<ta_User?> GetTa_User_ById(string cUserId);
      /// <summary>Gets every <c>ta_User</c> row.</summary>
      Task<ta_User[]> GetTa_Users();
      /// <summary>Counts the <c>ta_User</c> rows.</summary>
      Task<int> GetTa_Users_Count();
      /// <summary>Gets the <c>ta_User</c> rows of the given contact.</summary>
      Task<ta_User[]> GetTa_Users_ByContactId(string cContactId);
      /// <summary>Gets one page of <c>ta_User</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<ta_User[]> GetTa_Users_InPage(int page, int pageSize);
      /// <summary>Creates a user together with its contact, address, comm, and credential rows.</summary>
      Task PostTa_User_New(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential> data);
      /// <summary>Creates several users together with their related rows.</summary>
      Task PostTa_User_NewBatch(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>[] datas);
      /// <summary>Updates one <c>ta_User</c> row.</summary>
      Task PostTa_User_Update(ta_User data);
      /// <summary>Updates several <c>ta_User</c> rows.</summary>
      Task PostTa_User_UpdateBatch(ta_User[] datas);
      /// <summary>Deletes one <c>ta_User</c> row.</summary>
      Task PostTa_User_Delete(ta_User data);
      /// <summary>Deletes several <c>ta_User</c> rows.</summary>
      Task PostTa_User_DeleteBatch(ta_User[] datas);

      #endregion

      #region ta_Role

      /// <summary>Gets one <c>ta_Role</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<ta_Role?> GetTa_Role_ById(string cRoleId);
      /// <summary>Gets every <c>ta_Role</c> row.</summary>
      Task<ta_Role[]> GetTa_Roles();
      /// <summary>Counts the <c>ta_Role</c> rows.</summary>
      Task<int> GetTa_Roles_Count();
      /// <summary>Gets one page of <c>ta_Role</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<ta_Role[]> GetTa_Roles_InPage(int page, int pageSize);
      /// <summary>Inserts one <c>ta_Role</c> row.</summary>
      Task PostTa_Role_New(ta_Role data);
      /// <summary>Inserts several <c>ta_Role</c> rows.</summary>
      Task PostTa_Role_NewBatch(ta_Role[] datas);
      /// <summary>Updates one <c>ta_Role</c> row.</summary>
      Task PostTa_Role_Update(ta_Role data);
      /// <summary>Updates several <c>ta_Role</c> rows.</summary>
      Task PostTa_Role_UpdateBatch(ta_Role[] datas);
      /// <summary>Deletes one <c>ta_Role</c> row.</summary>
      Task PostTa_Role_Delete(ta_Role data);
      /// <summary>Deletes several <c>ta_Role</c> rows.</summary>
      Task PostTa_Role_DeleteBatch(ta_Role[] datas);

      #endregion

      #region ta_RoleClaim

      /// <summary>Gets the <c>ta_RoleClaim</c> rows of the given role.</summary>
      Task<ta_RoleClaim[]> GetTa_RoleClaims_ByRoleId(string cRoleId);
      /// <summary>Inserts one <c>ta_RoleClaim</c> row.</summary>
      Task PostTa_RoleClaim_New(ta_RoleClaim data);
      /// <summary>Inserts several <c>ta_RoleClaim</c> rows.</summary>
      Task PostTa_RoleClaim_NewBatch(ta_RoleClaim[] datas);
      /// <summary>Deletes one <c>ta_RoleClaim</c> row.</summary>
      Task PostTa_RoleClaim_Delete(ta_RoleClaim data);
      /// <summary>Deletes several <c>ta_RoleClaim</c> rows.</summary>
      Task PostTa_RoleClaim_DeleteBatch(ta_RoleClaim[] datas);

      #endregion

      #region ta_UserRole

      /// <summary>Gets the <c>ta_UserRole</c> rows of the given role.</summary>
      Task<ta_UserRole[]> GetTa_UserRoles_ByRoleId(string cRoleId);
      /// <summary>Gets the <c>ta_UserRole</c> rows of the given user.</summary>
      Task<ta_UserRole[]> GetTa_UserRoles_ByUserId(string cUserId);
      /// <summary>Counts the <c>ta_UserRole</c> rows.</summary>
      Task<int> GetTa_UserRoles_Count();
      /// <summary>Inserts one <c>ta_UserRole</c> row.</summary>
      Task PostTa_UserRole_New(ta_UserRole data);
      /// <summary>Inserts several <c>ta_UserRole</c> rows.</summary>
      Task PostTa_UserRole_NewBatch(ta_UserRole[] datas);
      /// <summary>Updates one <c>ta_UserRole</c> row.</summary>
      Task PostTa_UserRole_Update(ta_UserRole data);
      /// <summary>Deletes one <c>ta_UserRole</c> row.</summary>
      Task PostTa_UserRole_Delete(ta_UserRole data);
      /// <summary>Deletes several <c>ta_UserRole</c> rows.</summary>
      Task PostTa_UserRole_DeleteBatch(ta_UserRole[] datas);

      #endregion

      #endregion

      #region Views

      #region vi_User
      /// <summary>Gets one <c>vi_User</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<vi_User?> GetVi_User_ById(string cUserId);
      /// <summary>Gets one <c>vi_User</c> row by account name; <c>null</c> when it does not exist.</summary>
      Task<vi_User?> GetVi_User_ByAccount(string cUserAccount);
      /// <summary>Gets every <c>vi_User</c> row.</summary>
      Task<vi_User[]> GetVi_Users();
      /// <summary>Gets one page of <c>vi_User</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<vi_User[]> GetVi_Users_InPage(int page, int pageSize);
      /// <summary>Gets the <c>vi_User</c> rows of the given contact.</summary>
      Task<vi_User[]> GetVi_Users_ByContactId(string cContactId);
      /// <summary>Gets the <c>vi_User</c> rows of the given role.</summary>
      Task<vi_User[]> GetVi_Users_ByRoleId(string cRoleId);

      #endregion

      #region vi_Role

      /// <summary>Gets one <c>vi_Role</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<vi_Role?> GetVi_Role_ById(string cRoleId);
      /// <summary>Gets every <c>vi_Role</c> row.</summary>
      Task<vi_Role[]> GetVi_Roles();
      /// <summary>Gets one page of <c>vi_Role</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<vi_Role[]> GetVi_Roles_InPage(int page, int pageSize);

      #endregion

      #endregion

      #region Meta's

      /// <summary>Signs in with an account name and password and returns the access and refresh tokens.</summary>
      Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password);

      /// <summary>Exchanges a refresh token for a new token pair.</summary>
      Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken);

      /// <summary>Ends the caller's current session.</summary>
      Task PostMeta_SignOut();

      /// <summary>Ends every session of the caller.</summary>
      Task PostMeta_SignOutAll();

      /// <summary>Changes the caller's own password; <paramref name="oldPassword"/> must be correct.</summary>
      Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword);

      /// <summary>Sets a new password for a user (administrators).</summary>
      Task PostMeta_ResetPassword(string cUserId, string newPassword);

      /// <summary>Sessions of a user, without refresh token hashes. Only the user themselves or an administrator.</summary>
      Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId);

      /// <summary>Sets a new password for the built-in administrator account and ends all its sessions.</summary>
      Task PostMeta_ResetAdminPassword(string newPassword);

      /// <summary>Changes the built-in administrator password; <paramref name="oldPassword"/> must be correct.</summary>
      Task PostMeta_ChangeAdminPassword(string oldPassword, string newPassword);

      /// <summary>Every claim action declared on the server.</summary>
      Task<ClaimAction[]> GetMeta_AllClaimActions();

      /// <summary>Claims granted directly to a user and valid now. Only the user themselves or an administrator.</summary>
      Task<ClaimAction[]> GetMeta_UserClaims(string cUserId);

      /// <summary>Grants a claim directly to a user.</summary>
      Task PostMeta_AddUserClaim(UserClaimPayload claim);

      /// <summary>Removes a claim granted directly to a user.</summary>
      Task PostMeta_RemoveUserClaim(UserClaimPayload claim);

      /// <summary>Claims a user receives through roles valid now. Only the user themselves or an administrator.</summary>
      Task<ClaimAction[]> GetMeta_UserRoleClaims(string cUserId);

      /// <summary>Member and claim counts per role.</summary>
      Task<RoleCounter[]> GetMeta_RoleCounters();

      /// <summary>Number of roles holding each claim.</summary>
      Task<ClaimUsage[]> GetMeta_ClaimUsage();

      #endregion
   }
}
