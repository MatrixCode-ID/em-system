using Em.Shared;

namespace Em.Api.Core.Models
{
   public interface ICredentialServices : IServices
   {
      #region Tables

      #region ta_User

      Task<ta_User?> GetTa_User_ById(string cUserId);
      Task<ta_User[]> GetTa_Users();
      Task<int> GetTa_Users_Count();
      Task<ta_User[]> GetTa_Users_ByContactId(string cContactId);
      Task<ta_User[]> GetTa_Users_InPage(int page, int pageSize);
      Task PostTa_User_New(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential> data);
      Task PostTa_User_NewBatch(DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>[] datas);
      Task PostTa_User_Update(ta_User data);
      Task PostTa_User_UpdateBatch(ta_User[] datas);
      Task PostTa_User_Delete(ta_User data);
      Task PostTa_User_DeleteBatch(ta_User[] datas);

      #endregion

      #region ta_Role

      Task<ta_Role?> GetTa_Role_ById(string cRoleId);
      Task<ta_Role[]> GetTa_Roles();
      Task<int> GetTa_Roles_Count();
      Task<ta_Role[]> GetTa_Roles_InPage(int page, int pageSize);
      Task PostTa_Role_New(ta_Role data);
      Task PostTa_Role_NewBatch(ta_Role[] datas);
      Task PostTa_Role_Update(ta_Role data);
      Task PostTa_Role_UpdateBatch(ta_Role[] datas);
      Task PostTa_Role_Delete(ta_Role data);
      Task PostTa_Role_DeleteBatch(ta_Role[] datas);

      #endregion

      #region ta_RoleClaim

      Task<ta_RoleClaim[]> GetTa_RoleClaims_ByRoleId(string cRoleId);
      Task PostTa_RoleClaim_New(ta_RoleClaim data);
      Task PostTa_RoleClaim_NewBatch(ta_RoleClaim[] datas);
      Task PostTa_RoleClaim_Delete(ta_RoleClaim data);
      Task PostTa_RoleClaim_DeleteBatch(ta_RoleClaim[] datas);

      #endregion

      #region ta_UserRole

      Task<ta_UserRole[]> GetTa_UserRoles_ByRoleId(string cRoleId);
      Task<ta_UserRole[]> GetTa_UserRoles_ByUserId(string cUserId);
      Task<int> GetTa_UserRoles_Count();
      Task PostTa_UserRole_New(ta_UserRole data);
      Task PostTa_UserRole_NewBatch(ta_UserRole[] datas);
      Task PostTa_UserRole_Update(ta_UserRole data);
      Task PostTa_UserRole_Delete(ta_UserRole data);
      Task PostTa_UserRole_DeleteBatch(ta_UserRole[] datas);

      #endregion

      #endregion

      #region Views

      #region vi_User
      Task<vi_User?> GetVi_User_ById(string cUserId);
      Task<vi_User?> GetVi_User_ByAccount(string cUserAccount);
      Task<vi_User[]> GetVi_Users();
      Task<vi_User[]> GetVi_Users_InPage(int page, int pageSize);
      Task<vi_User[]> GetVi_Users_ByContactId(string cContactId);
      Task<vi_User[]> GetVi_Users_ByRoleId(string cRoleId);

      #endregion

      #region vi_Role

      Task<vi_Role?> GetVi_Role_ById(string cRoleId);
      Task<vi_Role[]> GetVi_Roles();
      Task<vi_Role[]> GetVi_Roles_InPage(int page, int pageSize);

      #endregion

      #endregion

      #region Meta's

      Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password);

      Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken);

      Task PostMeta_SignOut();

      Task PostMeta_SignOutAll();

      Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword);

      Task PostMeta_ResetPassword(string cUserId, string newPassword);

      Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId);

      Task PostMeta_ResetAdminPassword(string newPassword);

      Task PostMeta_ChangeAdminPassword(string oldPassword, string newPassword);
      
      Task<ClaimAction[]> GetMeta_AllClaimActions();
      Task<ClaimAction[]> GetMeta_UserClaims(string cUserId);
      Task PostMeta_AddUserClaim(UserClaimPayload claim);
      Task PostMeta_RemoveUserClaim(UserClaimPayload claim);
      Task<ClaimAction[]> GetMeta_UserRoleClaims(string cUserId);
      Task<RoleCounter[]> GetMeta_RoleCounters();
      Task<ClaimUsage[]> GetMeta_ClaimUsage();

      #endregion
   }
}
