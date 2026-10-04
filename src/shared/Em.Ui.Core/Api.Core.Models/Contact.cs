using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// Model UI untuk satu baris kontak beserta alamat dan kontak-komunikasi default-nya,
   /// dibaca dari tampilan gabungan kontak. Kolom hasil gabungan bersifat baca-saja; yang bisa
   /// diubah dan disimpan hanyalah kolom milik kontak itu sendiri.
   /// </summary>
   public class Contact : UiModel<vi_Contact, IContactServices>
   {
      #region Statics

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

      public static async Task<Contact?> GetContact_ByIdAsync(IEmApp app, string cContactId) {
         EnsureNotSystemContact(cContactId);
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var data = await svc.GetVi_Contact_ById(cContactId);
         return data != null ? Build(app, data) : null;
      }

      public static Contact Build(IEmApp app, vi_Contact data) => new(app, data);

      public static async Task<Contact[]> GetContactsInPageAsync(IEmApp app, int page, int pageSize) {
         var svc = app.ServiceProvider.GetRequiredService<IContactServices>();
         var rows = await svc.GetVi_Contacts_InPage(page, pageSize);
         return [.. rows.Select(r => Build(app, r))];
      }

      /// <summary>
      /// Mencari kontak dari teks bebas dan mengembalikan paling banyak <paramref name="maxResults"/> baris,
      /// terurut dari yang paling mirip dengan teks yang dicari. Teks dipecah per spasi dan setiap potongnya
      /// wajib ketemu, jadi "budi jakarta" hanya mencocokkan kontak yang memuat kedua kata itu. Penyaringan,
      /// pengurutan, dan pembatasan jumlah baris semuanya dikerjakan di sisi server, jadi yang dikirim ke
      /// aplikasi hanya baris yang benar-benar dipakai.
      /// </summary>
      /// <param name="app">Aplikasi tempat layanan kontak diambil.</param>
      /// <param name="searchType">Menentukan kolom mana yang ikut dicari: seluruh kolom teks kontak, hanya nama
      /// lengkap, atau hanya lokasi alamat.</param>
      /// <param name="searchTerm">Teks yang dicari; kosong atau hanya spasi langsung menghasilkan array kosong
      /// tanpa memanggil server.</param>
      /// <param name="maxResults">Batas jumlah baris yang diminta. Nilai di bawah 1 diganti 200 oleh server.</param>
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

      public string cContactId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      public string cContactFullName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public ContactState cContactState {
         get;
         set => SetField(ref field, value);
      }

      public ContactType cContactType {
         get;
         set => SetField(ref field, value);
      }

      public string? cContactNote {
         get;
         set => SetField(ref field, value);
      }

      public string? cContactDefaultAddress_cAddressId {
         get;
         set => SetField(ref field, value);
      }

      public string? cContactDefaultComm_cCommId {
         get;
         set => SetField(ref field, value);
      }

      public string? cAddressName {
         get;
         private set => SetField(ref field, value);
      }

      public string? cAddressLocation {
         get;
         private set => SetField(ref field, value);
      }

      public string? cAddressZip {
         get;
         private set => SetField(ref field, value);
      }

      public int? cCommType {
         get;
         private set => SetField(ref field, value);
      }

      public ContactCommunicationState? cCommState {
         get;
         private set => SetField(ref field, value);
      }

      public string? cCommValue {
         get;
         private set => SetField(ref field, value);
      }

      public string? cCommNote {
         get;
         private set => SetField(ref field, value);
      }
      
      #endregion

      #region Methods

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

      protected override Task<vi_Contact?> FetchAsync() {
         EnsureNotSystemContact(cContactId);
         return Service.GetVi_Contact_ById(cContactId);
      }

      protected override Task UpdateAsync(vi_Contact entity) {
         EnsureNotSystemContact(entity.cContactId);
         return Service.PostTa_Contact_Update(entity);
      }

      #endregion
   }
}