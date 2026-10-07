using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>UI model of one address of a contact.</summary>
   public class ContactAddress : UiModel<vi_Address, IContactServices>
   {
      #region Statics
      
      /// <summary>Creates a new address for a contact, identified by its id.</summary>
      public static async Task<ContactAddress> CreateAddress(IEmApp app, string cContactId, string cAddressName,
         string cAddressLocation, string cAddressZip, AddressState cAddressState, object? jsonObject = null) {
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var stamp = await app.GetDateStampAsync();
         var address = new ta_Address {
            cAddressId = $"{Ulid.NewUlid()}",
            cContactId = cContactId,
            cAddressName = cAddressName,
            cAddressState = cAddressState,
            cAddressLocation = cAddressLocation,
            cAddressZip = cAddressZip,
            ustamp = stamp,
            datestamp = stamp,
            json_object = jsonObject != null ? JsonSerializer.Serialize(jsonObject) : null
         };
         await svc.PostTa_Address_New(address);
         return await GetAddress_ByAddressId(app, address.cAddressId);
      }

      /// <summary>Creates a new address for the given contact.</summary>
      public static async Task<ContactAddress> CreateAddress(Contact contact, string cAddressName,
         string cAddressLocation, string cAddressZip, AddressState cAddressState, object? jsonObject = null) {
         var stamp = await contact.App.GetDateStampAsync();
         var address = new ta_Address {
            cAddressId = $"{Ulid.NewUlid()}",
            cContactId = contact.cContactId,
            cAddressName = cAddressName,
            cAddressState = cAddressState,
            cAddressLocation = cAddressLocation,
            cAddressZip = cAddressZip,
            ustamp = stamp,
            datestamp = stamp,
            json_object = jsonObject != null ? JsonSerializer.Serialize(jsonObject) : null
         };
         await contact.Service.PostTa_Address_New(address);
         return await contact.GetAddressAsync(address.cAddressId);
      }

      /// <summary>Gets all addresses of a contact.</summary>
      public static async Task<ContactAddress[]> GetAddresses_ByContact(Contact contact) {
         try {
            var rows = await contact.Service.GetVi_Addresses_ByContactId(contact.cContactId);
            return [.. rows.Select(r => Build(contact, r))];
         }
         catch (Exception) {
            throw;
         }
      }

      /// <summary>Gets one address by its id.</summary>
      public static async Task<ContactAddress> GetAddress_ByAddressId(IEmApp app, string cAddressId) {
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var dataAddress = await svc.GetVi_Address_ById(cAddressId);
         if (dataAddress == null) throw new InvalidOperationException("Cannot find address with id " + cAddressId);
         return Build(app, dataAddress);
      }

      /// <summary>Wraps an address view row in a model, bound to its contact.</summary>
      public static ContactAddress Build(Contact contact, vi_Address address) => Build(contact.App, address);

      /// <summary>
      /// Builds an address model directly from the application it lives in, without needing its parent
      /// contact model. Used when only the address row itself is available - an address keeps its contact as
      /// an ordinary column, so fetching the contact model first just to create this model would be one
      /// server call whose result goes unused.
      /// </summary>
      /// <param name="app">The application object, the source of the DI container and the server time.</param>
      /// <param name="address">The address row that becomes the content of the model.</param>
      public static ContactAddress Build(IEmApp app, vi_Address address) => new(app, address);

      #endregion

      private ContactAddress(IEmApp app, vi_Address address) : base(app, address) { }

      #region MyRegion

      /// <summary>Id of the address.</summary>
      public string cAddressId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Id of the contact.</summary>
      public string cContactId {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Name of the address.</summary>
      public string cAddressName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>State of the address.</summary>
      public AddressState cAddressState {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Location of the address.</summary>
      public string cAddressLocation {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Zip of the address.</summary>
      public string cAddressZip {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      #endregion

      #region Methods

      /// <inheritdoc />
      protected override void WriteTo(vi_Address target) {
         target.cAddressId = cAddressId;
         target.cContactId = cContactId;
         target.cAddressName = cAddressName;
         target.cAddressState = cAddressState;
         target.cAddressLocation = cAddressLocation;
         target.cAddressZip = cAddressZip;
         target.ustamp = ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      /// <inheritdoc />
      protected override void ReadFrom(vi_Address source) {
         cAddressId = source.cAddressId;
         cContactId = source.cContactId;
         cAddressName = source.cAddressName;
         cAddressState = source.cAddressState;
         cAddressLocation = source.cAddressLocation;
         cAddressZip = source.cAddressZip;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override Task<vi_Address?> FetchAsync() => Service.GetVi_Address_ById(cAddressId);

      /// <inheritdoc />
      protected override Task UpdateAsync(vi_Address entity) => Service.PostTa_Address_Update(entity);

      #endregion
   }
}