using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// UI model of one contact row together with its default address and communication contact, read from
   /// the combined contact view. The combined columns are read-only; only the contact's own columns can be
   /// changed and saved.
   /// </summary>
   public class Contact : UiModel<vi_Contact, IContactServices>
   {
      #region Statics

      /// <summary>Creates a new contact on the server and returns its model.</summary>
      public static async Task<Contact> NewContactAsync(IEmApp app, string fullName,
         ContactType contactType, ContactState contactState = ContactState.Active,
         string? note = null, string? jsonObject = null) {
         var stamp = await app.GetDateStampAsync();
         var dbData = new ta_Contact {
            cContactId = $"{Ulid.NewUlid()}",
            cContactFullName = fullName,
            cContactState = contactState,
            cContactType = contactType,
            cContactNote = note,
            ustamp = stamp,
            datestamp = stamp,
            json_object = jsonObject
         };
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         await svc.PostTa_Contact_New(dbData);
         var result = await GetContact_ByIdAsync(app, dbData.cContactId);
         return result!;
      }

      /// <summary>Gets one contact by its id, or <c>null</c> when it does not exist.</summary>
      public static async Task<Contact?> GetContact_ByIdAsync(IEmApp app, string cContactId) {
         EnsureNotSystemContact(cContactId);
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var data = await svc.GetVi_Contact_ById(cContactId);
         return data != null ? Build(app, data) : null;
      }

      /// <summary>Wraps a contact view row in a model.</summary>
      public static Contact Build(IEmApp app, vi_Contact data) => new(app, data);

      /// <summary>Gets one page of contacts.</summary>
      public static async Task<Contact[]> GetContactsInPageAsync(IEmApp app, int page, int pageSize) {
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var rows = await svc.GetVi_Contacts_InPage(page, pageSize);
         return [.. rows.Select(r => Build(app, r))];
      }

      /// <summary>
      /// Searches contacts from free text and returns at most <paramref name="maxResults"/> rows, ordered from
      /// the closest match to the searched text. The text is split by spaces and every piece must be found, so
      /// "budi jakarta" only matches contacts that contain both words. Filtering, ordering, and limiting the
      /// number of rows are all done on the server side, so only the rows that are really used are sent to the
      /// application.
      /// </summary>
      /// <param name="app">The application the contact service is taken from.</param>
      /// <param name="searchType">Decides which columns are searched: all text columns of the contact, only the
      /// full name, or only the address location.</param>
      /// <param name="searchTerm">The text to search for; empty or only spaces immediately yields an empty array
      /// without calling the server.</param>
      /// <param name="maxResults">Limit on the number of rows requested. A value below 1 is replaced by 200 on the server.</param>
      public static async Task<Contact[]> SearchContactsAsync(IEmApp app, ContactSearchType searchType,
         string searchTerm, int maxResults = 200) {
         if (string.IsNullOrWhiteSpace(searchTerm)) return [];
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var rows = await svc.GetVi_Contacts_Search(searchType, searchTerm, maxResults);
         return [.. rows.Select(r => Build(app, r))];
      }

      #endregion

      private Contact(IEmApp app, vi_Contact data) : base(app, data) { }

      #region Properties

      /// <summary>Id of the contact.</summary>
      public string cContactId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Full name of the contact.</summary>
      public string cContactFullName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>State of the contact.</summary>
      public ContactState cContactState {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Type of the contact.</summary>
      public ContactType cContactType {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Note of the contact.</summary>
      public string? cContactNote {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Default address of the contact.</summary>
      public string? cContactDefaultAddress_cAddressId {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Default comm of the contact.</summary>
      public string? cContactDefaultComm_cCommId {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Name of the address.</summary>
      public string? cAddressName {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Location of the address.</summary>
      public string? cAddressLocation {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Zip of the address.</summary>
      public string? cAddressZip {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Type of the communication.</summary>
      public int? cCommType {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>State of the communication.</summary>
      public ContactCommunicationState? cCommState {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Value of the communication.</summary>
      public string? cCommValue {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Note of the communication.</summary>
      public string? cCommNote {
         get;
         private set => SetField(ref field, value);
      }
      
      #endregion

      #region Methods

      /// <inheritdoc />
      protected override void ReadFrom(vi_Contact source) {
         cContactId = source.cContactId;
         cContactFullName = source.cContactFullName;
         cContactState = source.cContactState;
         cContactType = source.cContactType;
         cContactNote = source.cContactNote;
         cContactDefaultAddress_cAddressId = source.cContactDefaultAddress_cAddressId;
         cContactDefaultComm_cCommId = source.cContactDefaultComm_cCommId;
         cAddressName = source.cAddressName;
         cAddressLocation = source.cAddressLocation;
         cAddressZip = source.cAddressZip;
         cCommType = source.cCommType;
         cCommState = source.cCommState;
         cCommValue = source.cCommValue;
         cCommNote = source.cCommNote;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      /// <inheritdoc />
      protected override void WriteTo(vi_Contact target) {
         target.cContactId = cContactId;
         target.cContactFullName = cContactFullName;
         target.cContactState = cContactState;
         target.cContactType = cContactType;
         target.cContactNote = cContactNote;
         target.cContactDefaultAddress_cAddressId = cContactDefaultAddress_cAddressId;
         target.cContactDefaultComm_cCommId = cContactDefaultComm_cCommId;
         target.cAddressName = cAddressName;
         target.cAddressLocation = cAddressLocation;
         target.cAddressZip = cAddressZip;
         target.cCommType = cCommType;
         target.cCommState = cCommState;
         target.cCommValue = cCommValue;
         target.cCommNote = cCommNote;
         target.ustamp = ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      // The debugger account and the administrator account carry a contact id of their own, but no
      // contact was ever stored for them: they stand in for a signed-in user rather than describing
      // a person. Asking for that contact is a mistake either way, so it is refused here instead of
      // coming back empty and reading like a contact somebody deleted.
      private static void EnsureNotSystemContact(string cContactId) {
         if (cContactId is Defaults.DebuggerUserId or Defaults.AdminUserId) {
            throw new InvalidOperationException(
               $"Contact '{cContactId}' belongs to a system account and has no record of its own.");
         }
      }

      /// <inheritdoc />
      protected override Task<vi_Contact?> FetchAsync() {
         EnsureNotSystemContact(cContactId);
         return Service.GetVi_Contact_ById(cContactId);
      }

      /// <inheritdoc />
      protected override Task UpdateAsync(vi_Contact entity) {
         EnsureNotSystemContact(entity.cContactId);
         return Service.PostTa_Contact_Update(entity);
      }

      #endregion
   }
}