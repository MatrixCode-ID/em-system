using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>UI model of one user account together with its contact, address, and default communication entry.</summary>
   public class User : UiModel<vi_User, ICredentialServices>
   {
      #region Statics

      /// <summary>Creates a blank user that has never been saved; its key is a placeholder until it is saved.</summary>
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

      /// <summary>Gets the number of users, for paging.</summary>
      public static Task<int> GetUsers_PageCountAsync(IEmApp app) =>
         app.ServiceProvider.GetRequiredService<ICredentialServices>().GetTa_Users_Count();

      /// <summary>Gets one page of users.</summary>
      public static async Task<User[]> GetUsers_InPageAsync(IEmApp app, int page, int pageSize) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var rows = await svc.GetVi_Users_InPage(page, pageSize);
         return [.. rows.Select(r => Build(app, r))];
      }

      /// <summary>Gets one user by its id.</summary>
      public static async Task<User> GetUser_ByIdAsync(IEmApp app, string cUserId) {
         EnsureNotSystemAccount(cUserId);
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var data = await svc.GetVi_User_ById(cUserId);
         return data == null
            ? throw new InvalidOperationException("User not found")
            : Build(app, data!);
      }

      /// <summary>Gets one user by its account name; throws when it does not exist.</summary>
      public static async Task<User> GetUser_ByAccountAsync(IEmApp app, string cUserAccount) =>
         (await GetUser_ByAccountAsync(app, cUserAccount, required: true))!;

      /// <summary>
      /// Finds a user by account name. With <paramref name="required"/> <c>false</c>, an account that is not
      /// found is answered with <c>null</c> instead of an exception - for flows that consider "no such
      /// account" an ordinary answer, not a failure, like a login screen whose account name was mistyped.
      /// <para>
      /// System accounts are still refused with <see cref="SystemAccountException"/> whatever the value of
      /// <paramref name="required"/>: they have no record to read at all.
      /// </para>
      /// </summary>
      /// <param name="app">The application object that owns the data service.</param>
      /// <param name="cUserAccount">The account name being looked up.</param>
      /// <param name="required">
      /// <c>true</c> when the account must exist - not found means an exception; <c>false</c> when not found
      /// may be answered with <c>null</c>.
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

      /// <summary>Wraps a user view row in a model.</summary>
      public static User Build(IEmApp app, vi_User data) => new(app, data);
   
      #endregion

      private User(IEmApp app, vi_User data) : base(app, data) { }

      #region Properties

      /// <summary>Id of the user.</summary>
      public string cUserId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Account of the user.</summary>
      public string cUserAccount {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Id of the contact.</summary>
      public string cContactId {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>State of the user.</summary>
      public UserState cUserState {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Is admin of the user.</summary>
      public bool cUserIsAdmin {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Full name of the contact.</summary>
      public string cContactFullName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>State of the contact.</summary>
      public ContactState cContactState {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Type of the contact.</summary>
      public ContactType cContactType {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Note of the contact.</summary>
      public string? cContactNote {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Name of the address.</summary>
      public string? cAddressName {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>Location of the address.</summary>
      public string? cAddressLocation {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Zip of the address.</summary>
      public string? cAddressZip {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Type of the communication.</summary>
      public CommunationType cCommType {
         get;
         private set => SetField(ref field, value);
      }

      /// <summary>State of the communication.</summary>
      public ContactCommunicationState cCommState {
         get;
         private set => SetField(ref field, value);
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

      /// <inheritdoc />
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

      /// <inheritdoc />
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

      /// <inheritdoc />
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

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override UiIconType DefaultUiIcon => UiIconType.User;

      /// <inheritdoc />
      protected override Task<vi_User?> FetchAsync() {
         // ResetAsync reads the server through here and nowhere else, so guarding this one method
         // is what closes reloading for a system account as well.
         EnsureNotSystemAccount(cUserId);
         return Service.GetVi_User_ById(cUserId);
      }

      /// <inheritdoc />
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
      /// The rights this user really holds, loaded once by <c>EmApp.RefreshClaimsAsync</c> when this user is
      /// made the active user. Rights belong to a person, not to a connection - so they are kept here, not in
      /// <c>ApiClient</c> - and disappear as soon as this user stops being the active user.
      /// </summary>
      // Not "internal": EmApp.RefreshClaimsAsync, the one filler that should exist, lives in the
      // Em.Ui.Wpf.Core assembly - different from this class's assembly - and this repo deliberately does not
      // use InternalsVisibleTo anywhere (see the backend CLAUDE.md). The limit is therefore a convention, not
      // compiler enforcement: modules should not overwrite this value themselves.
      public ClaimAction[] AvailableClaims { get; set; } = [];

      /// <summary>
      /// Reads the rights that are really granted to this user from the server. Must not be called for system
      /// accounts (debugger, built-in administrator): neither has any grant rows to read -
      /// <c>EmApp.RefreshClaimsAsync</c> knows this and never calls it for them.
      /// </summary>
      public Task<ClaimAction[]> GetClaims() {
         EnsureNotSystemAccount(cUserId);
         return Service.GetMeta_UserClaims(cUserId);
      }

      /// <summary>
      /// Reads the rights that flow to this user through the roles they hold. What the server answers is only
      /// rights from roles that are on and assignments whose validity period is running - that decision is
      /// never taken here. A pair with <see cref="GetClaims"/>, and closed to system accounts for exactly the
      /// same reason.
      /// </summary>
      public Task<ClaimAction[]> GetRoleClaims() {
         EnsureNotSystemAccount(cUserId);
         return Service.GetMeta_UserRoleClaims(cUserId);
      }

      #endregion
   }
}