using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Api.Core.Models
{
   /// <summary>
   /// UI model of one role - a set of rights that many users can hold at once, with a validity period per
   /// assignment. The role's own columns are saved through the ordinary route of
   /// <see cref="UiModel{TEntity,TService}"/>; the role's content - its rights and its members - lives in
   /// separate rows and therefore has its own methods that talk to the server directly.
   /// </summary>
   public class Role : UiModel<vi_Role, ICredentialServices>
   {
      #region Statics

      /// <summary>
      /// Creates an empty role that has never been saved. The row is only really born when
      /// <see cref="UiModel{TEntity,TService}.SaveAsync"/> is called - its id is issued there.
      /// </summary>
      public static Role CreateNewRole(IEmApp app) {
         // Both starting values go on the raw row rather than on the model, because that row also
         // becomes what RollBack returns to: anything set on the model afterwards would be wiped by
         // the first Discard, and would mark the record changed before anything had been typed.
         var role = new Role(app, new vi_Role {
            cRoleId = "Save Role To Generate ID's",
            cRoleState = RoleState.Active
         }) {
            IsBlank = true
         };
         return role;
      }

      /// <summary>Gets one role by its id.</summary>
      public static async Task<Role> GetRole_ByIdAsync(IEmApp app, string cRoleId) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var data = await svc.GetVi_Role_ById(cRoleId);
         return data == null
            ? throw new InvalidOperationException("Role not found")
            : Build(app, data);
      }

      /// <summary>Gets one page of roles.</summary>
      public static async Task<Role[]> GetRoles_InPageAsync(IEmApp app, int page, int pageSize) {
         var svc = app.ServiceProvider.GetRequiredService<ICredentialServices>();
         var rows = await svc.GetVi_Roles_InPage(page, pageSize);
         return [.. rows.Select(r => Build(app, r))];
      }

      /// <summary>Gets the number of roles, for paging.</summary>
      public static Task<int> GetRoles_PageCountAsync(IEmApp app) =>
         app.ServiceProvider.GetRequiredService<ICredentialServices>().GetTa_Roles_Count();

      /// <summary>Wraps a role view row in a model.</summary>
      public static Role Build(IEmApp app, vi_Role data) => new(app, data);

      #endregion

      private Role(IEmApp app, vi_Role entity) : base(app, entity) { }

      #region Properties

      /// <summary>Id of the role.</summary>
      public string cRoleId {
         get;
         private set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>Name of the role.</summary>
      public string cRoleName {
         get;
         set => SetField(ref field, value);
      } = string.Empty;

      /// <summary>State of the role.</summary>
      public RoleState cRoleState {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>Description of the role.</summary>
      public string? cRoleDescription {
         get;
         set => SetField(ref field, value);
      }

      /// <summary>
      /// Number of users who hold this role. Filled from outside by <see cref="RoleCollection"/>, which reads
      /// the numbers for the whole list at once - not computed per role, because a list holds a dozen roles
      /// and that would become a dozen requests to the server.
      /// </summary>
      /// <remarks>
      /// Deliberately not through <c>SetField</c>: this number is not a column of this row - it is computed
      /// from the assignment rows in another table - so filling it must not mark the role as changed. If it
      /// did, every role that was just read would immediately read as a role with unsaved changes, only
      /// because the list filled in its number.
      /// </remarks>
      public int MemberCount {
         get;
         set {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
         }
      }

      /// <summary>
      /// Number of rights this role carries. Filled from outside for the same reason as
      /// <see cref="MemberCount"/>.
      /// </summary>
      /// <inheritdoc cref="MemberCount" />
      public int ClaimCount {
         get;
         set {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
         }
      }

      #endregion

      #region UiModel Implementation

      /// <inheritdoc />
      protected override void ReadFrom(vi_Role source) {
         cRoleId = source.cRoleId;
         cRoleName = source.cRoleName;
         cRoleState = source.cRoleState;
         cRoleDescription = source.cRoleDescription;
         ustamp = source.ustamp;
         datestamp = source.datestamp;
         json_object = source.json_object;
      }

      /// <inheritdoc />
      protected override void WriteTo(vi_Role target) {
         target.cRoleId = cRoleId;
         target.cRoleName = cRoleName;
         target.cRoleState = cRoleState;
         target.cRoleDescription = cRoleDescription;
         target.ustamp = ustamp;
         target.datestamp = datestamp;
         target.json_object = json_object;
      }

      /// <inheritdoc />
      protected override Task<vi_Role?> FetchAsync() => Service.GetVi_Role_ById(cRoleId);

      /// <inheritdoc />
      protected override Task UpdateAsync(vi_Role entity) => Service.PostTa_Role_Update(entity);

      /// <inheritdoc />
      protected override Task InsertAsync(vi_Role entity) {
         entity.cRoleId = $"{Ulid.NewUlid()}";

         // SaveAsync has already stamped the row with the server time, so the moment it was created
         // is that same moment - asking the server a second time would only cost a round trip and
         // leave the two stamps a few milliseconds apart.
         entity.datestamp = entity.ustamp;
         return Service.PostTa_Role_New(entity);
      }

      /// <inheritdoc />
      protected override JsonObject BuildJson(JsonObject patch) => patch;

      /// <inheritdoc />
      protected override UiIconType DefaultUiIcon => UiIconType.Shield;

      #endregion

      #region Claims

      /// <summary>The rights this role carries, read directly from the server.</summary>
      public async Task<ClaimAction[]> GetRoleClaims() {
         var rows = await Service.GetTa_RoleClaims_ByRoleId(cRoleId);
         return [.. rows.Select(r => ClaimAction.FromKey(r.cClaimName))];
      }

      /// <summary>
      /// Adds one right to this role. A right that is already held is silently skipped by the server, so
      /// calling it twice is not an error.
      /// </summary>
      public Task AddClaim(ClaimAction action) {
         EnsureSaved();
         return Service.PostTa_RoleClaim_New(new ta_RoleClaim {
            cRoleId = cRoleId,
            cClaimName = action.Key
         });
      }

      /// <summary>Revokes one right from this role.</summary>
      public Task RemoveClaim(ClaimAction action) {
         EnsureSaved();
         return Service.PostTa_RoleClaim_Delete(new ta_RoleClaim {
            cRoleId = cRoleId,
            cClaimName = action.Key
         });
      }

      #endregion

      #region Members

      /// <summary>
      /// The users who hold this role. The validity period of their assignment is not here - read
      /// <see cref="GetAssignments"/> for that, and pair the two through <c>cUserId</c>.
      /// </summary>
      public async Task<User[]> GetMembers() {
         var rows = await Service.GetVi_Users_ByRoleId(cRoleId);
         return [.. rows.Select(r => User.Build(App, r))];
      }

      /// <summary>
      /// The assignments of this role together with their validity period, one row per user. A pair with
      /// <see cref="GetMembers"/>: one answers who, the other answers since and until when.
      /// </summary>
      public Task<ta_UserRole[]> GetAssignments() => Service.GetTa_UserRoles_ByRoleId(cRoleId);

      /// <summary>
      /// Gives this role to a user.
      /// </summary>
      /// <param name="member">The user who is given this role.</param>
      /// <param name="start">Start of its validity; <c>null</c> means valid from now.</param>
      /// <param name="expiry">End of its validity; <c>null</c> means no end.</param>
      public async Task AddMember(User member, DateTime? start = null, DateTime? expiry = null) {
         EnsureSaved();
         EnsureNotSystemAccount(member);

         var stamp = await App.GetDateStampAsync();
         await Service.PostTa_UserRole_New(new ta_UserRole {
            cUserId = member.cUserId,
            cRoleId = cRoleId,
            cUserRoleStart = start,
            cUserRoleExpiry = expiry,
            ustamp = stamp,
            datestamp = stamp
         });
      }

      /// <summary>
      /// Changes the validity period of an existing assignment, without revoking and granting it again -
      /// which would erase when the assignment was first created.
      /// </summary>
      /// <param name="member">The user who holds that assignment.</param>
      /// <param name="start">The new start of validity; <c>null</c> means since it was created.</param>
      /// <param name="expiry">The new end of validity; <c>null</c> means no end.</param>
      public async Task SetMemberPeriod(User member, DateTime? start, DateTime? expiry) {
         EnsureSaved();
         EnsureNotSystemAccount(member);

         // The old row is read first, not rebuilt from scratch: its datestamp keeps when this assignment was
         // created, and writing a new row over it would replace that with now - as if this person had just been
         // given the role.
         var assignments = await Service.GetTa_UserRoles_ByRoleId(cRoleId);
         var assignment = assignments.SingleOrDefault(r => r.cUserId == member.cUserId)
            ?? throw new InvalidOperationException(
               $"User '{member.cUserAccount}' does not hold role '{cRoleName}'.");

         assignment.cUserRoleStart = start;
         assignment.cUserRoleExpiry = expiry;
         assignment.ustamp = await App.GetDateStampAsync();
         await Service.PostTa_UserRole_Update(assignment);
      }

      /// <summary>Revokes this role from a user.</summary>
      public Task RemoveMember(User member) {
         EnsureSaved();
         EnsureNotSystemAccount(member);

         return Service.PostTa_UserRole_Delete(new ta_UserRole {
            cUserId = member.cUserId,
            cRoleId = cRoleId
         });
      }

      #endregion

      #region Content

      /// <summary>
      /// Sends all changes to the content of this role - rights and members - in a single call. The only door
      /// used by the role manager screen; <see cref="AddClaim"/> and its companions remain for callers that
      /// really change only one thing.
      /// <para>
      /// Keeps no state: what is received is the difference, not the desired state, so the caller still holds
      /// the "before" and "after". All of its operations tolerate being repeated - if the submission is cut
      /// off midway, sending the same difference once more finishes it without duplicating anything.
      /// </para>
      /// </summary>
      /// <param name="set">The difference to send. An empty one produces no request at all.</param>
      /// <exception cref="InvalidOperationException">
      /// When this role has never been saved, or when <paramref name="set"/> turns out to belong to another role.
      /// </exception>
      public async Task SaveContentAsync(RoleSet set) {
         ArgumentNullException.ThrowIfNull(set);
         EnsureSaved();

         if (set.cRoleId != cRoleId) {
            throw new InvalidOperationException(
               $"This change set belongs to role '{set.cRoleId}', not to '{cRoleId}'.");
         }

         if (set.IsEmpty) return;

         // Revocations are sent first so a right that is revoked and granted again with a different validity
         // period does not overwrite each other's order.
         if (set.ClaimsRevoked.Length > 0) await Service.PostTa_RoleClaim_DeleteBatch(set.ClaimsRevoked);
         if (set.ClaimsGranted.Length > 0) await Service.PostTa_RoleClaim_NewBatch(set.ClaimsGranted);

         if (set.MembersRemoved.Length > 0) await Service.PostTa_UserRole_DeleteBatch(set.MembersRemoved);
         if (set.MembersAdded.Length > 0) await Service.PostTa_UserRole_NewBatch(set.MembersAdded);

         // One by one, because there is no batch action for validity period changes - and a single save rarely
         // changes the validity period of more than one or two rows.
         foreach (var assignment in set.MembersRescheduled) {
            await Service.PostTa_UserRole_Update(assignment);
         }

         // The number on the rail row moves along without reading the whole page again. Computed from the
         // difference that was just sent, so it cannot drift from what really changed on the server.
         ClaimCount += set.ClaimsGranted.Length - set.ClaimsRevoked.Length;
         MemberCount += set.MembersAdded.Length - set.MembersRemoved.Length;
      }

      #endregion

      #region Methods

      /// <summary>
      /// Deletes this role for real - not deactivates it. The rights it carries and all its assignments
      /// disappear with it, and there is no way to bring them back.
      /// </summary>
      public Task DeleteAsync() {
         EnsureSaved();
         return Service.PostTa_Role_Delete(ToEntity());
      }

      /// <summary>
      /// Creates a new role named <paramref name="newName"/> that carries exactly the same rights as this
      /// role. Its members are <b>not</b> copied: what is copied is the shape of a position, not who is
      /// currently holding it.
      /// </summary>
      public async Task<Role> DuplicateAsync(string newName) {
         EnsureSaved();

         var copy = CreateNewRole(App);
         copy.cRoleName = newName;
         copy.cRoleState = cRoleState;
         copy.cRoleDescription = cRoleDescription;
         await copy.SaveAsync();

         var rows = await Service.GetTa_RoleClaims_ByRoleId(cRoleId);
         if (rows.Length > 0) {
            // One request for all rights, not one per right: a role worth duplicating is precisely one with many
            // rights.
            await Service.PostTa_RoleClaim_NewBatch([
               .. rows.Select(r => new ta_RoleClaim { cRoleId = copy.cRoleId, cClaimName = r.cClaimName })
            ]);
            copy.ClaimCount = rows.Length;
         }

         return copy;
      }

      // The content of a role lives in other rows that point to this role's id, so none of it can be
      // written before that id exists. Refused here so it is clear what is missing, instead of being left to
      // become a foreign key violation from the database over a placeholder id that still reads
      // "Save Role...".
      private void EnsureSaved() {
         if (IsBlank) {
            throw new InvalidOperationException(
               "This role has not been saved yet, so it has no id for its claims and members to point at.");
         }
      }

      // The same reason as the guard of the same name in User: the debugger account and the built-in
      // administrator account stand in for the signed-in user without having a user row of their own, so
      // there is nothing for an assignment row to point to - and they already have the right to everything.
      private static void EnsureNotSystemAccount(User member) {
         if (member.cUserId is Defaults.DebuggerUserId or Defaults.AdminUserId) {
            throw new SystemAccountException(
               $"User '{member.cUserId}' is a system account and cannot be given a role.",
               member.cUserId);
         }
      }

      #endregion
   }
}
