using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Maui.Core;

namespace Em.Api.Core
{
   [Module("core.contact")]
   public class ContactService(EmApp emApp) : ServiceMauiBase(emApp), IContactServices
   {
      #region Tables

      #region ta_Contact

      public Task<ta_Contact?> GetTa_Contact_ById(string cContactId) =>
         GetAsync<ta_Contact?>(nameof(GetTa_Contact_ById), cContactId);

      public Task<ta_Contact[]> GetTa_Contacts() => GetAsync<ta_Contact[]>(nameof(GetTa_Contacts));
      public Task<int> GetTa_Contacts_Count() => GetAsync<int>(nameof(GetTa_Contacts_Count));

      public Task<ta_Contact[]> GetTa_Contacts_InPage(int page, int pageSize) =>
         GetAsync<ta_Contact[]>(nameof(GetTa_Contacts_InPage), page, pageSize);

      public Task PostTa_Contact_New(ta_Contact data) => PostAsync(nameof(PostTa_Contact_New), data);
      public Task PostTa_Contact_NewBatch(ta_Contact[] datas) => PostAsync(nameof(PostTa_Contact_NewBatch), (object)datas);
      public Task PostTa_Contact_Update(ta_Contact data) => PostAsync(nameof(PostTa_Contact_Update), data);

      public Task PostTa_Contact_UpdateBatch(ta_Contact[] datas) =>
         PostAsync(nameof(PostTa_Contact_UpdateBatch), (object)datas);

      public Task PostTa_Contact_Delete(ta_Contact data) => PostAsync(nameof(PostTa_Contact_Delete), data);

      public Task PostTa_Contact_DeleteBatch(ta_Contact[] datas) =>
         PostAsync(nameof(PostTa_Contact_DeleteBatch), (object)datas);

      #endregion

      #region ta_Comm

      public Task<ta_Comm?> GetTa_Comm_ById(string cCommId) => GetAsync<ta_Comm?>(nameof(GetTa_Comm_ById), cCommId);
      public Task<ta_Comm[]> GetTa_Comms() => GetAsync<ta_Comm[]>(nameof(GetTa_Comms));
      public Task<int> GetTa_Comms_Count() => GetAsync<int>(nameof(GetTa_Comms_Count));

      public Task<ta_Comm[]> GetTa_Comms_ByContactId(string cContactId) =>
         GetAsync<ta_Comm[]>(nameof(GetTa_Comms_ByContactId), cContactId);

      public Task<ta_Comm[]> GetTa_Comms_InPage(int page, int pageSize) =>
         GetAsync<ta_Comm[]>(nameof(GetTa_Comms_InPage), page, pageSize);

      public Task PostTa_Comm_New(ta_Comm data) => PostAsync(nameof(PostTa_Comm_New), data);
      public Task PostTa_Comm_NewBatch(ta_Comm[] datas) => PostAsync(nameof(PostTa_Comm_NewBatch), (object)datas);
      public Task PostTa_Comm_Update(ta_Comm data) => PostAsync(nameof(PostTa_Comm_Update), data);
      public Task PostTa_Comm_UpdateBatch(ta_Comm[] datas) => PostAsync(nameof(PostTa_Comm_UpdateBatch), (object)datas);
      public Task PostTa_Comm_Delete(ta_Comm data) => PostAsync(nameof(PostTa_Comm_Delete), data);
      public Task PostTa_Comm_DeleteBatch(ta_Comm[] datas) => PostAsync(nameof(PostTa_Comm_DeleteBatch), (object)datas);

      #endregion

      #region ta_Address

      public Task<ta_Address?> GetTa_Address_ById(string cAddressId) =>
         GetAsync<ta_Address?>(nameof(GetTa_Address_ById), cAddressId);

      public Task<ta_Address[]> GetTa_Addresses() => GetAsync<ta_Address[]>(nameof(GetTa_Addresses));

      public Task<ta_Address[]> GetTa_Addresses_ByContactId(string cContactId) =>
         GetAsync<ta_Address[]>(nameof(GetTa_Addresses_ByContactId), cContactId);

      public Task<int> GetTa_Addresses_Count() => GetAsync<int>(nameof(GetTa_Addresses_Count));

      public Task<ta_Address[]> GetTa_Addresses_InPage(int page, int pageSize) =>
         GetAsync<ta_Address[]>(nameof(GetTa_Addresses_InPage), page, pageSize);

      public Task PostTa_Address_New(ta_Address data) => PostAsync(nameof(PostTa_Address_New), data);
      public Task PostTa_Address_NewBatch(ta_Address[] datas) => PostAsync(nameof(PostTa_Address_NewBatch), (object)datas);
      public Task PostTa_Address_Update(ta_Address data) => PostAsync(nameof(PostTa_Address_Update), data);

      public Task PostTa_Address_UpdateBatch(ta_Address[] datas) =>
         PostAsync(nameof(PostTa_Address_UpdateBatch), (object)datas);

      public Task PostTa_Address_Delete(ta_Address data) => PostAsync(nameof(PostTa_Address_Delete), data);

      public Task PostTa_Address_DeleteBatch(ta_Address[] datas) =>
         PostAsync(nameof(PostTa_Address_DeleteBatch), (object)datas);

      #endregion

      #endregion

      #region Views

      #region vi_Contact

      public Task<vi_Contact?> GetVi_Contact_ById(string cContactId) =>
         GetAsync<vi_Contact?>(nameof(GetVi_Contact_ById), cContactId);

      public Task<vi_Contact[]> GetVi_Contacts() => GetAsync<vi_Contact[]>(nameof(GetVi_Contacts));

      public Task<vi_Contact[]> GetVi_Contacts_InPage(int page, int pageSize) =>
         GetAsync<vi_Contact[]>(nameof(GetVi_Contacts_InPage), page, pageSize);

      public Task<vi_Contact[]> GetVi_Contacts_Search(ContactSearchType searchType, string searchTerm,
         int maxResults = 200) =>
         GetAsync<vi_Contact[]>(nameof(GetVi_Contacts_Search), searchType, searchTerm, maxResults);

      #endregion

      #region vi_Address

      public Task<vi_Address?> GetVi_Address_ById(string cAddressId) =>
         GetAsync<vi_Address?>(nameof(GetVi_Address_ById), cAddressId);

      public Task<vi_Address[]> GetVi_Addresses() => GetAsync<vi_Address[]>(nameof(GetVi_Addresses));

      public Task<vi_Address[]> GetVi_Addresses_InPage(int page, int pageSize) =>
         GetAsync<vi_Address[]>(nameof(GetVi_Addresses_InPage), page, pageSize);

      public Task<vi_Address[]> GetVi_Addresses_ByContactId(string cContactId) =>
         GetAsync<vi_Address[]>(nameof(GetVi_Addresses_ByContactId), cContactId);

      #endregion

      #region vi_Comms

      public Task<vi_Comm?> GetVi_Comm_ById(string cCommId) => GetAsync<vi_Comm?>(nameof(GetVi_Comm_ById), cCommId);
      public Task<vi_Comm[]> GetVi_Comms() => GetAsync<vi_Comm[]>(nameof(GetVi_Comms));
      public Task<vi_Comm[]> GetVi_Comms_InPage(int page, int pageSize) => GetAsync<vi_Comm[]>(nameof(GetVi_Comms_InPage), page, pageSize);
      public Task<vi_Comm[]> GetVi_Comms_ByContactId(string cContactId) => GetAsync<vi_Comm[]>(nameof(GetVi_Comms_ByContactId), cContactId);

      #endregion
      
      #endregion
   }
}