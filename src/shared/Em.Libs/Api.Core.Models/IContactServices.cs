using Em.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>Contacts with their communication channels and addresses.</summary>
   public interface IContactServices : IServices
   {
      #region Tables

      #region ta_Contact

      /// <summary>Gets one <c>ta_Contact</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<ta_Contact?> GetTa_Contact_ById(string cContactId);
      /// <summary>Gets every <c>ta_Contact</c> row.</summary>
      Task<ta_Contact[]> GetTa_Contacts();
      /// <summary>Counts the <c>ta_Contact</c> rows.</summary>
      Task<int> GetTa_Contacts_Count();
      /// <summary>Gets one page of <c>ta_Contact</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<ta_Contact[]> GetTa_Contacts_InPage(int page, int pageSize);
      /// <summary>Inserts one <c>ta_Contact</c> row.</summary>
      Task PostTa_Contact_New(ta_Contact data);
      /// <summary>Inserts several <c>ta_Contact</c> rows.</summary>
      Task PostTa_Contact_NewBatch(ta_Contact[] datas);
      /// <summary>Updates one <c>ta_Contact</c> row.</summary>
      Task PostTa_Contact_Update(ta_Contact data);
      /// <summary>Updates several <c>ta_Contact</c> rows.</summary>
      Task PostTa_Contact_UpdateBatch(ta_Contact[] datas);
      /// <summary>Deletes one <c>ta_Contact</c> row.</summary>
      Task PostTa_Contact_Delete(ta_Contact data);
      /// <summary>Deletes several <c>ta_Contact</c> rows.</summary>
      Task PostTa_Contact_DeleteBatch(ta_Contact[] datas);

      #endregion

      #region ta_Comm

      /// <summary>Gets one <c>ta_Comm</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<ta_Comm?> GetTa_Comm_ById(string cCommId);
      /// <summary>Gets every <c>ta_Comm</c> row.</summary>
      Task<ta_Comm[]> GetTa_Comms();
      /// <summary>Counts the <c>ta_Comm</c> rows.</summary>
      Task<int> GetTa_Comms_Count();
      /// <summary>Gets the <c>ta_Comm</c> rows of the given contact.</summary>
      Task<ta_Comm[]> GetTa_Comms_ByContactId(string cContactId);
      /// <summary>Gets one page of <c>ta_Comm</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<ta_Comm[]> GetTa_Comms_InPage(int page, int pageSize);
      /// <summary>Inserts one <c>ta_Comm</c> row.</summary>
      Task PostTa_Comm_New(ta_Comm data);
      /// <summary>Inserts several <c>ta_Comm</c> rows.</summary>
      Task PostTa_Comm_NewBatch(ta_Comm[] datas);
      /// <summary>Updates one <c>ta_Comm</c> row.</summary>
      Task PostTa_Comm_Update(ta_Comm data);
      /// <summary>Updates several <c>ta_Comm</c> rows.</summary>
      Task PostTa_Comm_UpdateBatch(ta_Comm[] datas);
      /// <summary>Deletes one <c>ta_Comm</c> row.</summary>
      Task PostTa_Comm_Delete(ta_Comm data);
      /// <summary>Deletes several <c>ta_Comm</c> rows.</summary>
      Task PostTa_Comm_DeleteBatch(ta_Comm[] datas);

      #endregion

      #region ta_Address

      /// <summary>Gets one <c>ta_Address</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<ta_Address?> GetTa_Address_ById(string cAddressId);
      /// <summary>Gets every <c>ta_Address</c> row.</summary>
      Task<ta_Address[]> GetTa_Addresses();
      /// <summary>Gets the <c>ta_Address</c> rows of the given contact.</summary>
      Task<ta_Address[]> GetTa_Addresses_ByContactId(string cContactId);
      /// <summary>Counts the <c>ta_Address</c> rows.</summary>
      Task<int> GetTa_Addresses_Count();
      /// <summary>Gets one page of <c>ta_Address</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<ta_Address[]> GetTa_Addresses_InPage(int page, int pageSize);
      /// <summary>Inserts one <c>ta_Address</c> row.</summary>
      Task PostTa_Address_New(ta_Address data);
      /// <summary>Inserts several <c>ta_Address</c> rows.</summary>
      Task PostTa_Address_NewBatch(ta_Address[] datas);
      /// <summary>Updates one <c>ta_Address</c> row.</summary>
      Task PostTa_Address_Update(ta_Address data);
      /// <summary>Updates several <c>ta_Address</c> rows.</summary>
      Task PostTa_Address_UpdateBatch(ta_Address[] datas);
      /// <summary>Deletes one <c>ta_Address</c> row.</summary>
      Task PostTa_Address_Delete(ta_Address data);
      /// <summary>Deletes several <c>ta_Address</c> rows.</summary>
      Task PostTa_Address_DeleteBatch(ta_Address[] datas);

      #endregion

      #endregion

      #region Views

      #region vi_Contact

      /// <summary>Gets one <c>vi_Contact</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<vi_Contact?> GetVi_Contact_ById(string cContactId);
      /// <summary>Gets every <c>vi_Contact</c> row.</summary>
      Task<vi_Contact[]> GetVi_Contacts();
      /// <summary>Gets one page of <c>vi_Contact</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<vi_Contact[]> GetVi_Contacts_InPage(int page, int pageSize);
      /// <summary>Searches contacts by <paramref name="searchType"/>, returning at most <paramref name="maxResults"/> rows.</summary>
      Task<vi_Contact[]> GetVi_Contacts_Search(ContactSearchType searchType, string searchTerm, int maxResults);
      #endregion

      #region vi_Address

      /// <summary>Gets one <c>vi_Address</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<vi_Address?> GetVi_Address_ById(string cAddressId);
      /// <summary>Gets every <c>vi_Address</c> row.</summary>
      Task<vi_Address[]> GetVi_Addresses();
      /// <summary>Gets one page of <c>vi_Address</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<vi_Address[]> GetVi_Addresses_InPage(int page, int pageSize);
      /// <summary>Gets the <c>vi_Address</c> rows of the given contact.</summary>
      Task<vi_Address[]> GetVi_Addresses_ByContactId(string cContactId);

      #endregion

      #region vi_Comm

      /// <summary>Gets one <c>vi_Comm</c> row by its ID; <c>null</c> when it does not exist.</summary>
      Task<vi_Comm?> GetVi_Comm_ById(string cCommId);
      /// <summary>Gets every <c>vi_Comm</c> row.</summary>
      Task<vi_Comm[]> GetVi_Comms();
      /// <summary>Gets one page of <c>vi_Comm</c> rows; <paramref name="page"/> starts at one.</summary>
      Task<vi_Comm[]> GetVi_Comms_InPage(int page, int pageSize);
      /// <summary>Gets the <c>vi_Comm</c> rows of the given contact.</summary>
      Task<vi_Comm[]> GetVi_Comms_ByContactId(string cContactId);

      #endregion

      #endregion
   }
}