using System.Text.Json;
using System.Text.Json.Nodes;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>UI model of one communication entry (phone, email, and so on) of a contact.</summary>
   public class ContactCommunication : UiModel<vi_Comm, IContactServices>
   {
      #region Statics

      /// <summary>Creates a new communication entry for a contact.</summary>
      public static async Task<ContactCommunication> CreateComm(Contact contact, CommunationType cCommType,
         ContactCommunicationState cCommState, string cCommValue, string? cCommNote = null, object? jsonObject = null) {
         var stamp = await contact.App.GetDateStampAsync();
         var data = new ta_Comm {
            cCommId = $"{Ulid.NewUlid()}",
            cContactId = contact.cContactId,
            cCommType = cCommType,
            cCommState = cCommState,
            cCommValue = cCommValue,
            cCommNote = cCommNote,
            ustamp = stamp,
            datestamp = stamp,
            json_object = jsonObject != null ? JsonSerializer.Serialize(jsonObject) : null
         };
         await contact.Service.PostTa_Comm_New(data);
         return await GetComm_ByCommIdAsync(contact, data.cCommId);
      }

      /// <summary>Gets one communication entry of a contact by its id.</summary>
      public static async Task<ContactCommunication> GetComm_ByCommIdAsync(Contact contact, string commId) {
         var data = await contact.Service.GetVi_Comm_ById(commId);
         return data == null
            ? throw new InvalidOperationException("Contact Communication not available")
            : Build(contact, data);
      }

      /// <summary>Wraps a communication view row in a model, bound to its contact.</summary>
      public static ContactCommunication Build(Contact contact, vi_Comm comm) => new(contact, comm);

      #endregion

      private ContactCommunication(Contact contact, vi_Comm entity) : base(contact.App, entity) {
         Contact = contact;
      }

      #region Properties

      /// <summary>The contact this communication entry belongs to.</summary>
      public Contact Contact { get; }

      /// <summary>Id of the communication.</summary>
      public string cCommId {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Id of the contact.</summary>
      public string cContactId {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Type of the communication.</summary>
      public CommunationType cCommType {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>State of the communication.</summary>
      public ContactCommunicationState cCommState {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Value of the communication.</summary>
      public string cCommValue {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Note of the communication.</summary>
      public string? cCommNote {
         get;
         set => SetField(ref field, value);
      }

      #endregion

      /// <inheritdoc />
      protected override void WriteTo(vi_Comm target) {
         target.cCommId = cCommId;
         target.cContactId = cContactId;
         target.cCommType = cCommType;
         target.cCommState = cCommState;
         target.cCommValue = cCommValue;
         target.cCommNote = cCommNote;
         target.ustamp = ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      /// <inheritdoc />
      protected override void ReadFrom(vi_Comm source) {
         cCommId = source.cCommId;
         cContactId = source.cContactId;
         cCommType = source.cCommType;
         cCommState = source.cCommState;
         cCommValue = source.cCommValue;
         cCommNote = source.cCommNote;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override Task<vi_Comm?> FetchAsync() => Service.GetVi_Comm_ById(cCommId);

      /// <inheritdoc />
      protected override Task UpdateAsync(vi_Comm entity) => Service.PostTa_Comm_Update(entity);
   }
}