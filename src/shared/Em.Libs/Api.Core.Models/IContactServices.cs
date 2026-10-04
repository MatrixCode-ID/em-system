using Em.Shared;

namespace Em.Api.Core.Models
{
   public interface IContactServices : IServices
   {
      #region Tables

      #region ta_Contact

      Task<ta_Contact?> GetTa_Contact_ById(string cContactId);
      Task<ta_Contact[]> GetTa_Contacts();
      Task<int> GetTa_Contacts_Count();
      Task<ta_Contact[]> GetTa_Contacts_InPage(int page, int pageSize);
      Task PostTa_Contact_New(ta_Contact data);
      Task PostTa_Contact_NewBatch(ta_Contact[] datas);
      Task PostTa_Contact_Update(ta_Contact data);
      Task PostTa_Contact_UpdateBatch(ta_Contact[] datas);
      Task PostTa_Contact_Delete(ta_Contact data);
      Task PostTa_Contact_DeleteBatch(ta_Contact[] datas);

      #endregion

      #region ta_Comm

      Task<ta_Comm?> GetTa_Comm_ById(string cCommId);
      Task<ta_Comm[]> GetTa_Comms();
      Task<int> GetTa_Comms_Count();
      Task<ta_Comm[]> GetTa_Comms_ByContactId(string cContactId);
      Task<ta_Comm[]> GetTa_Comms_InPage(int page, int pageSize);
      Task PostTa_Comm_New(ta_Comm data);
      Task PostTa_Comm_NewBatch(ta_Comm[] datas);
      Task PostTa_Comm_Update(ta_Comm data);
      Task PostTa_Comm_UpdateBatch(ta_Comm[] datas);
      Task PostTa_Comm_Delete(ta_Comm data);
      Task PostTa_Comm_DeleteBatch(ta_Comm[] datas);

      #endregion

      #region ta_Address

      Task<ta_Address?> GetTa_Address_ById(string cAddressId);
      Task<ta_Address[]> GetTa_Addresses();
      Task<ta_Address[]> GetTa_Addresses_ByContactId(string cContactId);
      Task<int> GetTa_Addresses_Count();
      Task<ta_Address[]> GetTa_Addresses_InPage(int page, int pageSize);
      Task PostTa_Address_New(ta_Address data);
      Task PostTa_Address_NewBatch(ta_Address[] datas);
      Task PostTa_Address_Update(ta_Address data);
      Task PostTa_Address_UpdateBatch(ta_Address[] datas);
      Task PostTa_Address_Delete(ta_Address data);
      Task PostTa_Address_DeleteBatch(ta_Address[] datas);

      #endregion

      #endregion

      #region Views

      #region vi_Contact

      Task<vi_Contact?> GetVi_Contact_ById(string cContactId);
      Task<vi_Contact[]> GetVi_Contacts();
      Task<vi_Contact[]> GetVi_Contacts_InPage(int page, int pageSize);
      Task<vi_Contact[]> GetVi_Contacts_Search(ContactSearchType searchType, string searchTerm, int maxResults);
      #endregion

      #region vi_Address

      Task<vi_Address?> GetVi_Address_ById(string cAddressId);
      Task<vi_Address[]> GetVi_Addresses();
      Task<vi_Address[]> GetVi_Addresses_InPage(int page, int pageSize);
      Task<vi_Address[]> GetVi_Addresses_ByContactId(string cContactId);

      #endregion

      #region vi_Comm

      Task<vi_Comm?> GetVi_Comm_ById(string cCommId);
      Task<vi_Comm[]> GetVi_Comms();
      Task<vi_Comm[]> GetVi_Comms_InPage(int page, int pageSize);
      Task<vi_Comm[]> GetVi_Comms_ByContactId(string cContactId);

      #endregion

      #endregion
   }
}