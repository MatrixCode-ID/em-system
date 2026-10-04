using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   public class User : UiModel<vi_User, ICredentialServices>
   {
      #region Statics

      public static User CreateNewUser(IEmApp app) {
         // Both starting values go on the raw row rather than on the model, because that row also
         // becomes what RollBack returns to: anything set on the model afterwards would be wiped by
         // the first Discard, and would mark the record changed before anything had been typed.
         var user = new User(app, new vi_User() {
            cUserId = "Save User To Generate ID's",
            cUserState = UserState.Pending
         }) {
            IsBlank = true
         };
         return user;
      }

      public static Task<int> GetUsers_PageCountAsync(IEmApp app) =>
         app.ServiceProvider.GetRequiredService<ICredentialServices>().GetTa_Users_Count();

      public static async Task<User[]> GetUsers_InPageAsync(IEmApp app, int page, int pageSize) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var rows = await svc.GetVi_Users_InPage(page, pageSize);
         return [.. rows.Select(r => Build(app, r))];
      }

      public static async Task<User> GetUser_ByIdAsync(IEmApp app, string cUserId) {
         EnsureNotSystemAccount(cUserId);
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var data = await svc.GetVi_User_ById(cUserId);
         return data == null
            ? throw new InvalidOperationException("User not found")
            : Build(app, data!);
      }

      public static async Task<User> GetUser_ByAccountAsync(IEmApp app, string cUserAccount) =>
         (await GetUser_ByAccountAsync(app, cUserAccount, required: true))!;

      /// <summary>
      /// Mencari user lewat nama akunnya. Dengan <paramref name="required"/> <c>false</c>, akun yang
      /// tidak ketemu dijawab <c>null</c> alih-alih exception — untuk alur yang memang menganggap
      /// "tidak ada akun itu" sebagai jawaban biasa, bukan kegagalan, seperti layar login yang nama
      /// akunnya salah ketik.
      /// <para>
      /// Akun sistem tetap ditolak dengan <see cref="SystemAccountException"/> berapa pun nilai
      /// <paramref name="required"/>: mereka sama sekali tidak punya record untuk dibaca.
      /// </para>
      /// </summary>
      /// <param name="app">Objek aplikasi pemilik service data.</param>
      /// <param name="cUserAccount">Nama akun yang dicari.</param>
      /// <param name="required">
      /// <c>true</c> kalau akun wajib ada — tidak ketemu berarti exception; <c>false</c> kalau tidak
      /// ketemu boleh dijawab <c>null</c>.
      /// </param>
      public static async Task<User?> GetUser_ByAccountAsync(IEmApp app, string cUserAccount, bool required) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var data = await svc.GetVi_User_ByAccount(cUserAccount);
         if (data == null) {
            return required ? throw new InvalidOperationException("User not found") : null;
         }

         EnsureNotSystemAccount(data.cUserId);
         return Build(app, data!);
      }

      public static User Build(IEmApp app, vi_User data) => new(app, data);
   
      #endregion

      private User(IEmApp app, vi_User data) : base(app, data) { }

      #region Properties

      public string cUserId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      public string cUserAccount {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public string cContactId {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public UserState cUserState {
         get;
         set => SetField(ref field, value);
      }

      public bool cUserIsAdmin {
         get;
         set => SetField(ref field, value);
      }

      public string cContactFullName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public ContactState cContactState {
         get;
         private set => SetField(ref field, value);
      }

      public ContactType cContactType {
         get;
         private set => SetField(ref field, value);
      }

      public string? cContactNote {
         get;
         private set => SetField(ref field, value);
      }

      public string? cAddressName {
         get;
         private set => SetField(ref field, value);
      }

      public string? cAddressLocation {
         get;
         set => SetField(ref field, value);
      }

      public string? cAddressZip {
         get;
         set => SetField(ref field, value);
      }

      public CommunationType cCommType {
         get;
         private set => SetField(ref field, value);
      }

      public ContactCommunicationState cCommState {
         get;
         private set => SetField(ref field, value);
      }

      public string cCommValue {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      public string? cCommNote {
         get;
         set => SetField(ref field, value);
      }

      #endregion

      #region Methods

      private Contact? _contact;
      private ContactAddress? _address;
      private ContactCommunication? _comm;

      // The debugger account and the administrator account stand in for a signed-in user without
      // having a record of their own: nothing was ever stored for them. Every path in this model
      // that would read or write that record is closed here, so it says why on the spot instead of
      // failing further in against something that was never there.
      private static void EnsureNotSystemAccount(string cUserId) {
         if (cUserId is Defaults.DebuggerUserId or Defaults.AdminUserId) {
            throw new SystemAccountException(
               $"User '{cUserId}' is a system account and has no record of its own to read or write.",
               cUserId);
         }
      }

      protected override async Task InsertAsync(vi_User entity) {
         EnsureNotSystemAccount(cUserId);
         entity.cUserId = $"{Ulid.NewUlid()}";

         // SaveAsync has already stamped the row with the server time, so the moment it was created
         // is that same moment - asking the server a second time would only cost a round trip and
         // leave the two stamps a few milliseconds apart. The rows built below share that one stamp
         // for the same reason: they are all created by this single operation.
         entity.datestamp = entity.ustamp;
         entity.cContactId = $"{Ulid.NewUlid()}";

         var contact = new ta_Contact {
            cContactId = entity.cContactId,
            cContactFullName = entity.cContactFullName,
            cContactState = entity.cContactState,
            cContactType = entity.cContactType,
            cContactNote = entity.cContactNote,
            ustamp = entity.ustamp,
            datestamp = entity.datestamp
         };

         // An address row is written even when nothing was typed into it, so that every user has one
         // to edit later; its columns do not accept null, so what was left empty is stored as such.
         var address = new ta_Address {
            cAddressId = $"{Ulid.NewUlid()}",
            cContactId = contact.cContactId,
            cAddressName = entity.cAddressName ?? string.Empty,
            cAddressState = AddressState.ActiveAsUserReference,
            cAddressLocation = entity.cAddressLocation ?? string.Empty,
            cAddressZip = entity.cAddressZip ?? string.Empty,
            ustamp = entity.ustamp,
            datestamp = entity.datestamp
         };

         var comm = new ta_Comm {
            cCommId = $"{Ulid.NewUlid()}",
            cContactId = contact.cContactId,
            cCommType = entity.cCommType,
            cCommState = ContactCommunicationState.ActiveAsUserReference,
            cCommValue = entity.cCommValue,
            cCommNote = entity.cCommNote,
            ustamp = entity.ustamp,
            datestamp = entity.datestamp
         };

         // The password credential is written together with the user rather than the first time
         // someone asks for it, so that every account has one from the moment it exists and setting
         // a password later is only ever an edit. It goes in empty and Pending: nothing has been
         // typed yet, and an account with no password set must not be one that can be signed into.
         var credential = new ta_UserCredential {
            cCredentialId = $"{Ulid.NewUlid()}",
            cUserId = entity.cUserId,
            cCredentialType = Defaults.PasswordCredentialType,
            cCredentialState = CredentialState.Pending,
            cCredentialKey = null,
            cCredentialSecret = null,
            ustamp = entity.ustamp,
            datestamp = entity.datestamp,
            json_object = null
         };

         contact.cContactDefaultAddress_cAddressId = address.cAddressId;
         contact.cContactDefaultComm_cCommId = comm.cCommId;

         // SaveAsync refills the model from this entity once the insert returns, so the columns it
         // carries from the joined rows are squared with the rows actually being written. Left alone
         // they would keep whatever the blank row started with and the model would show a state that
         // was never stored - the communication state most of all, which is fixed here rather than chosen.
         entity.cCommState = comm.cCommState;
         entity.cAddressName = address.cAddressName;
         entity.cAddressLocation = address.cAddressLocation;
         entity.cAddressZip = address.cAddressZip;

         // One request, one transaction: the five rows depend on each other, and a failure halfway
         // through would otherwise leave a contact, an address and a communication row behind with
         // no user to own them.
         // The type arguments are spelled out because entity is a vi_User: left to inference the
         // payload would close over the view type, which is not what the action takes.
         await Service.PostTa_User_New(
            DtoPayload.Build<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>(
               entity, contact, address, comm, credential));
      }

      protected override void ReadFrom(vi_User source) {
         cUserId = source.cUserId;
         cUserAccount = source.cUserAccount;
         cContactId = source.cContactId;
         cUserState = source.cUserState;
         cUserIsAdmin = source.cUserIsAdmin;
         cContactFullName = source.cContactFullName;
         cContactState = source.cContactState;
         cContactType = source.cContactType;
         cContactNote = source.cContactNote;
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

      protected override void WriteTo(vi_User target) {
         target.cUserId = cUserId;
         target.cUserAccount = cUserAccount;
         target.cContactId = cContactId;
         target.cUserState = cUserState;
         target.cUserIsAdmin = cUserIsAdmin;
         target.cContactFullName = cContactFullName;
         target.cContactState = cContactState;
         target.cContactType = cContactType;
         target.cContactNote = cContactNote;
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

      /// <inheritdoc />
      protected override UiIconType DefaultUiIcon => UiIconType.User;

      protected override Task<vi_User?> FetchAsync() {
         // ResetAsync reads the server through here and nowhere else, so guarding this one method
         // is what closes reloading for a system account as well.
         EnsureNotSystemAccount(cUserId);
         return Service.GetVi_User_ById(cUserId);
      }

      protected override async Task UpdateAsync(vi_User entity) {
         EnsureNotSystemAccount(cUserId);
         _contact ??= await Contact.GetContact_ByIdAsync(App, entity.cContactId)
            ?? throw new InvalidOperationException($"Contact '{entity.cContactId}' was not found on the server.");
         _comm ??= await ContactCommunication.GetComm_ByCommIdAsync(_contact, _contact.cContactDefaultComm_cCommId!);

         // A contact is allowed to have no default address - only rows created through this model are
         // guaranteed one - so the address is read back only when there is one to read, and what was
         // typed into the address fields is dropped rather than written to a row that does not exist.
         if (_address is null && _contact.cContactDefaultAddress_cAddressId is { } addressId) {
            _address = await _contact.GetAddressAsync(addressId);
         }

         _contact.cContactFullName = cContactFullName;
         _contact.cContactNote = cContactNote;
         _comm.cCommValue = cCommValue;
         _comm.cCommNote = cCommNote;

         if (_address is not null) {
            _address.cAddressName = cAddressName ?? string.Empty;
            _address.cAddressLocation = cAddressLocation ?? string.Empty;
            _address.cAddressZip = cAddressZip ?? string.Empty;
         }

         // Written one after another rather than together. They are four separate requests, so nothing
         // makes them a single transaction either way, and running them at once only buys a failure
         // that is harder to read: Task.WhenAll reports the first error and swallows the rest, leaving
         // no way to tell which of the rows made it through.
         await _contact.SaveAsync();
         await _comm.SaveAsync();
         if (_address is not null) await _address.SaveAsync();
         await Service.PostTa_User_Update(entity);
      }
      
      /// <summary>
      /// Hak yang benar-benar dimiliki user ini, dimuat sekali oleh <c>EmApp.RefreshClaimsAsync</c>
      /// saat user ini diangkat jadi pengguna aktif. Hak adalah milik orang, bukan milik koneksi -
      /// jadi disimpan di sini, bukan di <c>ApiClient</c> - dan ikut hilang begitu user ini berhenti
      /// menjadi pengguna aktif.
      /// </summary>
      // Bukan "internal": EmApp.RefreshClaimsAsync, satu-satunya pengisi yang semestinya, tinggal
      // di assembly Em.Ui.Wpf.Core - berbeda dari assembly class ini - dan repo ini sengaja tidak
      // memakai InternalsVisibleTo di mana pun (lihat CLAUDE.md backend). Batasnya karena itu adalah
      // konvensi, bukan penegakan compiler: module tidak seharusnya menimpa nilai ini sendiri.
      public ClaimAction[] AvailableClaims { get; set; } = [];

      /// <summary>
      /// Membaca hak yang benar-benar diberikan ke user ini dari server. Tidak boleh dipanggil untuk
      /// akun sistem (debugger, admin bawaan): keduanya tidak punya baris pemberian untuk dibaca sama
      /// sekali - <c>EmApp.RefreshClaimsAsync</c> mengetahui ini dan tidak pernah memanggilnya untuk
      /// mereka.
      /// </summary>
      public Task<ClaimAction[]> GetClaims() {
         EnsureNotSystemAccount(cUserId);
         return Service.GetMeta_UserClaims(cUserId);
      }

      /// <summary>
      /// Membaca hak yang mengalir ke user ini lewat role yang dipegangnya. Yang dijawab server
      /// hanya hak dari role yang sedang menyala dan penugasan yang masa berlakunya sedang jalan —
      /// keputusan itu tidak pernah diambil di sini. Sepasang dengan <see cref="GetClaims"/>, dan
      /// tertutup untuk akun sistem dengan alasan yang sama persis.
      /// </summary>
      public Task<ClaimAction[]> GetRoleClaims() {
         EnsureNotSystemAccount(cUserId);
         return Service.GetMeta_UserRoleClaims(cUserId);
      }

      #endregion
   }
}