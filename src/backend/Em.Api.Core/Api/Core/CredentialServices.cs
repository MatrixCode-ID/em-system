using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>Implementation of the user, role, claim, sign-in, and session actions.</summary>
   [Module(Defaults.CredentialModuleName)]
   public class CredentialServices(ApiCoreContext ctx) : ServicesBase, ICredentialServices
   {
      #region Tables

      #region ta_User

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_User?> GetTa_User_ById(string cUserId) {
         var data = await ctx.ta_Users.Where(r => r.cUserId == cUserId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_User[]> GetTa_Users() {
         var data = await ctx.ta_Users
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public Task<int> GetTa_Users_Count() => ctx.ta_Users.CountAsync(AbortToken);

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_User[]> GetTa_Users_ByContactId(string cContactId) {
         var data = await ctx.ta_Users
            .Where(r => r.cContactId == cContactId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_User[]> GetTa_Users_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.ta_Users
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cUserId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_User_New(
         DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential> data) {
         // The payload carries the five rows positionally; naming them here states the ordering
         // once and leaves the transaction below reading as what it is.
         var user = data.Payload1;
         var contact = data.Payload2;
         var address = data.Payload3;
         var comm = data.Payload4;
         var credential = data.Payload5;

         EnsureAccountNameIsNotReserved(user);

         // The contact and the rows it points at reference each other, so no single ordering of
         // plain writes satisfies both directions: the contact lands first without its default
         // columns, then the rows those columns name, then the contact is filled in. EF is never
         // told about any of these foreign keys - the entities carry no navigations and the model
         // is not configured - so it cannot work the order out and would otherwise write the rows
         // in whatever order they were handed to it.
         await using var tx = await ctx.Database.BeginTransactionAsync();
         try {
            var defaultAddressId = contact.cContactDefaultAddress_cAddressId;
            var defaultCommId = contact.cContactDefaultComm_cCommId;
            contact.cContactDefaultAddress_cAddressId = null;
            contact.cContactDefaultComm_cCommId = null;

            // A new user does not always arrive with new rows around it: the contact handed over -
            // and the address and communication under it - can be ones already on file, and writing
            // those as inserts would fail on their primary key. Each of the three is written as an
            // update when its key is already there, and the rest of the transaction carries on with
            // the row the context ends up tracking rather than the one off the payload.
            var storedContact = await Upsert(ctx.ta_Contacts, contact, contact.cContactId);
            await ctx.SaveChangesAsync();

            await Upsert(ctx.ta_Addresses, address, address.cAddressId);
            await Upsert(ctx.ta_Comms, comm, comm.cCommId);
            await ctx.SaveChangesAsync();

            storedContact.cContactDefaultAddress_cAddressId = defaultAddressId;
            storedContact.cContactDefaultComm_cCommId = defaultCommId;
            ctx.ta_Users.Add(user);
            await ctx.SaveChangesAsync();

            // Saved on its own round rather than together with the user above: the credential points
            // at the user, and that reference is enforced by the database while EF knows nothing
            // about it, so the two have to be written in an order this method decides.
            ctx.ta_UserCredentials.Add(credential);
            await ctx.SaveChangesAsync();

            await tx.CommitAsync();
         }
         catch (Exception) {
            await tx.RollbackAsync();
            throw;
         }
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_User_NewBatch(
         DtoPayload<ta_User, ta_Contact, ta_Address, ta_Comm, ta_UserCredential>[] datas) {
         if (datas.Length == 0) return;

         // Same three phases as the single insert, only one round of rows per phase instead of one
         // per user. The default columns are parked per contact rather than recomputed from the
         // payload, so a contact that arrived without them keeps arriving without them.
         await using var tx = await ctx.Database.BeginTransactionAsync();
         try {
            // Same positional payload as the single insert, read once into named arrays.
            var users = datas.Select(r => r.Payload1).ToArray();
            foreach (var user in users) EnsureAccountNameIsNotReserved(user);

            var contacts = datas.Select(r => r.Payload2).ToArray();
            var addresses = datas.Select(r => r.Payload3).ToArray();
            var comms = datas.Select(r => r.Payload4).ToArray();
            var credentials = datas.Select(r => r.Payload5).ToArray();
            var defaults = contacts
               .Select(r => (r.cContactDefaultAddress_cAddressId, r.cContactDefaultComm_cCommId))
               .ToArray();

            foreach (var contact in contacts) {
               contact.cContactDefaultAddress_cAddressId = null;
               contact.cContactDefaultComm_cCommId = null;
            }

            // The same rule as the single insert - a row whose key is already on file is an update,
            // not an insert - only decided here rather than by the change tracker, which bulk writes
            // do not go through. Each array is split into what is new and what is a restatement of
            // something already stored.
            var (newContacts, storedContacts) = await SplitByStored(ctx.ta_Contacts, contacts,
               nameof(ta_Contact.cContactId), r => r.cContactId, (r, createdStamp) => r.datestamp = createdStamp);
            var (newAddresses, storedAddresses) = await SplitByStored(ctx.ta_Addresses, addresses,
               nameof(ta_Address.cAddressId), r => r.cAddressId, (r, createdStamp) => r.datestamp = createdStamp);
            var (newComms, storedComms) = await SplitByStored(ctx.ta_Comms, comms,
               nameof(ta_Comm.cCommId), r => r.cCommId, (r, createdStamp) => r.datestamp = createdStamp);

            if (newContacts.Length > 0) await ctx.BulkInsertAsync(newContacts);
            if (storedContacts.Length > 0) await ctx.BulkUpdateAsync(storedContacts);

            if (newAddresses.Length > 0) await ctx.BulkInsertAsync(newAddresses);
            if (storedAddresses.Length > 0) await ctx.BulkUpdateAsync(storedAddresses);

            if (newComms.Length > 0) await ctx.BulkInsertAsync(newComms);
            if (storedComms.Length > 0) await ctx.BulkUpdateAsync(storedComms);

            for (var index = 0; index < contacts.Length; index++) {
               contacts[index].cContactDefaultAddress_cAddressId = defaults[index].cContactDefaultAddress_cAddressId;
               contacts[index].cContactDefaultComm_cCommId = defaults[index].cContactDefaultComm_cCommId;
            }

            // Every contact gets its default columns back, whether it was inserted above or updated.
            await ctx.BulkUpdateAsync(contacts);
            await ctx.BulkInsertAsync(users);

            // After the users, never with them: every credential points at one of the rows just
            // written, and the database refuses it until that row exists.
            await ctx.BulkInsertAsync(credentials);

            await tx.CommitAsync();
         }
         catch (Exception) {
            await tx.RollbackAsync();
            throw;
         }
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_User_Update(ta_User data) {
         // The administrator right is attached to the access token since its session was opened, so revoking
         // it here has no effect while the old session is still alive. The previous value is read first so
         // those sessions can be ended - exactly the same reason that makes a password change revoke
         // sessions: what the token vouches for must not differ from what is written in the data.
         var wasAdmin = await ctx.ta_Users.AsNoTracking()
            .Where(r => r.cUserId == data.cUserId)
            .Select(r => (bool?)r.cUserIsAdmin)
            .SingleOrDefaultAsync();

         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();

         if (wasAdmin is { } previous && previous != data.cUserIsAdmin) {
            await Tokens.RevokeAllAsync(data.cUserId);
         }
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_User_UpdateBatch(ta_User[] datas) {
         // Same as on the single-row path above: a changed administrator right must end the owner's
         // sessions, and the change can only be detected by reading the old value before this write
         // overwrites it.
         var ids = datas.Select(r => r.cUserId).ToArray();
         var wereAdmin = await ctx.ta_Users.AsNoTracking()
            .Where(r => ids.Contains(r.cUserId))
            .Select(r => new { r.cUserId, r.cUserIsAdmin })
            .ToDictionaryAsync(r => r.cUserId, r => r.cUserIsAdmin);

         await ctx.BulkUpdateAsync(datas);

         foreach (var user in datas) {
            if (wereAdmin.TryGetValue(user.cUserId, out var wasAdmin) && wasAdmin != user.cUserIsAdmin) {
               await Tokens.RevokeAllAsync(user.cUserId);
            }
         }
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_User_Delete(ta_User data) {
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_User_DeleteBatch(ta_User[] datas) {
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      #region ta_Role

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_Role?> GetTa_Role_ById(string cRoleId) {
         Request.RequireUserId();
         var data = await ctx.ta_Roles.Where(r => r.cRoleId == cRoleId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_Role[]> GetTa_Roles() {
         Request.RequireUserId();
         var data = await ctx.ta_Roles
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public Task<int> GetTa_Roles_Count() {
         Request.RequireUserId();
         return ctx.ta_Roles.CountAsync(AbortToken);
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_Role[]> GetTa_Roles_InPage(int page, int pageSize) {
         Request.RequireUserId();
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.ta_Roles
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cRoleId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_Role_New(ta_Role data) {
         Request.RequireAdmin();
         ctx.ta_Roles.Add(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_Role_NewBatch(ta_Role[] datas) {
         Request.RequireAdmin();
         await ctx.BulkInsertAsync(datas);
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_Role_Update(ta_Role data) {
         Request.RequireAdmin();
         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_Role_UpdateBatch(ta_Role[] datas) {
         Request.RequireAdmin();
         await ctx.BulkUpdateAsync(datas);
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_Role_Delete(ta_Role data) {
         Request.RequireAdmin();

         // The assignments are deleted first because the foreign key is NO ACTION: deleting a role that is
         // still held by someone would fail as a raw foreign key violation from SQL Server - a message that
         // means nothing on screen. The role's rights are removed by themselves through CASCADE, so they need
         // not be mentioned here.
         var assignments = await ctx.ta_UserRoles
            .Where(r => r.cRoleId == data.cRoleId)
            .ToArrayAsync();
         foreach (var row in assignments) {
            ctx.DeleteRow(row);
         }

         ctx.DeleteRow(data);

         // A single SaveChanges, so a single transaction: if deleting the role fails, the assignments that
         // were already deleted come back too - not left as rows pointing at a role that still exists.
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_Role_DeleteBatch(ta_Role[] datas) {
         Request.RequireAdmin();

         // Deliberately not BulkDeleteAsync: bulk operations bypass the change tracker and run as their own
         // command, so deleting the assignments and deleting the role would no longer be in one transaction -
         // see the reason on the action above.
         var ids = datas.Select(r => r.cRoleId).ToArray();
         var assignments = await ctx.ta_UserRoles
            .Where(r => ids.Contains(r.cRoleId))
            .ToArrayAsync();
         foreach (var row in assignments) {
            ctx.DeleteRow(row);
         }

         foreach (var row in datas) {
            ctx.DeleteRow(row);
         }

         await ctx.SaveChangesAsync();
      }

      #endregion

      #region ta_RoleClaim

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_RoleClaim[]> GetTa_RoleClaims_ByRoleId(string cRoleId) {
         Request.RequireUserId();
         var data = await ctx.ta_RoleClaims
            .Where(r => r.cRoleId == cRoleId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      // The right key that arrives here is not checked against the catalog again, and that is deliberate.
      // The server catalog only contains rights declared by modules; the client has its own, wider catalog -
      // rights of the client's built-in screens, which belong to no module - and checking here would reject
      // exactly those legitimate rights. The side that checks the spelling of a key is the one that knows
      // both halves of the catalog, which is the client.
      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_RoleClaim_New(ta_RoleClaim data) {
         Request.RequireAdmin();

         // Idempotent, just like granting a right directly: granting a right that is already held is not an
         // error, and the second row would be refused by its own primary key.
         var exists = await ctx.ta_RoleClaims
            .AnyAsync(r => r.cRoleId == data.cRoleId && r.cClaimName == data.cClaimName);
         if (exists) return;

         ctx.ta_RoleClaims.Add(data);
         await ctx.SaveChangesAsync();
      }

      // Not checked against the catalog, for the same reason as the single grant above.
      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_RoleClaim_NewBatch(ta_RoleClaim[] datas) {
         Request.RequireAdmin();

         var roleIds = datas.Select(r => r.cRoleId).Distinct().ToArray();
         var existing = await ctx.ta_RoleClaims
            .Where(r => roleIds.Contains(r.cRoleId))
            .Select(r => new { r.cRoleId, r.cClaimName })
            .ToArrayAsync();
         var held = existing
            .Select(r => RoleClaimKey(r.cRoleId, r.cClaimName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         // Existing rows are filtered out here, not left to fail in the database: one right that happens to be
         // held already must not fail the eleven other changes sent with it by the same save button. Add also
         // filters a submission that contains duplicate rows within itself.
         var rows = datas
            .Where(r => held.Add(RoleClaimKey(r.cRoleId, r.cClaimName)))
            .ToArray();
         if (rows.Length == 0) return;

         ctx.ta_RoleClaims.AddRange(rows);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_RoleClaim_Delete(ta_RoleClaim data) {
         Request.RequireAdmin();
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_RoleClaim_DeleteBatch(ta_RoleClaim[] datas) {
         Request.RequireAdmin();
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      #region ta_UserRole

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_UserRole[]> GetTa_UserRoles_ByRoleId(string cRoleId) {
         Request.RequireUserId();
         var data = await ctx.ta_UserRoles
            .Where(r => r.cRoleId == cRoleId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ta_UserRole[]> GetTa_UserRoles_ByUserId(string cUserId) {
         Request.RequireSelfOrAdmin(cUserId);
         var data = await ctx.ta_UserRoles
            .Where(r => r.cUserId == cUserId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public Task<int> GetTa_UserRoles_Count() {
         Request.RequireUserId();
         return ctx.ta_UserRoles.CountAsync(AbortToken);
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_UserRole_New(ta_UserRole data) {
         Request.RequireAdmin();

         // Idempotent, a pair with ta_RoleClaim above: giving a role to someone who already holds it is not an
         // error, and the second row would be refused by its own composite key (cUserId, cRoleId).
         var exists = await ctx.ta_UserRoles
            .AnyAsync(r => r.cUserId == data.cUserId && r.cRoleId == data.cRoleId);
         if (exists) return;

         ctx.ta_UserRoles.Add(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_UserRole_NewBatch(ta_UserRole[] datas) {
         Request.RequireAdmin();

         var roleIds = datas.Select(r => r.cRoleId).Distinct().ToArray();
         var existing = await ctx.ta_UserRoles
            .Where(r => roleIds.Contains(r.cRoleId))
            .Select(r => new { r.cUserId, r.cRoleId })
            .ToArrayAsync();
         var held = existing
            .Select(r => UserRoleKey(r.cUserId, r.cRoleId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         // Exactly the same reason as PostTa_RoleClaim_NewBatch: one assignment that happens to exist already
         // must not fail the other changes sent with it by the same save button - and that is what makes
         // "press save again" after a submission cut off midway end cleanly, instead of ending in a primary key
         // collision. Add also filters a submission that contains duplicate rows within itself.
         var rows = datas
            .Where(r => held.Add(UserRoleKey(r.cUserId, r.cRoleId)))
            .ToArray();
         if (rows.Length == 0) return;

         await ctx.BulkInsertAsync(rows);
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_UserRole_Update(ta_UserRole data) {
         Request.RequireAdmin();
         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_UserRole_Delete(ta_UserRole data) {
         Request.RequireAdmin();
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostTa_UserRole_DeleteBatch(ta_UserRole[] datas) {
         Request.RequireAdmin();
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      // The credential rows never leave this server: their secret column holds the password hash,
      // and a hash that can be fetched is a password that can be broken offline at leisure. These
      // stay as plain methods with no action attribute, so the dispatcher never gives them a URL -
      // the only way in is through the sign-in and password actions further down.
      #region ta_UserCredential

      internal async Task<ta_UserCredential?> GetTa_UserCredential_ById(string cCredentialId) {
         var data = await ctx.ta_UserCredentials.Where(r => r.cCredentialId == cCredentialId)
            .SingleOrDefaultAsync();
         return data;
      }

      internal async Task<ta_UserCredential?> GetTa_UserCredential_ByType(string cUserId, string cCredentialType) {
         var data = await ctx.ta_UserCredentials
            .Where(r => r.cUserId == cUserId && r.cCredentialType == cCredentialType)
            .SingleOrDefaultAsync();
         return data;
      }

      internal async Task<ta_UserCredential[]> GetTa_UserCredentials() {
         var data = await ctx.ta_UserCredentials
            .ToArrayAsync();
         return data;
      }

      internal Task<int> GetTa_UserCredentials_Count() => ctx.ta_UserCredentials.CountAsync();

      internal async Task<ta_UserCredential[]> GetTa_UserCredentials_ByUserId(string cUserId) {
         var data = await ctx.ta_UserCredentials
            .Where(r => r.cUserId == cUserId)
            .ToArrayAsync();
         return data;
      }

      internal async Task<ta_UserCredential[]> GetTa_UserCredentials_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.ta_UserCredentials
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cCredentialId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync();
         return data;
      }

      internal async Task PostTa_UserCredential_New(ta_UserCredential data) {
         ctx.ta_UserCredentials.Add(data);
         await ctx.SaveChangesAsync();
      }

      internal async Task PostTa_UserCredential_NewBatch(ta_UserCredential[] datas) {
         await ctx.BulkInsertAsync(datas);
      }

      internal async Task PostTa_UserCredential_Update(ta_UserCredential data) {
         ctx.UpdateRow(data);
         await ctx.SaveChangesAsync();
      }

      internal async Task PostTa_UserCredential_UpdateBatch(ta_UserCredential[] datas) {
         await ctx.BulkUpdateAsync(datas);
      }

      internal async Task PostTa_UserCredential_Delete(ta_UserCredential data) {
         ctx.DeleteRow(data);
         await ctx.SaveChangesAsync();
      }

      internal async Task PostTa_UserCredential_DeleteBatch(ta_UserCredential[] datas) {
         await ctx.BulkDeleteAsync(datas);
      }

      #endregion

      #endregion

      #region Views

      #region vi_User

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_User?> GetVi_User_ById(string cUserId) {
         var data = await ctx.vi_Users.Where(r => r.cUserId == cUserId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_User?> GetVi_User_ByAccount(string cUserAccount) {
         var data = await ctx.vi_Users.Where(r => r.cUserAccount == cUserAccount)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_User[]> GetVi_Users() {
         var data = await ctx.vi_Users
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_User[]> GetVi_Users_InPage(int page, int pageSize) {
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.vi_Users
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cUserId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_User[]> GetVi_Users_ByContactId(string cContactId) {
         var data = await ctx.vi_Users
            .Where(r => r.cContactId == cContactId)
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_User[]> GetVi_Users_ByRoleId(string cRoleId) {
         Request.RequireUserId();

         // The assignments are what is filtered, not the users, and then the rows are taken from the user view
         // as-is: what is asked for is the members of a role in exactly the same shape as a user wherever it
         // is read, not a new combined shape the screen has to learn to recognize.
         var data = await (
               from user in ctx.vi_Users
               join assignment in ctx.ta_UserRoles on user.cUserId equals assignment.cUserId
               where assignment.cRoleId == cRoleId
               orderby user.cUserId
               select user)
            .ToArrayAsync(AbortToken);
         return data;
      }

      #endregion

      #region vi_Role

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_Role?> GetVi_Role_ById(string cRoleId) {
         Request.RequireUserId();
         var data = await ctx.vi_Roles.Where(r => r.cRoleId == cRoleId)
            .SingleOrDefaultAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_Role[]> GetVi_Roles() {
         Request.RequireUserId();
         var data = await ctx.vi_Roles
            .ToArrayAsync(AbortToken);
         return data;
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<vi_Role[]> GetVi_Roles_InPage(int page, int pageSize) {
         Request.RequireUserId();
         if (page < 1) page = 1;
         if (pageSize < 1) pageSize = 10;
         var data = await ctx.vi_Roles
            // A page is only a slice of an order, and without one the server is free to
            // return the rows differently each time - which is how a row ends up on two
            // pages or on none. The key is unique, so the order it gives is total.
            .OrderBy(r => r.cRoleId)
            .Skip((page - 1) * pageSize) // Skips the rows of prior pages
            .Take(pageSize)
            .ToArrayAsync(AbortToken);
         return data;
      }

      #endregion

      #endregion

      #region Meta's

      // One message for every way a sign-in can be wrong: no such account, an account that is not
      // allowed in, a credential that was never enrolled or has been revoked, and a password that
      // simply does not match. Telling them apart would turn this action into a way of finding out
      // which accounts exist.
      private const string InvalidCredentialsMessage = "Incorrect username or password.";

      /// <inheritdoc />
      [PostAction(IsPublicAction = true)]
      public async Task<TokenResult> PostGetMeta_SignIn(string cUserAccount, string password) {
         // Checked before anything else, because nothing below applies to this account: it has no
         // row in the user table, so there is no state column to read and no credential row to hold
         // its password. The name is reserved on the way in, so no other account can reach here.
         if (AdminAccount.IsAdminAccount(cUserAccount)) {
            return await SignInAdminAsync(password);
         }

         var user = await ctx.ta_Users.SingleOrDefaultAsync(r => r.cUserAccount == cUserAccount);

         // Deleted, suspended and pending accounts are all turned away here rather than further in:
         // the state column is what says an account may be signed into, and a correct password does
         // not override it.
         if (user is null || user.cUserState < UserState.Inactive) {
            throw new ActionException(InvalidCredentialsMessage, 401);
         }

         var credential = await this.GetCredentialAsync(user, PasswordCredential.CredentialType);
         if (!await credential.IsValidAsync(password)) {
            throw new ActionException(InvalidCredentialsMessage, 401);
         }

         return await Tokens.IssueAsync(user.cUserId);
      }

      /// <inheritdoc />
      [PostAction(IsPublicAction = true)]
      public Task<TokenResult> PostGetMeta_RefreshToken(string refreshToken) =>
         // Public because the refresh token is the proof: the access token that went with it has
         // expired by the time anyone needs this, and an expired token proves nothing.
         Tokens.RefreshAsync(refreshToken);

      /// <inheritdoc />
      [PostAction]
      public Task PostMeta_SignOut() => Tokens.RevokeAsync(Request.RequireSessionId());

      /// <inheritdoc />
      [PostAction]
      public Task PostMeta_SignOutAll() => Tokens.RevokeAllAsync(Request.RequireUserId());

      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_ChangeMyPassword(string oldPassword, string newPassword) {
         var user = await RequireUserAsync(Request.RequireUserId());
         var credential = await this.GetCredentialAsync(user, PasswordCredential.CredentialType);

         // The old password is asked for even though the caller is already signed in: it is what
         // stops a borrowed unlocked screen from becoming a permanent takeover of the account.
         // 400, not 401: the caller is signed in and stays signed in - what is wrong is the value
         // they typed, not their token. Answering 401 here would send a client that refreshes on 401
         // chasing a new token it does not need.
         if (!await credential.IsValidAsync(oldPassword)) {
            throw new ActionException("The current password is not correct.", 400);
         }

         await ((PasswordCredential)credential).UpdateCredential(newPassword);

         // Every other session keeps a refresh token that was handed out under the old password, so
         // changing it has to end them - otherwise whoever the change was meant to lock out stays in.
         await Tokens.RevokeAllAsync(user.cUserId);
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_ResetPassword(string cUserId, string newPassword) {
         Request.RequireSelfOrAdmin(cUserId);

         var user = await RequireUserAsync(cUserId);
         var credential = await this.GetCredentialAsync(user, PasswordCredential.CredentialType);
         await ((PasswordCredential)credential).UpdateCredential(newPassword);
         await Tokens.RevokeAllAsync(cUserId);
      }

      /// <inheritdoc />
      [PostAction]
      public async Task<ta_UserSession[]> PostGetMeta_GetSessions(string cUserId) {
         Request.RequireSelfOrAdmin(cUserId);

         // Asked of the token service rather than queried here, because a system account keeps its
         // sessions somewhere else entirely - and which table that is stays engine business. Both
         // kinds come back in the same shape, and neither carries the hash of its refresh token:
         // what is being shown is since when and until when somebody is signed in, and the hash is
         // no part of that - it is the stored half of a credential that is still live.
         var sessions = await Tokens.ListSessionsAsync(cUserId);
         return [.. sessions.Select(r => new ta_UserSession {
            cUserSessionId = r.SessionId,
            cUserId = r.AccountId,
            cUserSessionHash = string.Empty,
            cUserSessionState = r.State,
            cUserSessionExpiry = r.Expiry,
            ustamp = r.UpdatedAt,
            datestamp = r.StartedAt,
            json_object = null
         })];
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_ResetAdminPassword(string newPassword) {
         await RequireAdminAccountAccessAsync();

         await AdminAccount.SetPasswordAsync(ctx, Hasher, newPassword);

         // Every session of the account was opened under the old password, so changing it has to
         // end them - otherwise whoever the change was meant to lock out simply stays in.
         await Tokens.RevokeAllAsync(Defaults.AdminUserId);
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<bool> GetMeta_AdminAccountEnabled() {
         // Same answer as a route that does not exist: the login screen hides whether the account is
         // switched off, and this action must not become the way around that.
         if (!Request.IsDebugRequest) {
            throw new ActionException($"Action '{Request.RouteLabel}' was not found.", 404);
         }

         return await AdminAccount.IsEnabledAsync(ctx);
      }

      /// <inheritdoc />
      [GetAction]
      public Task<ClaimAction[]> GetMeta_AllClaimActions() {
         Request.RequireUserId();
         return Task.FromResult(App.AllClaims.ToArray());
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ClaimAction[]> GetMeta_UserClaims(string cUserId) {
         Request.RequireSelfOrAdmin(cUserId);

         // The time comes from the server, like GetMeta_UserRoleClaims: a grant that has expired - or not yet
         // started - is not a right, and it is not the asking machine that decides that.
         var now = await App.GetDateStampAsync();

         // Through UserClaimLoader, not its own query: the time window rule is exactly the same as the one the
         // gate uses when deciding whether an action may be called, and this screen loses its purpose if what
         // it shows can differ from what really applies.
         var names = await UserClaimLoader.QueryDirectNames(ctx, cUserId, now).ToArrayAsync(AbortToken);

         // Not filtered against the catalog: an orphaned grant - a claim removed from code whose row remains -
         // must stay visible here, because only there can it be recognized and revoked through
         // PostMeta_RemoveUserClaim.
         return [.. names.Select(ClaimAction.FromKey)];
      }

      // Not checked against the catalog, for the same reason as granting a role right - see the note on
      // PostTa_RoleClaim_New.
      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_AddUserClaim(UserClaimPayload claim) {
         Request.RequireAdmin();

         var exists = await ctx.ta_UserClaims.AsNoTracking()
            .AnyAsync(r => r.cUserId == claim.cUserId && r.cUserClaimName == claim.cUserClaimName);
         if (exists) return;

         // All three time columns are NOT NULL in the database and no screen picks their values yet, so they
         // are filled here: valid from now, with no end. Left empty, the value would fall to 0001-01-01, which
         // is outside the range of the SQL Server datetime type - and every grant would fail on insert.
         var stamp = await App.GetDateStampAsync();

         ctx.ta_UserClaims.Add(new ta_UserClaim {
            cUserClaimId = $"{Ulid.NewUlid()}",
            cUserId = claim.cUserId,
            cUserClaimName = claim.cUserClaimName,
            cUserClaimStart = stamp,
            cUserClaimExpiry = Defaults.NoExpiry,
            datestamp = stamp
         });
         await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_RemoveUserClaim(UserClaimPayload claim) {
         Request.RequireAdmin();

         var rows = await ctx.ta_UserClaims
            .Where(r => r.cUserId == claim.cUserId && r.cUserClaimName == claim.cUserClaimName)
            .ToArrayAsync();

         foreach (var row in rows) {
            ctx.DeleteRow(row);
         }

         if (rows.Length > 0) await ctx.SaveChangesAsync();
      }

      /// <inheritdoc />
      [PostAction]
      public async Task PostMeta_ChangeAdminPassword(string oldPassword, string newPassword) {
         await RequireAdminAccountAccessAsync();

         // The old password is asked for even though the caller is already signed in: it is what
         // stops a borrowed unlocked screen from becoming a permanent takeover of the account.
         // 400, not 401: the caller is signed in and stays signed in - what is wrong is the value
         // they typed, not their token. Answering 401 here would send a client that refreshes on
         // 401 chasing a new token it does not need.
         if (!await AdminAccount.IsPasswordValidAsync(ctx, Hasher, oldPassword)) {
            throw new ActionException("The current password is not correct.", 400);
         }

         await AdminAccount.SetPasswordAsync(ctx, Hasher, newPassword);
         await Tokens.RevokeAllAsync(Defaults.AdminUserId);
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ClaimAction[]> GetMeta_UserRoleClaims(string cUserId) {
         Request.RequireSelfOrAdmin(cUserId);

         // The time is taken from the server, not from the caller: a validity period that the asking machine
         // can decide is no validity period at all.
         var now = await App.GetDateStampAsync();

         // Through UserClaimLoader, for the same reason as GetMeta_UserClaims: one time window rule for the
         // screen and for the gate.
         var names = await UserClaimLoader.QueryRoleNames(ctx, cUserId, now)
            .Distinct()
            .ToArrayAsync(AbortToken);

         // Not filtered against the catalog, for exactly the same reason as GetMeta_UserClaims: orphaned grants
         // must stay visible so they can be recognized and revoked.
         return [.. names.Select(ClaimAction.FromKey)];
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<RoleCounter[]> GetMeta_RoleCounters() {
         Request.RequireUserId();

         // Three queries, not one per role: the role list on screen holds a dozen rows, and counting them one
         // by one would mean a dozen round trips to the server for two numbers.
         var members = await ctx.ta_UserRoles
            .GroupBy(r => r.cRoleId)
            .Select(g => new { cRoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.cRoleId, r => r.Count, AbortToken);

         var claims = await ctx.ta_RoleClaims
            .GroupBy(r => r.cRoleId)
            .Select(g => new { cRoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.cRoleId, r => r.Count, AbortToken);

         // The role list decides which rows come out, not the grouping results above: a role that has no
         // members or rights yet must still be answered - with zero, not with no row at all.
         var ids = await ctx.ta_Roles
            .Select(r => r.cRoleId)
            .ToArrayAsync(AbortToken);

         return [.. ids.Select(id => new RoleCounter {
            cRoleId = id,
            MemberCount = members.GetValueOrDefault(id),
            ClaimCount = claims.GetValueOrDefault(id)
         })];
      }

      /// <inheritdoc />
      [GetAction]
      public async Task<ClaimUsage[]> GetMeta_ClaimUsage() {
         Request.RequireUserId();

         var rows = await ctx.ta_RoleClaims
            .GroupBy(r => r.cClaimName)
            .Select(g => new { ClaimKey = g.Key, RoleCount = g.Count() })
            .ToArrayAsync(AbortToken);

         return [.. rows.Select(r => new ClaimUsage { ClaimKey = r.ClaimKey, RoleCount = r.RoleCount })];
      }

      #endregion

      #region Meta helpers

      // Resolved on use rather than taken in the constructor: the token service is engine-internal
      // and this class is part of the published surface, so naming it in a public constructor would
      // drag it out with it.
      private ITokenServices Tokens => GetService<ITokenServices>()
         ?? throw new InvalidOperationException($"No {nameof(ITokenServices)} is registered.");

      // Composite key of one ta_RoleClaim row, used as the content of a HashSet when filtering duplicate
      // rows. Built through a single method so both sides of the comparison - what is already stored and
      // what just arrived - cannot be built in different ways.
      private static string RoleClaimKey(string cRoleId, string cClaimName) => $"{cRoleId}|{cClaimName}";

      private static string UserRoleKey(string cUserId, string cRoleId) => $"{cUserId}|{cRoleId}";

      private async Task<ta_User> RequireUserAsync(string cUserId) =>
         await ctx.ta_Users.SingleOrDefaultAsync(r => r.cUserId == cUserId)
         ?? throw new ActionException($"User '{cUserId}' was not found.", 404);

      // Resolved on use for the same reason as the token service above: it is engine wiring, and a
      // public constructor naming it would drag it out into the published surface along with this
      // class.
      private IStringHasher Hasher => GetService<IStringHasher>()
         ?? throw new InvalidOperationException($"No {nameof(IStringHasher)} is registered.");

      // Signing in as the administrator account. Kept apart from the action above rather than woven
      // into it: the two share the answer they give when they fail, and nothing else.
      private async Task<TokenResult> SignInAdminAsync(string password) {
         // Turned away with the very same message as a wrong password, deliberately. A message of
         // its own would turn the login screen into a way of learning that the account is there but
         // currently switched off - which is exactly what switching it off was meant to stop.
         if (!await AdminAccount.IsEnabledAsync(ctx)) {
            throw new ActionException(InvalidCredentialsMessage, 401);
         }

         // PasswordCredential is not used here even though this is a password: it works over a user
         // row and a credential row, and this account has neither.
         if (!await AdminAccount.IsPasswordValidAsync(ctx, Hasher, password)) {
            throw new ActionException(InvalidCredentialsMessage, 401);
         }

         return await Tokens.IssueAsync(Defaults.AdminUserId);
      }

      // Both administrator password actions ask the same two things: that the caller is a
      // signed-in administrator - which the request itself already knows, straight off the claim the
      // token carries - and that the account is switched on at all.
      private async Task RequireAdminAccountAccessAsync() {
         Request.RequireAdmin();

         // Nobody edits the password of an account that is switched off - not even from inside it.
         // The switch lives in the database and moves only by hand, so this is the one thing here
         // that no signed-in caller can talk their way past.
         if (!await AdminAccount.IsEnabledAsync(ctx)) {
            throw new ActionException("The administrator account is not enabled.", 403);
         }
      }

      // The administrator account is typed at the login screen the same way any other account is,
      // so no stored account may answer to that name: two of them sharing it would leave the login
      // screen unable to say which one is being signed into. Refused here rather than only in the
      // editor, because the editor is not the only way a row reaches this table.
      private static void EnsureAccountNameIsNotReserved(ta_User user) {
         if (AdminAccount.IsAdminAccount(user.cUserAccount)) {
            throw new ActionException($"The account name '{Defaults.AdminUserAccount}' is reserved.", 400);
         }
      }

      #endregion

      #region Helpers

      // Every table in this repository carries the same creation stamp column. The name is spelled
      // out once here because the helper below works over any entity type and so cannot reach the
      // column through a property.
      private const string CreatedStampProperty = "datestamp";

      // Writes one row of a payload: an insert when its key is not on file yet, an update of the
      // stored row when it is. What comes back is the instance the context is tracking - the stored
      // row where there was one - so a caller that has to touch the row again after saving changes
      // the instance the write will actually be taken from.
      private async Task<T> Upsert<T>(DbSet<T> set, T row, object key) where T : class {
         var stored = await set.FindAsync(key);
         if (stored is null) {
            set.Add(row);
            return row;
         }

         // Reads are no-tracking by default, so a row that came from the database rather than from
         // the change tracker arrives detached - and an edit to a row nothing is watching is an
         // edit the save below would not write. Find is still used to look it up, because it
         // answers from the change tracker first and only then asks the database.
         var entry = ctx.Entry(stored);
         if (entry.State == EntityState.Detached) entry = set.Attach(stored);

         // The creation stamp is the one column the payload must not carry over: it records when
         // the row was first written, while the payload holds the moment this request ran. Every
         // other column is taken as given - an existing row is being restated, not merged into.
         var created = entry.Property(CreatedStampProperty).CurrentValue;
         entry.CurrentValues.SetValues(row);
         entry.Property(CreatedStampProperty).CurrentValue = created;
         return stored;
      }

      // The batch counterpart of Upsert: reads which of the keys are already on file and splits the
      // rows into the ones to insert and the ones to update. Bulk writes never reach the change
      // tracker, so an update has nothing to merge into and would carry the payload's creation stamp
      // over the stored one - the stamp is put back on each row here instead.
      // The key is named twice on purpose: once as a column, which is what the query can filter on,
      // and once as a property, which is what the rows in memory can be grouped by.
      private async Task<(T[] Inserts, T[] Updates)> SplitByStored<T>(DbSet<T> set, T[] rows, string keyColumn,
         Func<T, string> keyOf, Action<T, DateTime> setCreatedStamp) where T : class {
         var keys = rows.Select(keyOf).ToArray();
         var stored = await set.AsNoTracking()
            .Where(r => keys.Contains(EF.Property<string>(r, keyColumn)))
            .Select(r => new {
               Key = EF.Property<string>(r, keyColumn),
               CreatedStamp = EF.Property<DateTime>(r, CreatedStampProperty)
            })
            .ToDictionaryAsync(r => r.Key, r => r.CreatedStamp);

         if (stored.Count == 0) return (rows, []);

         var inserts = new List<T>();
         var updates = new List<T>();
         foreach (var row in rows) {
            if (stored.TryGetValue(keyOf(row), out var createdStamp)) {
               setCreatedStamp(row, createdStamp);
               updates.Add(row);
            }
            else {
               inserts.Add(row);
            }
         }

         return ([.. inserts], [.. updates]);
      }

      #endregion
   }
}