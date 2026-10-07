using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core
{
   /// <summary>Client-side implementation of the contact, communication, and address actions.</summary>
   [Module("core.contact")]
   public class ContactService(EmApp emApp) : ServiceWpfBase(emApp), IContactServices
   {
      #region Tables

      #region ta_Contact

      /// <inheritdoc />
      public Task<ta_Contact?> GetTa_Contact_ById(string cContactId) =>
         GetAsync<ta_Contact?>(nameof(GetTa_Contact_ById), cContactId);

      /// <inheritdoc />
      public Task<ta_Contact[]> GetTa_Contacts() => GetAsync<ta_Contact[]>(nameof(GetTa_Contacts));
      /// <inheritdoc />
      public Task<int> GetTa_Contacts_Count() => GetAsync<int>(nameof(GetTa_Contacts_Count));

      /// <inheritdoc />
      public Task<ta_Contact[]> GetTa_Contacts_InPage(int page, int pageSize) =>
         GetAsync<ta_Contact[]>(nameof(GetTa_Contacts_InPage), page, pageSize);

      /// <inheritdoc />
      public Task PostTa_Contact_New(ta_Contact data) => PostAsync(nameof(PostTa_Contact_New), data);
      /// <inheritdoc />
      public Task PostTa_Contact_NewBatch(ta_Contact[] datas) => PostAsync(nameof(PostTa_Contact_NewBatch), (object)datas);
      /// <inheritdoc />
      public Task PostTa_Contact_Update(ta_Contact data) => PostAsync(nameof(PostTa_Contact_Update), data);

      /// <inheritdoc />
      public Task PostTa_Contact_UpdateBatch(ta_Contact[] datas) =>
         PostAsync(nameof(PostTa_Contact_UpdateBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_Contact_Delete(ta_Contact data) => PostAsync(nameof(PostTa_Contact_Delete), data);

      /// <inheritdoc />
      public Task PostTa_Contact_DeleteBatch(ta_Contact[] datas) =>
         PostAsync(nameof(PostTa_Contact_DeleteBatch), (object)datas);

      #endregion

      #region ta_Comm

      /// <inheritdoc />
      public Task<ta_Comm?> GetTa_Comm_ById(string cCommId) => GetAsync<ta_Comm?>(nameof(GetTa_Comm_ById), cCommId);
      /// <inheritdoc />
      public Task<ta_Comm[]> GetTa_Comms() => GetAsync<ta_Comm[]>(nameof(GetTa_Comms));
      /// <inheritdoc />
      public Task<int> GetTa_Comms_Count() => GetAsync<int>(nameof(GetTa_Comms_Count));

      /// <inheritdoc />
      public Task<ta_Comm[]> GetTa_Comms_ByContactId(string cContactId) =>
         GetAsync<ta_Comm[]>(nameof(GetTa_Comms_ByContactId), cContactId);

      /// <inheritdoc />
      public Task<ta_Comm[]> GetTa_Comms_InPage(int page, int pageSize) =>
         GetAsync<ta_Comm[]>(nameof(GetTa_Comms_InPage), page, pageSize);

      /// <inheritdoc />
      public Task PostTa_Comm_New(ta_Comm data) => PostAsync(nameof(PostTa_Comm_New), data);
      /// <inheritdoc />
      public Task PostTa_Comm_NewBatch(ta_Comm[] datas) => PostAsync(nameof(PostTa_Comm_NewBatch), (object)datas);
      /// <inheritdoc />
      public Task PostTa_Comm_Update(ta_Comm data) => PostAsync(nameof(PostTa_Comm_Update), data);
      /// <inheritdoc />
      public Task PostTa_Comm_UpdateBatch(ta_Comm[] datas) => PostAsync(nameof(PostTa_Comm_UpdateBatch), (object)datas);
      /// <inheritdoc />
      public Task PostTa_Comm_Delete(ta_Comm data) => PostAsync(nameof(PostTa_Comm_Delete), data);
      /// <inheritdoc />
      public Task PostTa_Comm_DeleteBatch(ta_Comm[] datas) => PostAsync(nameof(PostTa_Comm_DeleteBatch), (object)datas);

      #endregion

      #region ta_Address

      /// <inheritdoc />
      public Task<ta_Address?> GetTa_Address_ById(string cAddressId) =>
         GetAsync<ta_Address?>(nameof(GetTa_Address_ById), cAddressId);

      /// <inheritdoc />
      public Task<ta_Address[]> GetTa_Addresses() => GetAsync<ta_Address[]>(nameof(GetTa_Addresses));

      /// <inheritdoc />
      public Task<ta_Address[]> GetTa_Addresses_ByContactId(string cContactId) =>
         GetAsync<ta_Address[]>(nameof(GetTa_Addresses_ByContactId), cContactId);

      /// <inheritdoc />
      public Task<int> GetTa_Addresses_Count() => GetAsync<int>(nameof(GetTa_Addresses_Count));

      /// <inheritdoc />
      public Task<ta_Address[]> GetTa_Addresses_InPage(int page, int pageSize) =>
         GetAsync<ta_Address[]>(nameof(GetTa_Addresses_InPage), page, pageSize);

      /// <inheritdoc />
      public Task PostTa_Address_New(ta_Address data) => PostAsync(nameof(PostTa_Address_New), data);
      /// <inheritdoc />
      public Task PostTa_Address_NewBatch(ta_Address[] datas) => PostAsync(nameof(PostTa_Address_NewBatch), (object)datas);
      /// <inheritdoc />
      public Task PostTa_Address_Update(ta_Address data) => PostAsync(nameof(PostTa_Address_Update), data);

      /// <inheritdoc />
      public Task PostTa_Address_UpdateBatch(ta_Address[] datas) =>
         PostAsync(nameof(PostTa_Address_UpdateBatch), (object)datas);

      /// <inheritdoc />
      public Task PostTa_Address_Delete(ta_Address data) => PostAsync(nameof(PostTa_Address_Delete), data);

      /// <inheritdoc />
      public Task PostTa_Address_DeleteBatch(ta_Address[] datas) =>
         PostAsync(nameof(PostTa_Address_DeleteBatch), (object)datas);

      #endregion

      #endregion

      #region Views

      #region vi_Contact

      /// <inheritdoc />
      public Task<vi_Contact?> GetVi_Contact_ById(string cContactId) =>
         GetAsync<vi_Contact?>(nameof(GetVi_Contact_ById), cContactId);

      /// <inheritdoc />
      public Task<vi_Contact[]> GetVi_Contacts() => GetAsync<vi_Contact[]>(nameof(GetVi_Contacts));

      /// <inheritdoc />
      public Task<vi_Contact[]> GetVi_Contacts_InPage(int page, int pageSize) =>
         GetAsync<vi_Contact[]>(nameof(GetVi_Contacts_InPage), page, pageSize);

      /// <inheritdoc />
      public Task<vi_Contact[]> GetVi_Contacts_Search(ContactSearchType searchType, string searchTerm,
         int maxResults = 200) =>
         GetAsync<vi_Contact[]>(nameof(GetVi_Contacts_Search), searchType, searchTerm, maxResults);

      #endregion

      #region vi_Address

      /// <inheritdoc />
      public Task<vi_Address?> GetVi_Address_ById(string cAddressId) =>
         GetAsync<vi_Address?>(nameof(GetVi_Address_ById), cAddressId);

      /// <inheritdoc />
      public Task<vi_Address[]> GetVi_Addresses() => GetAsync<vi_Address[]>(nameof(GetVi_Addresses));

      /// <inheritdoc />
      public Task<vi_Address[]> GetVi_Addresses_InPage(int page, int pageSize) =>
         GetAsync<vi_Address[]>(nameof(GetVi_Addresses_InPage), page, pageSize);

      /// <inheritdoc />
      public Task<vi_Address[]> GetVi_Addresses_ByContactId(string cContactId) =>
         GetAsync<vi_Address[]>(nameof(GetVi_Addresses_ByContactId), cContactId);

      #endregion

      #region vi_Comms

      /// <inheritdoc />
      public Task<vi_Comm?> GetVi_Comm_ById(string cCommId) => GetAsync<vi_Comm?>(nameof(GetVi_Comm_ById), cCommId);
      /// <inheritdoc />
      public Task<vi_Comm[]> GetVi_Comms() => GetAsync<vi_Comm[]>(nameof(GetVi_Comms));
      /// <inheritdoc />
      public Task<vi_Comm[]> GetVi_Comms_InPage(int page, int pageSize) => GetAsync<vi_Comm[]>(nameof(GetVi_Comms_InPage), page, pageSize);
      /// <inheritdoc />
      public Task<vi_Comm[]> GetVi_Comms_ByContactId(string cContactId) => GetAsync<vi_Comm[]>(nameof(GetVi_Comms_ByContactId), cContactId);

      #endregion
      
      #endregion
   }
}