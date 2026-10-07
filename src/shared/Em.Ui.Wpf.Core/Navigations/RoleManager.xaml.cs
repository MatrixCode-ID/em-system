using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Application = System.Windows.Application;
using MessageBoxResult = System.Windows.MessageBoxResult;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class RoleManager : UserControl, INavigationBody
   {
      private readonly EmApp _app;

      /// <summary>Creates a new instance of <see cref="RoleManager"/>.</summary>
      public RoleManager(EmApp app) {
         _app = app;
         InitializeComponent();
         Vm.EmApp = _app;
      }

      /// <summary>The vm.</summary>
      public RoleManagerVm Vm => (RoleManagerVm)DataContext;

      // Nothing is read here: the host raises this on every way into the screen, back and forward
      // included, and those two return to a rail that is already filled. Filling it belongs to
      // OnReloadRequested, which only a real navigation raises.
      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      // The third of the three ways out of an unsaved role - the other two, picking another role
      // and turning the page, are held inside the view model. All three ask the same question and
      // read the same answer; only the way of refusing differs, and here it is this flag.
      /// <inheritdoc />
      public async Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (await Vm.ConfirmLeavePendingAsync()) return;

         args.Cancel = true;
         args.Message = "This role still has changes that have not been saved.";
      }

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>
   /// The role manager screen: a rail with the list of roles on the left, and on the right one open role
   /// together with its rights, members, and notes about it.
   /// <para>
   /// The content of a role is not written directly. What is edited on this screen is access rights, so
   /// revoking one right by a misclick must not be sent right away: this view model holds the
   /// <i>baseline</i> (the state of the role when it was opened) and the <i>desired</i> state, and their
   /// difference is only computed and sent when the save button is pressed.
   /// </para>
   /// </summary>
   public class RoleManagerVm : MvvmModelBase
   {
      private const string DateFormat = "dd MMM yyyy";

      private RoleCollection? _collection;

      // The content of roles that have been read, kept while the screen lives and discarded on reload:
      // comparing two roles means going back and forth between them, and three requests to the server per
      // click is too expensive to pay repeatedly.
      private readonly Dictionary<string, RoleContent> _content = [];

      // The state of the role that is currently open, as stored on the server.
      private readonly HashSet<string> _baselineClaims = new(StringComparer.OrdinalIgnoreCase);
      private readonly Dictionary<string, ta_UserRole> _baselineMembers = [];

      // The difference between baseline and desired, recomputed every time either changes. The number in the
      // action bar is read from here, so there is no second counter that could drift from it.
      private RoleSet? _pending;

      // Server time, read once per reload: the ACTIVE / SCHEDULED / EXPIRED label on every member row is
      // computed against it, and asking per row would mean one trip to the server for everyone in the list.
      private DateTime _serverNow = DateTime.Now;

      // On while the view model itself moves the selection in the rail, so the move guard does not ask again
      // what it just answered.
      private bool _selectionSuspended;

      // On while desired is being rebuilt from baseline, so turning on a hundred checkboxes at once does not
      // recompute the difference a hundred times.
      private bool _rebuildingDesired;

      // Clears the last drop notice. Created once and reused: every new drop restarts its count from the
      // beginning, so dragging several rights in a row does not clear the notice that was just published.
      private DispatcherTimer? _dropFeedbackTimer;

      private Role? _watchedRole;

      /// <summary>Creates a new instance of <see cref="RoleManagerVm"/>.</summary>
      public RoleManagerVm() {
         RegisterCommand(nameof(NewRoleCommand), NewRoleCommand, NewRoleCommandAllowed);
         RegisterCommand(nameof(DuplicateRoleCommand), DuplicateRoleCommand, DuplicateRoleCommandAllowed);
         RegisterCommand(nameof(EditDetailsCommand), EditDetailsCommand, EditDetailsCommandAllowed);
         RegisterCommand(nameof(SaveChangesCommand), SaveChangesCommand, SaveChangesCommandAllowed);
         RegisterCommand(nameof(DiscardCommand), DiscardCommand, DiscardCommandAllowed);
         RegisterCommand<ClaimItemVm?>(nameof(RevokeClaimCommand), RevokeClaimCommand, RevokeClaimCommandAllowed);
         RegisterCommand<ClaimGroupVm?>(nameof(RevokeModuleCommand), RevokeModuleCommand, RevokeModuleCommandAllowed);
         RegisterCommand<object?>(nameof(GrantDroppedCommand), GrantDroppedCommand, GrantDroppedCommandAllowed);
         RegisterCommand<MemberRowVm?>(nameof(RemoveMemberCommand), RemoveMemberCommand, RemoveMemberCommandAllowed);
         RegisterCommand(nameof(DisableRoleCommand), DisableRoleCommand, DisableRoleCommandAllowed);
         RegisterCommand(nameof(DeleteRoleCommand), DeleteRoleCommand, DeleteRoleCommandAllowed);
         RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
         RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);

         // Both are read from the content of the rail, so they follow its content - not whichever path happened
         // to change it. A single draft row entering or leaving is enough to shift the caption at the foot of
         // the rail and remove the "no roles yet" message.
         Roles.CollectionChanged += (_, _) => {
            NotifyChanged(nameof(HasNoRole));
            NotifyChanged(nameof(RailCaption));
         };

         // Same reason: the claim caption is read from the group list, so it follows that list.
         GrantedGroups.CollectionChanged += (_, _) => {
            NotifyChanged(nameof(GrantedSummaryCaption));
            NotifyChanged(nameof(ClaimDetailCaption));
         };

         MemberRows.CollectionChanged += (_, _) => NotifyChanged(nameof(MemberDetailCaption));
         ClaimGroups.CollectionChanged += (_, _) => NotifyChanged(nameof(CatalogueSummaryCaption));
      }

      #region Data

      /// <summary>
      /// The roles shown in the rail: one page from the latest read, plus the draft row that has never been
      /// saved when there is one.
      /// </summary>
      public ObservableCollection<Role> Roles { get; } = [];

      /// <summary>
      /// The whole catalog of rights known to the application, grouped by module. Read once per reload, not
      /// per role - the catalog is the same for any role.
      /// </summary>
      public ObservableCollection<ClaimGroupVm> ClaimGroups { get; } = [];

      /// <summary>
      /// The part of <see cref="ClaimGroups"/> that really carries rights in the open role. This is what the
      /// Permissions tab draws: a module the role has no business with need not take up room there.
      /// </summary>
      public ObservableCollection<ClaimGroupVm> GrantedGroups { get; } = [];

      /// <summary>The members of the open role - the desired state, not the stored one.</summary>
      public ObservableCollection<MemberRowVm> MemberRows { get; } = [];

      /// <summary>
      /// The role currently open in the right pane. Changing it while there are unsaved changes raises a
      /// question first, and the answer is respected: the rail does not move until the changes are really
      /// saved or discarded.
      /// </summary>
      public Role? SelectedRole {
         get => Get<Role?>();
         set {
            var current = Get<Role?>();
            if (ReferenceEquals(current, value)) return;

            if (_selectionSuspended || !HasPendingChanges) {
               Set(value, opened => { _ = OpenRoleAsync(opened); });
               return;
            }

            // The rail has already moved by the time we get here, and the answer to the question may take a trip to
            // the server - so the row is put back first, then really moved only if the changes are indeed settled.
            RestoreSelection();
            _ = MoveSelectionAsync(value);
         }
      }

      /// <summary><c>true</c> when there is no role at all in the rail.</summary>
      public bool HasNoRole => Roles.Count == 0;

      /// <summary><c>true</c> when there is a role open in the right pane.</summary>
      public bool HasSelectedRole => SelectedRole is not null;

      #endregion

      #region Header & details

      /// <summary>
      /// On while the name and description of the role are being edited in the pane header. What closes it is
      /// the save button in the action bar, not a separate OK button - the name follows the same save flow as
      /// rights and members. For a new role this mode is forced on: the role name is required.
      /// </summary>
      public bool IsEditingDetails {
         get => Get<bool>();
         private set => Set(value, _ => RaiseCommandsChanged());
      }

      /// <summary>Short description of the state of the open role, for the chip beside its name.</summary>
      public string StateCaption => SelectedRole?.cRoleState == RoleState.Active ? "IN USE" : "OFF";

      /// <summary>Caption of the member count of the open role, e.g. "4 members".</summary>
      public string MemberCountCaption => Plural(SelectedRole?.MemberCount ?? 0, "member");

      /// <summary>Caption of the right count of the open role, e.g. "12 claims granted".</summary>
      public string ClaimCountCaption => $"{Plural(SelectedRole?.ClaimCount ?? 0, "claim")} granted";

      /// <summary>When the open role last changed, as recorded by its own row.</summary>
      public string LastChangedCaption =>
         SelectedRole is { IsBlank: false } role && role.ustamp != default
            ? role.ustamp.ToString($"{DateFormat}, HH:mm")
            : "Not saved yet";

      /// <summary>
      /// The complete sentence for the fact row in the header. Composed here, not assembled in XAML from a
      /// piece of a word and a piece of a value: a row that has never been saved has no "when" to mention,
      /// and attaching the words "Last changed" in front of it would produce a meaningless sentence.
      /// </summary>
      public string LastChangedLine =>
         SelectedRole is { IsBlank: false } ? $"Last changed {LastChangedCaption}" : "Not saved yet";

      /// <summary>Id of the open role, or a human-readable placeholder while its id has not been issued.</summary>
      public string IdentifierCaption => SelectedRole?.cRoleId ?? string.Empty;

      /// <summary>State of the open role, spelled out in full for the Details tab.</summary>
      public string StateDetailCaption => SelectedRole?.cRoleState == RoleState.Active ? "In use" : "Disabled";

      /// <summary>Member summary for the Details tab, e.g. "4 accounts, 1 of them scheduled".</summary>
      public string MemberDetailCaption {
         get {
            if (SelectedRole is null) return string.Empty;

            var accounts = Plural(MemberRows.Count, "account");
            var scheduled = MemberRows.Count(r => r.State == MemberPeriodState.Scheduled);
            return scheduled == 0 ? accounts : $"{accounts}, {scheduled} of them scheduled";
         }
      }

      /// <summary>Right summary for the Details tab, e.g. "12 claims, from 4 modules".</summary>
      public string ClaimDetailCaption {
         get {
            if (SelectedRole is null) return string.Empty;

            var claims = GrantedGroups.Sum(g => g.GrantedCount);
            return claims == 0
               ? "No claim granted"
               : $"{Plural(claims, "claim")}, from {Plural(GrantedGroups.Count, "module")}";
         }
      }

      /// <summary>Caption of the right count for the Permissions tab header, e.g. "12 in 5 modules".</summary>
      public string GrantedSummaryCaption {
         get {
            var claims = GrantedGroups.Sum(g => g.GrantedCount);
            return claims == 0 ? "none yet" : $"{claims} in {Plural(GrantedGroups.Count, "module")}";
         }
      }

      /// <summary>Caption of the catalog content for the foot of the catalog sheet, e.g. "20 claims in 5 modules".</summary>
      public string CatalogueSummaryCaption =>
         $"{Plural(ClaimGroups.Sum(g => g.Claims.Count), "claim")} in {Plural(ClaimGroups.Count, "module")}";

      /// <summary>
      /// The reason the Permissions and Members tabs are locked while the role has not been saved. Not just
      /// turning the tab off: what needs to be read is why, not merely that it is off.
      /// </summary>
      public string DraftLockCaption {
         get {
            var pendingClaims = _pending?.ClaimsGranted.Length ?? 0;
            return pendingClaims == 0
               ? "A role has to be saved before it can carry anything: its claims and its members point at an id that has not been issued yet."
               : $"A role has to be saved before it can carry anything. The {Plural(pendingClaims, "claim")} copied from the original will be granted the moment this role is saved.";
         }
      }

      /// <summary>
      /// Opens or collapses all module groups on the Permissions tab at once. A group that the reader has
      /// collapsed by hand afterwards parts ways with this button until it is pressed again - exactly as the
      /// text on the button promises.
      /// </summary>
      public bool ArePermissionGroupsExpanded {
         get => Get(true);
         set => Set(value, expanded => {
            foreach (var group in ClaimGroups) group.IsExpanded = expanded;
         });
      }

      /// <summary>
      /// Its counterpart for the catalog sheet. Kept separate because they are really two different buttons
      /// on two different lists.
      /// </summary>
      public bool AreCatalogueGroupsExpanded {
         get => Get(true);
         set => Set(value, expanded => {
            foreach (var group in ClaimGroups) group.IsCatalogueExpanded = expanded;
         });
      }

      /// <summary>
      /// What happened in the last drop, e.g. "3 of 5 claims from core.contact granted".
      /// <para>
      /// The number in the tab header already moves by itself, but a drop that changes nothing - a right
      /// that is already held, a module that is already full - moves nothing either, and without a word it
      /// reads as a failed drag.
      /// </para>
      /// </summary>
      public string DropFeedbackCaption {
         get => Get(string.Empty);
         set => Set(value, _ => NotifyChanged(nameof(HasDropFeedback)));
      }

      /// <summary>Whether there is a drop notice that still needs to be shown.</summary>
      public bool HasDropFeedback => !string.IsNullOrEmpty(DropFeedbackCaption);

      #endregion

      #region Paging & counters

      /// <summary>Total number of roles on the server, for the chip in the toolbar.</summary>
      public string TotalCaption => Plural(_collection?.TotalCount ?? 0, "role");

      /// <summary>Total number of role assignments on the server, for the chip in the toolbar.</summary>
      public string AssignmentCaption => Plural(_collection?.TotalAssignments ?? 0, "assignment");

      /// <summary>Caption of the rail content, e.g. "6 of 24 roles".</summary>
      public string RailCaption {
         get {
            var total = _collection?.TotalCount ?? 0;
            return total == 0 ? "No role to show" : $"{Roles.Count} of {Plural(total, "role")}";
         }
      }

      private int PageCount {
         get {
            if (_collection is not { TotalCount: > 0, PageSize: > 0 }) return 1;

            var pages = (_collection.TotalCount + _collection.PageSize - 1) / _collection.PageSize;
            return pages < 1 ? 1 : pages;
         }
      }

      #endregion

      #region Pending changes

      /// <summary>
      /// <c>true</c> when the open role has anything unsaved - a draft row that has never been born, a role
      /// column that changed, or a difference in rights and members.
      /// </summary>
      public bool HasPendingChanges =>
         SelectedRole is { } role && (role.IsBlank || role.IsDirty || _pending is { IsEmpty: false });

      /// <summary>Sentence in the action bar naming everything that changed since this role was opened.</summary>
      public string ChangeCaption {
         get {
            if (SelectedRole is not { } role) return string.Empty;
            if (role.IsBlank) return "This role has not been saved yet, so nothing it carries can be written.";

            var parts = new List<string>();
            if (role.IsDirty) parts.Add("the details");
            if (_pending is { ClaimChangeCount: > 0 } claims) parts.Add(Plural(claims.ClaimChangeCount, "claim"));
            if (_pending is { AssignmentChangeCount: > 0 } members) parts.Add(Plural(members.AssignmentChangeCount, "assignment"));

            if (parts.Count == 0) return "Nothing has changed since this role was opened.";

            var subject = parts.Count switch {
               1 => parts[0],
               2 => $"{parts[0]} and {parts[1]}",
               _ => $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}"
            };

            return $"{char.ToUpper(subject[0])}{subject[1..]} changed since this role was opened.";
         }
      }

      #endregion

      #region Commands

      /// <summary>
      /// Creates a new role without a dialog: its row appears at the top of the rail, lives in memory, and is
      /// only born in the database when the save button is pressed.
      /// </summary>
      public async Task NewRoleCommand() {
         if (EmApp == null || !await ConfirmLeavePendingAsync()) return;

         AddDraft(Role.CreateNewRole(EmApp));
      }

      /// <summary>May only run when the list is loaded and nothing else is running.</summary>
      public bool NewRoleCommandAllowed() => _collection is not null && !IsBusy;

      /// <summary>
      /// Duplicates the open role into a new draft row: its name, description, and all its rights come along,
      /// its members do not - what is duplicated is the shape of a position, not who is currently holding it.
      /// Like New Role, nothing touches the server until the save button is pressed.
      /// </summary>
      public async Task DuplicateRoleCommand() {
         if (EmApp == null || SelectedRole is not { IsBlank: false } source) return;

         // Read now, while the source role is still open: as soon as the draft is selected, the desired state
         // on screen belongs to that draft.
         var claims = ClaimGroups.SelectMany(g => g.Claims).Where(c => c.IsGranted).Select(c => c.Key).ToArray();

         if (!await ConfirmLeavePendingAsync()) return;

         var draft = Role.CreateNewRole(EmApp);
         draft.cRoleName = $"{source.cRoleName} (copy)";
         draft.cRoleDescription = source.cRoleDescription;
         draft.cRoleState = source.cRoleState;
         draft.UiIcon = source.UiIcon;

         AddDraft(draft, claims);
      }

      /// <summary>May only run when what is open is a role that has been saved.</summary>
      public bool DuplicateRoleCommandAllowed() => SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>Opens the edit mode of the name and description in the pane header.</summary>
      public void EditDetailsCommand() => IsEditingDetails = true;

      /// <summary>
      /// May only run when there is an open role that has been saved and the mode is not on yet. For a new
      /// role the button is off - the mode is already on and cannot be turned off.
      /// </summary>
      public bool EditDetailsCommandAllowed() => SelectedRole is { IsBlank: false } && !IsEditingDetails;

      /// <summary>
      /// Sends all changes of the open role: its own columns through the ordinary route, its content through
      /// the single door <see cref="Role.SaveContentAsync"/>.
      /// <para>
      /// If the sending fails, nothing on screen is touched: baseline stays baseline, desired stays desired,
      /// and pressing save once more computes exactly the same difference and sends it again.
      /// </para>
      /// </summary>
      public async Task SaveChangesCommand() {
         if (EmApp == null || SelectedRole is not { } role) return;

         try {
            WaiterText = "Saving role...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            var wasBlank = role.IsBlank;
            if (wasBlank && _collection != null) await _collection.NewRoleAsync(role);
            else if (wasBlank || role.IsDirty) await role.SaveAsync();

            var stamp = await EmApp.GetDateStampAsync();
            var set = BuildRoleSet(role, stamp);
            if (!set.IsEmpty) await role.SaveContentAsync(set);

            AcceptAsBaseline(role, set, stamp);
            IsEditingDetails = false;

            if (wasBlank) _content.Remove(role.cRoleId);
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RefreshPendingState();
            RaiseRoleDerived();
         }
      }

      /// <summary>May only run when there really is something unsaved.</summary>
      public bool SaveChangesCommandAllowed() => HasPendingChanges && !IsBusy;

      /// <summary>
      /// Returns the open role to the state it had when opened, and closes the name and description edit
      /// mode. For a draft row this means removing its row from the rail:
      /// <see cref="UiModel{TEntity,TService}.RollBack"/> only returns the columns to their original data, and
      /// the original data of a draft is an empty row holding a placeholder - discarding the object is the
      /// screen's business.
      /// </summary>
      public void DiscardCommand() {
         if (SelectedRole is not { } role) return;

         if (role.IsBlank) {
            RemoveDraft(role);
            return;
         }

         role.RollBack();
         RebuildDesired(role);
         IsEditingDetails = false;
         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>
      /// May run when there is something that can be cancelled - and also while the name and description edit
      /// mode is on even if nothing has been typed yet, because this button is the only way to close that mode
      /// in place. Without that, pressing Edit details and then changing one's mind would turn off all three
      /// buttons at once, and cancelling the intention would have to be paid for by leaving the open role.
      /// </summary>
      public bool DiscardCommandAllowed() => (HasPendingChanges || IsEditingDetails) && !IsBusy;

      /// <summary>
      /// Revokes one right from the open role - on screen only. What changes is the desired state; the server
      /// only hears about it when the save button is pressed.
      /// </summary>
      public void RevokeClaimCommand(ClaimItemVm? claim) {
         if (claim is not null) claim.IsGranted = false;
      }

      /// <summary>May only run for a right that is actually held.</summary>
      public bool RevokeClaimCommandAllowed(ClaimItemVm? claim) =>
         claim is { IsGranted: true } && SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Revokes all rights of one module from the open role at once - the opposite of dropping a module card
      /// onto this tab, and like single revocation it only moves the desired state. The card vanishes from the
      /// Permissions tab as soon as its last right is released, because this tab only draws modules the role
      /// has business with. Not asked first: nothing is sent until the save button is pressed, and Discard
      /// restores everything.
      /// </summary>
      public void RevokeModuleCommand(ClaimGroupVm? group) {
         if (group is null) return;

         // Copied first: Granted is rebuilt every time one right goes away, so revoking while walking through it
         // would mean walking a list that shrinks under one's own hands.
         var held = group.Granted.ToArray();
         if (held.Length == 0) return;

         _rebuildingDesired = true;
         try {
            foreach (var claim in held) claim.IsGranted = false;
         }
         finally {
            _rebuildingDesired = false;
         }

         group.RefreshGranted();
         RefreshGrantedGroups();
         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>May only run for a module that is actually carrying rights.</summary>
      public bool RevokeModuleCommandAllowed(ClaimGroupVm? group) =>
         group is { GrantedCount: > 0 } && SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Grants the right that was just dragged from the catalog to the open role - on screen only, like
      /// revocation. The payload may be one right (<see cref="ClaimItemVm"/>) or one whole module
      /// (<see cref="ClaimGroupVm"/>), and both come in through the same door because what is being dragged
      /// is really one thing: whatever is under the cursor.
      /// </summary>
      public void GrantDroppedCommand(object? payload) {
         switch (payload) {
            case ClaimItemVm claim:
               GrantOne(claim);
               break;

            case ClaimGroupVm group:
               GrantModule(group);
               break;
         }
      }

      /// <summary>
      /// May run for a payload this screen recognizes, as long as a saved role is open. A module whose rights
      /// are all already held is still answered as allowed: it will not change anything, but that is said
      /// after the drop, not with a "not allowed" cursor that makes people think their drag failed.
      /// </summary>
      public bool GrantDroppedCommandAllowed(object? payload) =>
         payload is ClaimItemVm or ClaimGroupVm && SelectedRole is { IsBlank: false } && !IsBusy;

      // One right: turning its switch on is enough, because the setter itself rebuilds the Permissions tab
      // and recomputes the difference.
      private void GrantOne(ClaimItemVm claim) {
         if (claim.IsGranted) {
            ShowDropFeedback($"{claim.Key} is already on this role");
            return;
         }

         claim.IsGranted = true;
         ShowDropFeedback($"{claim.Key} granted");
      }

      // One module: the ones not yet held are collected first, then turned on together. Without that, the tab
      // refresh and the difference computation are paid once per right - and what the person sees stays
      // exactly the same.
      private void GrantModule(ClaimGroupVm group) {
         var wanted = group.Claims.Where(c => !c.IsGranted).ToArray();
         if (wanted.Length == 0) {
            ShowDropFeedback($"{group.ModuleName} is already fully granted");
            return;
         }

         _rebuildingDesired = true;
         try {
            foreach (var claim in wanted) claim.IsGranted = true;
         }
         finally {
            _rebuildingDesired = false;
         }

         group.RefreshGranted();
         RefreshGrantedGroups();
         RefreshPendingState();
         RaiseRoleDerived();

         ShowDropFeedback(
            $"{wanted.Length:N0} of {Plural(group.Claims.Count, "claim")} from {group.ModuleName} granted");
      }

      private void ShowDropFeedback(string caption) {
         DropFeedbackCaption = caption;

         _dropFeedbackTimer ??= BuildDropFeedbackTimer();
         _dropFeedbackTimer.Stop();
         _dropFeedbackTimer.Start();
      }

      private DispatcherTimer BuildDropFeedbackTimer() {
         var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
         timer.Tick += (_, _) => ClearDropFeedback();
         return timer;
      }

      // Also called when the role changes and when the screen is read again: a sentence naming the previous
      // role must not linger on the screen of the next role.
      private void ClearDropFeedback() {
         _dropFeedbackTimer?.Stop();
         DropFeedbackCaption = string.Empty;
      }

      /// <summary>
      /// Removes one member from the open role - on screen only, like revoking a right.
      /// </summary>
      public void RemoveMemberCommand(MemberRowVm? member) {
         if (member is null || !MemberRows.Remove(member)) return;

         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>May only run for a member row that is really in the list.</summary>
      public bool RemoveMemberCommandAllowed(MemberRowVm? member) =>
         member is not null && SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Deactivates the open role, or turns it back on. Both only change the state column, so they follow the
      /// same save flow as the whole content of this screen - not a command that is sent by itself right away.
      /// </summary>
      public void DisableRoleCommand() {
         if (SelectedRole is not { } role) return;

         role.cRoleState = role.cRoleState == RoleState.Active ? RoleState.Disabled : RoleState.Active;
         RefreshPendingState();
         RaiseRoleDerived();
      }

      /// <summary>May only run for a role that has been saved.</summary>
      public bool DisableRoleCommandAllowed() => SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>
      /// Deletes the open role for real, together with all its rights and assignments. Asked first, and not
      /// through the action bar: it is the one thing on this screen that cannot be undone.
      /// </summary>
      public async Task DeleteRoleCommand() {
         if (_collection == null || SelectedRole is not { IsBlank: false } role) return;

         var owner = DialogOwner;
         if (owner == null) return;

         var answer = owner.ShowMboxDecideWarning(
            $"Delete the role '{role.cRoleName}'? It will be taken off the {Plural(role.MemberCount, "account")} that carry it, and this cannot be undone.",
            "Delete role");
         if (answer != MessageBoxResult.Yes) return;

         try {
            WaiterText = "Deleting role...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            await _collection.RemoveAsync(role);
            _content.Remove(role.cRoleId);
            Roles.Remove(role);
            await SelectFirstRoleAsync();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseListDerived();
         }
      }

      /// <summary>May only run for a role that has been saved.</summary>
      public bool DeleteRoleCommandAllowed() => SelectedRole is { IsBlank: false } && !IsBusy;

      /// <summary>Goes back one page in the rail.</summary>
      public Task PreviousPageCommand() => GoToPageAsync((_collection?.Page ?? 1) - 1);

      /// <summary>May only run when there is still a page before the open page.</summary>
      public bool PreviousPageCommandAllowed() => _collection is { Page: > 1 } && !IsBusy;

      /// <summary>Goes forward one page in the rail.</summary>
      public Task NextPageCommand() => GoToPageAsync((_collection?.Page ?? 1) + 1);

      /// <summary>May only run when there is still a page after the open page.</summary>
      public bool NextPageCommandAllowed() => _collection is not null && _collection.Page < PageCount && !IsBusy;

      #endregion

      #region Reload

      /// <summary>
      /// Reads the whole content of the screen again: one page of roles, the catalog of rights, and their
      /// numbers. Role content that was cached is discarded too - what is asked for is the latest state, not
      /// what happened to be read a moment ago.
      /// </summary>
      public async Task ReloadAsync() {
         if (EmApp == null || IsBusy) return;

         try {
            WaiterText = "Loading roles...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            ClearDropFeedback();
            _content.Clear();
            _serverNow = await EmApp.GetDateStampAsync();

            if (_collection == null) _collection = await RoleCollection.LoadAsync(EmApp);
            else await _collection.ReloadAsync();

            await BuildClaimCatalogueAsync();

            _selectionSuspended = true;
            try {
               Set<Role?>(null, null, nameof(SelectedRole));
               Roles.Clear();
               foreach (var role in _collection) Roles.Add(role);
            }
            finally {
               _selectionSuspended = false;
            }

            await SelectFirstRoleAsync();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseListDerived();
         }
      }

      // The catalog itself does not change while the application runs - the module declares it when the
      // server starts - but the "held by how many roles" number changes every time someone saves, so what is
      // read again on each reload is the number, not the catalog.
      private async Task BuildClaimCatalogueAsync() {
         var service = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();
         var usage = (await service.GetMeta_ClaimUsage())
            .ToDictionary(r => r.ClaimKey, r => r.RoleCount, StringComparer.OrdinalIgnoreCase);

         ClaimGroups.Clear();
         GrantedGroups.Clear();

         var groups = EmApp.AllClaims
            .GroupBy(c => c.ModuleName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

         foreach (var group in groups) {
            var item = new ClaimGroupVm(group.Key, group.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase), usage, OnClaimChanged);
            ClaimGroups.Add(item);
         }
      }

      #endregion

      #region Opening a role

      // The baseline is read once per role and then kept; desired is built from that baseline. What XAML
      // binds to is desired, so ticking and un-ticking the same box automatically returns to zero changes
      // without any list of intentions that would have to be found and cleaned.
      private async Task OpenRoleAsync(Role? role) {
         WatchRole(role);
         ClearDropFeedback();

         if (role == null) {
            ClearContent();
            RaiseRoleDerived();
            return;
         }

         IsEditingDetails = role.IsBlank;

         if (role.IsBlank) {
            // A draft has nothing on the server yet - its baseline is empty, and its desired state is whatever the
            // caller already put in (e.g. the result of duplicating another role).
            _baselineClaims.Clear();
            _baselineMembers.Clear();
            RefreshPendingState();
            RaiseRoleDerived();
            return;
         }

         // Called on its own or from the middle of a reload; whoever arrives first installs the wait layer, so
         // the second does not remove it while the first is still running.
         var ownsWaiting = false;

         try {
            if (!_content.TryGetValue(role.cRoleId, out var content)) {
               if (!IsBusy) {
                  ownsWaiting = true;
                  WaiterText = "Loading role content...";
                  InWaiting = IsBusy = true;
                  RaiseCommandsChanged();
               }

               content = new RoleContent(
                  await role.GetRoleClaims(),
                  await role.GetMembers(),
                  await role.GetAssignments());
               _content[role.cRoleId] = content;
            }

            _baselineClaims.Clear();
            foreach (var claim in content.Claims) _baselineClaims.Add(claim.Key);

            _baselineMembers.Clear();
            foreach (var assignment in content.Assignments) _baselineMembers[assignment.cUserId] = assignment;

            RebuildDesired(role, content);
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            if (ownsWaiting) InWaiting = IsBusy = false;
            RefreshPendingState();
            RaiseRoleDerived();
         }
      }

      private void RebuildDesired(Role role, RoleContent? content = null) {
         if (role.IsBlank) return;
         if (content == null && !_content.TryGetValue(role.cRoleId, out content)) return;

         _rebuildingDesired = true;
         try {
            foreach (var claim in ClaimGroups.SelectMany(g => g.Claims))
               claim.IsGranted = _baselineClaims.Contains(claim.Key);

            MemberRows.Clear();
            foreach (var member in content.Members) {
               _baselineMembers.TryGetValue(member.cUserId, out var assignment);
               MemberRows.Add(new MemberRowVm(member, assignment, _serverNow));
            }
         }
         finally {
            _rebuildingDesired = false;
         }

         RefreshGrantedGroups();
      }

      private void ClearContent() {
         _baselineClaims.Clear();
         _baselineMembers.Clear();
         _pending = null;

         _rebuildingDesired = true;
         try {
            foreach (var claim in ClaimGroups.SelectMany(g => g.Claims)) claim.IsGranted = false;
            MemberRows.Clear();
         }
         finally {
            _rebuildingDesired = false;
         }

         RefreshGrantedGroups();
         IsEditingDetails = false;
      }

      // The role itself reports when its name or state changes, so the counter in the action bar and the
      // save button move along while the person is still typing.
      private void WatchRole(Role? role) {
         if (ReferenceEquals(_watchedRole, role)) return;

         if (_watchedRole != null) _watchedRole.PropertyChanged -= OnSelectedRolePropertyChanged;
         _watchedRole = role;
         if (_watchedRole != null) _watchedRole.PropertyChanged += OnSelectedRolePropertyChanged;
      }

      private void OnSelectedRolePropertyChanged(object? sender, PropertyChangedEventArgs e) {
         RaiseRoleDerived();
         if (e.PropertyName is nameof(Role.IsDirty) or nameof(Role.IsBlank)) RaiseCommandsChanged();
      }

      #endregion

      #region Drafts

      private void AddDraft(Role draft, IReadOnlyCollection<string>? claims = null) {
         _selectionSuspended = true;
         try {
            Roles.Insert(0, draft);
            Set<Role?>(draft, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         WatchRole(draft);
         _baselineClaims.Clear();
         _baselineMembers.Clear();

         _rebuildingDesired = true;
         try {
            var wanted = claims?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var claim in ClaimGroups.SelectMany(g => g.Claims))
               claim.IsGranted = wanted.Contains(claim.Key);

            MemberRows.Clear();
         }
         finally {
            _rebuildingDesired = false;
         }

         RefreshGrantedGroups();
         IsEditingDetails = true;
         RefreshPendingState();
         RaiseRoleDerived();
         RaiseListDerived();
      }

      private void RemoveDraft(Role draft) {
         _selectionSuspended = true;
         try {
            Roles.Remove(draft);
            Set<Role?>(null, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         _pending = null;
         ClearContent();
         WatchRole(null);
         _ = SelectFirstRoleAsync();
         RaiseListDerived();
      }

      private Task SelectFirstRoleAsync() {
         var first = Roles.FirstOrDefault();

         _selectionSuspended = true;
         try {
            Set<Role?>(first, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         return OpenRoleAsync(first);
      }

      #endregion

      #region The guard on leaving unsaved changes

      /// <summary>
      /// Asks what to do about unsaved changes, then carries out the answer. Used by all three ways out of a
      /// role: moving to another row in the rail, moving to another page, and moving to another navigation.
      /// </summary>
      /// <returns>
      /// <c>true</c> when the screen may be left - either because nothing was held back, because the changes
      /// were saved, or because the person chose to discard them.
      /// </returns>
      public async Task<bool> ConfirmLeavePendingAsync() {
         if (!HasPendingChanges) return true;

         var owner = DialogOwner;
         if (owner == null) return true;

         var answer = owner.ShowMboxDecideCancel(
            $"{ChangeCaption}\n\nSave the changes before leaving this role?\n\nYes saves them, No throws them away, Cancel stays here.",
            "Unsaved changes");

         switch (answer) {
            case MessageBoxResult.Yes:
               await SaveChangesCommand();

               // A failed save must not read like a successful one: if anything is still held back, the screen stays
               // where it is and its changes remain whole to try again.
               return !HasPendingChanges;

            case MessageBoxResult.No:
               DiscardCommand();
               return true;

            default:
               return false;
         }
      }

      private async Task MoveSelectionAsync(Role? target) {
         if (!await ConfirmLeavePendingAsync()) return;

         // The discarded draft already carried its choice over to another row, so a target that is no longer in
         // the rail is not chased anymore.
         if (target != null && !Roles.Contains(target)) return;

         _selectionSuspended = true;
         try {
            Set(target, null, nameof(SelectedRole));
         }
         finally {
            _selectionSuspended = false;
         }

         await OpenRoleAsync(target);
      }

      private async Task GoToPageAsync(int page) {
         if (_collection == null || IsBusy) return;
         if (page < 1 || page > PageCount) return;
         if (!await ConfirmLeavePendingAsync()) return;

         try {
            WaiterText = "Loading roles...";
            InWaiting = IsBusy = true;
            RaiseCommandsChanged();

            _content.Clear();
            await _collection.GoToPageAsync(page);

            _selectionSuspended = true;
            try {
               Set<Role?>(null, null, nameof(SelectedRole));
               Roles.Clear();
               foreach (var role in _collection) Roles.Add(role);
            }
            finally {
               _selectionSuspended = false;
            }

            await SelectFirstRoleAsync();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            RaiseListDerived();
         }
      }

      // The rail has already moved its highlight by itself before the setter was called, and the value in the
      // view model did not change - so the only thing that puts it back is the notification. Through the
      // dispatcher, so the click that is running finishes first.
      private void RestoreSelection() {
         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher == null) {
            NotifyChanged(nameof(SelectedRole));
            return;
         }

         dispatcher.BeginInvoke(() => NotifyChanged(nameof(SelectedRole)), DispatcherPriority.Background);
      }

      #endregion

      #region Baseline versus desired

      private void OnClaimChanged(ClaimItemVm claim) {
         if (_rebuildingDesired) return;

         RefreshGrantedGroups();
         RefreshPendingState();
         RaiseRoleDerived();
      }

      // The Permissions tab only draws modules this role really has business with. The order follows the
      // catalog, so a module always appears in the same place whether it has just received its first right
      // or has long been there.
      private void RefreshGrantedGroups() {
         var wanted = ClaimGroups.Where(g => g.GrantedCount > 0).ToArray();

         for (var i = GrantedGroups.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(GrantedGroups[i])) GrantedGroups.RemoveAt(i);
         }

         for (var i = 0; i < wanted.Length; i++) {
            if (i < GrantedGroups.Count && ReferenceEquals(GrantedGroups[i], wanted[i])) continue;
            GrantedGroups.Insert(i, wanted[i]);
         }
      }

      private void RefreshPendingState() {
         _pending = SelectedRole is { } role ? BuildRoleSet(role, _serverNow) : null;

         NotifyChanged(nameof(HasPendingChanges));
         NotifyChanged(nameof(ChangeCaption));
         NotifyChanged(nameof(DraftLockCaption));
         RaiseCommandsChanged();
      }

      // The difference of desired against baseline. For a draft row the id that comes along is still a
      // placeholder - that is fine, because the only thing that reads it before the role is born is the
      // counter in the action bar; what is really sent is rebuilt after its id has been issued.
      private RoleSet BuildRoleSet(Role role, DateTime stamp) {
         var desired = ClaimGroups
            .SelectMany(g => g.Claims)
            .Where(c => c.IsGranted)
            .Select(c => c.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

         var granted = desired
            .Where(key => !_baselineClaims.Contains(key))
            .Select(key => new ta_RoleClaim { cRoleId = role.cRoleId, cClaimName = key })
            .ToArray();

         var revoked = _baselineClaims
            .Where(key => !desired.Contains(key))
            .Select(key => new ta_RoleClaim { cRoleId = role.cRoleId, cClaimName = key })
            .ToArray();

         var added = new List<ta_UserRole>();
         var rescheduled = new List<ta_UserRole>();

         foreach (var row in MemberRows) {
            if (!_baselineMembers.TryGetValue(row.User.cUserId, out var original)) {
               added.Add(new ta_UserRole {
                  cUserId = row.User.cUserId,
                  cRoleId = role.cRoleId,
                  cUserRoleStart = row.Start,
                  cUserRoleExpiry = row.Expiry,
                  ustamp = stamp,
                  datestamp = stamp
               });
               continue;
            }

            if (original.cUserRoleStart == row.Start && original.cUserRoleExpiry == row.Expiry) continue;

            // The datestamp of the old row is carried over as-is: it records when this assignment was first
            // created, and writing "now" over it would read as if this person had just been given the role.
            rescheduled.Add(new ta_UserRole {
               cUserId = original.cUserId,
               cRoleId = role.cRoleId,
               cUserRoleStart = row.Start,
               cUserRoleExpiry = row.Expiry,
               ustamp = stamp,
               datestamp = original.datestamp
            });
         }

         var kept = MemberRows.Select(r => r.User.cUserId).ToHashSet(StringComparer.Ordinal);
         var removed = _baselineMembers.Values
            .Where(r => !kept.Contains(r.cUserId))
            .Select(r => new ta_UserRole { cUserId = r.cUserId, cRoleId = role.cRoleId })
            .ToArray();

         return new RoleSet {
            cRoleId = role.cRoleId,
            ClaimsGranted = granted,
            ClaimsRevoked = revoked,
            MembersAdded = [.. added],
            MembersRemoved = removed,
            MembersRescheduled = [.. rescheduled]
         };
      }

      // Called only after sending has really succeeded: from this point the desired state is the stored
      // state, so the counter returns to zero by itself.
      private void AcceptAsBaseline(Role role, RoleSet set, DateTime stamp) {
         foreach (var claim in set.ClaimsRevoked) _baselineClaims.Remove(claim.cClaimName);
         foreach (var claim in set.ClaimsGranted) _baselineClaims.Add(claim.cClaimName);

         foreach (var member in set.MembersRemoved) _baselineMembers.Remove(member.cUserId);
         foreach (var member in set.MembersAdded) _baselineMembers[member.cUserId] = member;
         foreach (var member in set.MembersRescheduled) _baselineMembers[member.cUserId] = member;

         foreach (var row in MemberRows) {
            if (_baselineMembers.TryGetValue(row.User.cUserId, out var assignment)) row.Accept(assignment, stamp);
         }

         _content[role.cRoleId] = new RoleContent(
            [.. _baselineClaims.Select(ClaimAction.FromKey)],
            [.. MemberRows.Select(r => r.User)],
            [.. _baselineMembers.Values]);
      }

      #endregion

      #region Methods

      private static string Plural(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}";

      private void RaiseListDerived() {
         NotifyChanged(nameof(HasNoRole));
         NotifyChanged(nameof(TotalCaption));
         NotifyChanged(nameof(AssignmentCaption));
         NotifyChanged(nameof(RailCaption));
         NotifyChanged(nameof(CatalogueSummaryCaption));
         RaiseCommandsChanged();
      }

      private void RaiseRoleDerived() {
         NotifyChanged(nameof(HasSelectedRole));
         NotifyChanged(nameof(StateCaption));
         NotifyChanged(nameof(StateDetailCaption));
         NotifyChanged(nameof(MemberCountCaption));
         NotifyChanged(nameof(ClaimCountCaption));
         NotifyChanged(nameof(LastChangedCaption));
         NotifyChanged(nameof(LastChangedLine));
         NotifyChanged(nameof(IdentifierCaption));
         NotifyChanged(nameof(MemberDetailCaption));
         NotifyChanged(nameof(ClaimDetailCaption));
         NotifyChanged(nameof(GrantedSummaryCaption));
      }

      private void RaiseCommandsChanged() {
         foreach (var command in Commands) command.RaiseCanExecuteChanged();
      }

      #endregion

      // The content of one role as read from the server. Kept whole so moving to another role and coming
      // back does not mean reading it again.
      private sealed record RoleContent(ClaimAction[] Claims, User[] Members, ta_UserRole[] Assignments);
   }

   /// <summary>
   /// One module in the rights list: its name, all rights it declares, and the part of those rights that
   /// is held by the open role.
   /// </summary>
   public class ClaimGroupVm : NotifyPropertyBase
   {
      private readonly Action<ClaimItemVm> _onClaimChanged;

      internal ClaimGroupVm(
         string moduleName,
         IEnumerable<ClaimAction> claims,
         IReadOnlyDictionary<string, int> usage,
         Action<ClaimItemVm> onClaimChanged) {

         _onClaimChanged = onClaimChanged;
         ModuleName = string.IsNullOrWhiteSpace(moduleName) ? "(no module)" : moduleName;

         foreach (var claim in claims) {
            Claims.Add(new ClaimItemVm(claim, usage.GetValueOrDefault(claim.Key), OnClaimChanged));
         }

         IsExpanded = true;
         IsCatalogueExpanded = true;
      }

      /// <summary>Name of the module that declares the rights in this group.</summary>
      public string ModuleName { get; }

      /// <summary>All rights this module declares - this is what the catalog sheet draws.</summary>
      public ObservableCollection<ClaimItemVm> Claims { get; } = [];

      /// <summary>
      /// The part of <see cref="Claims"/> that is held by the open role - this is what the Permissions tab
      /// draws, which only talks about rights this role carries.
      /// </summary>
      public ObservableCollection<ClaimItemVm> Granted { get; } = [];

      /// <summary>Number of rights of this module held by the open role.</summary>
      public int GrantedCount => Granted.Count;

      /// <summary>The number on the group badge on the Permissions tab.</summary>
      public string GrantedCaption => $"{GrantedCount:N0}";

      /// <summary>The number on the group badge on the catalog sheet, e.g. "3 of 4".</summary>
      public string CatalogueCaption => $"{GrantedCount:N0} of {Claims.Count:N0}";

      /// <summary>Open or collapsed on the Permissions tab.</summary>
      public bool IsExpanded {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Open or collapsed on the catalog sheet. Kept apart from <see cref="IsExpanded"/> because the two have
      /// their own "open all" button, and a module collapsed in one list is not necessarily wanted collapsed
      /// in the other.
      /// </summary>
      public bool IsCatalogueExpanded {
         get => Get<bool>();
         set => Set(value);
      }

      private void OnClaimChanged(ClaimItemVm claim) {
         RefreshGranted();
         _onClaimChanged(claim);
      }

      internal void RefreshGranted() {
         var wanted = Claims.Where(c => c.IsGranted).ToArray();

         for (var i = Granted.Count - 1; i >= 0; i--) {
            if (!wanted.Contains(Granted[i])) Granted.RemoveAt(i);
         }

         for (var i = 0; i < wanted.Length; i++) {
            if (i < Granted.Count && ReferenceEquals(Granted[i], wanted[i])) continue;
            Granted.Insert(i, wanted[i]);
         }

         NotifyChanged(nameof(GrantedCount));
         NotifyChanged(nameof(GrantedCaption));
         NotifyChanged(nameof(CatalogueCaption));
      }
   }

   /// <summary>
   /// One right in the list: its declaration, how many roles carry it, and whether the open role
   /// <i>wants</i> to carry it - not whether it already carries it according to the server.
   /// </summary>
   public class ClaimItemVm : NotifyPropertyBase
   {
      private readonly Action<ClaimItemVm> _onChanged;

      internal ClaimItemVm(ClaimAction action, int roleCount, Action<ClaimItemVm> onChanged) {
         _onChanged = onChanged;
         Action = action;
         RoleCount = roleCount;
      }

      /// <summary>Declaration of this right - its owning module and its name.</summary>
      public ClaimAction Action { get; }

      /// <summary>Combined key of this right, <c>module:name</c>.</summary>
      public string Key => Action.Key;

      /// <summary>Name of this right without its module name, e.g. <c>Approve</c>.</summary>
      public string Name => Action.Name;

      /// <summary>Number of roles that carry this right, across the whole server.</summary>
      public int RoleCount { get; }

      /// <summary>Usage caption of this right for the catalog sheet, e.g. "Carried by 4 roles".</summary>
      public string UsageCaption => RoleCount == 1 ? "Carried by 1 role" : $"Carried by {RoleCount:N0} roles";

      /// <summary>
      /// Whether the open role carries this right. This is the desired state: changing it sends nothing to
      /// the server, it only moves the difference that the save button will send.
      /// </summary>
      public bool IsGranted {
         get => Get<bool>();
         set => Set(value, _ => _onChanged(this));
      }
   }

   /// <summary>State of one role assignment against the server time when the screen was loaded.</summary>
   public enum MemberPeriodState
   {
      /// <summary>Currently in effect.</summary>
      Active,

      /// <summary>Already granted, but its start date has not yet arrived.</summary>
      Scheduled,

      /// <summary>Its validity period has passed.</summary>
      Expired
   }

   /// <summary>
   /// One row on the Members tab: who holds this role, from when until when, and whether that assignment
   /// is currently in effect.
   /// </summary>
   public class MemberRowVm : NotifyPropertyBase
   {
      private DateTime _now;

      internal MemberRowVm(User user, ta_UserRole? assignment, DateTime now) {
         User = user;
         _now = now;
         Start = assignment?.cUserRoleStart;
         Expiry = assignment?.cUserRoleExpiry;
      }

      /// <summary>The user who holds this role.</summary>
      public User User { get; }

      /// <summary>The account name.</summary>
      public string Account => User.cUserAccount;

      /// <summary>The full name of the person.</summary>
      public string FullName => User.cContactFullName;

      /// <summary>Start of this assignment's validity; <c>null</c> means since the assignment was created.</summary>
      public DateTime? Start {
         get => Get<DateTime?>();
         set => Set(value, _ => RaiseDerived());
      }

      /// <summary>End of this assignment's validity; <c>null</c> means no end.</summary>
      public DateTime? Expiry {
         get => Get<DateTime?>();
         set => Set(value, _ => RaiseDerived());
      }

      /// <summary>Caption of the start of validity for the ASSIGNED column.</summary>
      public string StartCaption => Start?.ToString("dd MMM yyyy") ?? "From the start";

      /// <summary>Caption of the end of validity for the EXPIRES column.</summary>
      public string ExpiryCaption => Expiry?.ToString("dd MMM yyyy") ?? "No end date";

      /// <summary>State of this assignment against the server time when the screen was loaded.</summary>
      public MemberPeriodState State {
         get {
            if (Start is { } start && start > _now) return MemberPeriodState.Scheduled;
            if (Expiry is { } expiry && expiry < _now) return MemberPeriodState.Expired;
            return MemberPeriodState.Active;
         }
      }

      /// <summary>State of this assignment as text for the chip in the STATE column.</summary>
      public string StateCaption => State switch {
         MemberPeriodState.Scheduled => "SCHEDULED",
         MemberPeriodState.Expired => "EXPIRED",
         _ => "ACTIVE"
      };

      // Called after saving has succeeded: this row now reflects what is stored, and the reference time also
      // moves forward to the server time that was just read.
      internal void Accept(ta_UserRole assignment, DateTime now) {
         _now = now;
         Start = assignment.cUserRoleStart;
         Expiry = assignment.cUserRoleExpiry;
         RaiseDerived();
      }

      private void RaiseDerived() {
         NotifyChanged(nameof(StartCaption));
         NotifyChanged(nameof(ExpiryCaption));
         NotifyChanged(nameof(State));
         NotifyChanged(nameof(StateCaption));
      }
   }
}
