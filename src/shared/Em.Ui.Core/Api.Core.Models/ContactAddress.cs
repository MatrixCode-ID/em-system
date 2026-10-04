using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   public class ContactAddress : UiModel<vi_Address, IContactServices>
   {
      #region Statics
      
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

      public static async Task<ContactAddress[]> GetAddresses_ByContact(Contact contact) {
         try {
            var rows = await contact.Service.GetVi_Addresses_ByContactId(contact.cContactId);
            return [.. rows.Select(r => Build(contact, r))];
         }
         catch (Exception) {
            throw;
         }
      }

      public static async Task<ContactAddress> GetAddress_ByAddressId(IEmApp app, string cAddressId) {
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var dataAddress = await svc.GetVi_Address_ById(cAddressId);
         if (dataAddress == null) throw new InvalidOperationException("Cannot find address with id " + cAddressId);
         return Build(app, dataAddress);
      }

      public static ContactAddress Build(Contact contact, vi_Address address) => Build(contact.App, address);

      /// <summary>
      /// Membangun model alamat langsung dari aplikasi tempat ia hidup, tanpa perlu model kontak
      /// induknya. Dipakai kalau yang tersedia hanya baris alamatnya sendiri - alamat menyimpan
      /// kontaknya sebagai kolom biasa, jadi mengambil model kontak lebih dulu hanya untuk membuat
      /// model ini berarti satu panggilan ke server yang hasilnya tidak terpakai.
      /// </summary>
      /// <param name="app">Objek aplikasi, sumber DI container dan waktu server.</param>
      /// <param name="address">Baris alamat yang menjadi isi model.</param>
      public static ContactAddress Build(IEmApp app, vi_Address address) => new(app, address);

      #endregion

      private ContactAddress(IEmApp app, vi_Address address) : base(app, address) { }

      #region MyRegion

      public string cAddressId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      public string cContactId {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public string cAddressName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public AddressState cAddressState {
         get;
         set => SetField(ref field, value);
      }

      public string cAddressLocation {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public string cAddressZip {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      #endregion

      #region Methods

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

      protected override JsonObject BuildJson(JsonObject patch) => patch;

      protected override Task<vi_Address?> FetchAsync() => Service.GetVi_Address_ById(cAddressId);

      protected override Task UpdateAsync(vi_Address entity) => Service.PostTa_Address_Update(entity);

      #endregion
   }
}