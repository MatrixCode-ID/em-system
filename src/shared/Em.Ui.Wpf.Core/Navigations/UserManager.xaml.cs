using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class UserManager : UserControl, INavigationBody
   {
      private EmApp _app;
      /// <summary>Creates a new instance of <see cref="UserManager"/>.</summary>
      public UserManager(EmApp app) {
         _app = app;
         InitializeComponent();
         Vm.EmApp = _app;
         robotManager.Vm.EmApp = _app;
      }

      /// <summary>The vm.</summary>
      public UserManagerVm Vm => (UserManagerVm)DataContext;

      // Nothing is read here: the host raises this on every way into the screen, back and forward
      // included, and those two return to a list that is already filled. Filling it belongs to
      // OnReloadRequested, which only a real navigation raises.
      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         robotManager.Vm.NavigationEntry = Vm.NavigationEntry;
         robotManager.Vm.MainWindow = Vm.MainWindow;
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      // No longer async void: whoever asks for a reload now waits for it to finish, so this list is already
      // filled before the move that triggered it counts as complete.
      /// <inheritdoc />
      public async Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         await Vm.ReloadAsync();
         await robotManager.Vm.ReloadAsync();
      }

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;

      // The one gesture on this screen that is not a binding. ListBoxItem exposes no command for a
      // double click, and an input binding would live on a Freezable with no tree for RelativeSource
      // to walk back to the view model - so the gesture is caught here and handed straight to the
      // command the pencil on the row already runs.
      private void UserRow_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
         if (sender is FrameworkElement { DataContext: User user })
            Vm.Commands[nameof(Vm.EditUserCommand)]?.Execute(user);
      }

      #region Pager

      // The gap between two runs of page numbers is a button, and what it opens is a small box to
      // type a page into. These handlers live here rather than in the view model because all they
      // do is move focus and close a popup - the page itself is set through CurrentPage.

      private void PageJumpPopup_Opened(object? sender, EventArgs e) {
         if (sender is Popup { Child: { } child } && FindDescendant<TextBox>(child) is { } box) {
            box.Clear();
            box.Focus();
         }
      }

      private void PageJumpBox_KeyDown(object sender, KeyEventArgs e) {
         if (e.Key != Key.Enter) return;

         e.Handled = true;
         ApplyPageJump((TextBox)sender);
      }

      private void PageJumpGo_Click(object sender, RoutedEventArgs e) {
         // The Go button carries its own box in Tag, which saves walking the tree of a popup that
         // is not part of the control's visual tree to begin with.
         if (sender is FrameworkElement { Tag: TextBox box })
            ApplyPageJump(box);
      }

      private void ApplyPageJump(TextBox box) {
         if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var page))
            Vm.CurrentPage = page;

         // Closing through the popup rather than the button: IsOpen is bound to the gap button, so
         // the button comes back up with it.
         if (FindLogicalAncestor<Popup>(box) is { } popup)
            popup.IsOpen = false;
      }

      private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject {
         if (root is T match) return match;

         var count = VisualTreeHelper.GetChildrenCount(root);
         for (var i = 0; i < count; i++) {
            if (FindDescendant<T>(VisualTreeHelper.GetChild(root, i)) is { } found)
               return found;
         }

         return null;
      }

      private static T? FindLogicalAncestor<T>(DependencyObject node) where T : DependencyObject {
         for (var current = LogicalTreeHelper.GetParent(node);
              current != null;
              current = LogicalTreeHelper.GetParent(current)) {
            if (current is T match) return match;
         }

         return null;
      }

      #endregion
   }

   /// <summary>View model of the user manager screen.</summary>
   public class UserManagerVm : MvvmModelBase
   {
      // Pages either side of the current one that always stay on the pager; anything further out
      // collapses into a gap.
      private const int PagerWindow = 2;

      // Long enough to let a word be typed in one go, short enough to feel like the list follows the box.
      private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(350);

      // Set while the view model rewrites its own paging state, so the clamping it does along the
      // way does not queue a second fetch behind the one already running.
      private bool _pagingSuspended;
      private bool _pagerRebuildQueued;

      // A read asked for while another runs is not dropped: it is remembered and run once the
      // current one ends, so the list always ends up matching the latest conditions.
      private Task? _loading;
      private bool _reloadQueued;

      // Set while the view model moves the quick filter itself, so the segments it unchecks are
      // not taken for clicks.
      private bool _quickFilterSyncing;

      private readonly DispatcherTimer _searchTimer;

      #region Applied conditions

      // What the list is filtered by right now. The sheet edits a draft of these and only Apply
      // copies the draft back, so closing the sheet leaves the list as it was.
      private UserState[] _states = [];
      private readonly List<RoleRef> _roles = [];
      private bool _adminOnly;
      private bool _includeDeleted;
      private RoleRef[] _availableRoles = [];

      private sealed record RoleRef(string Id, string Name);

      #endregion

      /// <summary>Creates a new instance of <see cref="UserManagerVm"/>.</summary>
      public UserManagerVm() {
         RegisterCommand(nameof(FirstPageCommand), FirstPageCommand, FirstPageCommandAllowed);
         RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
         RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);
         RegisterCommand(nameof(LastPageCommand), LastPageCommand, LastPageCommandAllowed);
         RegisterCommand(nameof(AddNewUserCommand), AddNewUserCommand);
         RegisterCommand<User?>(nameof(EditUserCommand), EditUserCommand, EditUserCommandAllowed);
         RegisterCommand<UserListSortField>(nameof(SortCommand), SortCommand);
         RegisterCommand(nameof(ClearSearchCommand), ClearSearchCommand, ClearSearchCommandAllowed);
         RegisterCommand(nameof(ApplyFilterCommand), ApplyFilterCommand);
         RegisterCommand(nameof(ResetFilterCommand), ResetFilterCommand);
         RegisterCommand(nameof(CloseFilterSheetCommand), CloseFilterSheetCommand);
         RegisterCommand<ActiveFilterChip?>(nameof(RemoveFilterCommand), RemoveFilterCommand, RemoveFilterCommandAllowed);
         RegisterCommand(nameof(ClearAllFiltersCommand), ClearAllFiltersCommand, ClearAllFiltersCommandAllowed);

         _searchTimer = new DispatcherTimer { Interval = SearchDelay };
         _searchTimer.Tick += SearchTimerOnTick;

         QuickStates = [
            new FilterOption<UserState?>("All", null),
            new FilterOption<UserState?>("Active", UserState.Active),
            new FilterOption<UserState?>("Pending", UserState.Pending),
            new FilterOption<UserState?>("Suspended", UserState.Suspended),
            new FilterOption<UserState?>("Inactive", UserState.Inactive),
         ];
         foreach (var option in QuickStates)
            option.PropertyChanged += QuickStateOnPropertyChanged;

         SheetStates = [
            new FilterOption<UserState>("Active", UserState.Active),
            new FilterOption<UserState>("Pending", UserState.Pending),
            new FilterOption<UserState>("Suspended", UserState.Suspended),
            new FilterOption<UserState>("Inactive", UserState.Inactive),
         ];

         _pagingSuspended = true;
         DefaultPageSize = 200;
         CurrentPage = 1;
         SelectedPageSize = PageSizes.First(option => option.Size == DefaultPageSize);
         _pagingSuspended = false;

         SyncQuickFilter();
         RebuildPager();
      }

      #region Data

      /// <summary>
      /// The rows currently shown, which is the content of one page from the latest read.
      /// </summary>
      public ObservableCollection<User> Users { get; } = [];

      /// <summary>
      /// Number of users matching the search and filters, not just those on this page. Used to compute the
      /// page count and to compose the range caption at the foot of the list.
      /// </summary>
      public int TotalRecords {
         get => Get<int>();
         set => Set(value, _ => {
            ClampCurrentPage();
            OnPagingChanged(reload: false);
         });
      }

      /// <summary>Whether the latest read came back with no row, which is when the empty state shows.</summary>
      public bool IsEmpty => !IsBusy && Users.Count == 0;

      /// <summary>
      /// Caption of the empty state: a list narrowed down to nothing says so, rather than looking like there
      /// are no users at all.
      /// </summary>
      public string EmptyCaption => HasConditions
         ? "No user matches the search or filters"
         : "No user yet";

      #endregion

      #region Search, filter and sort

      /// <summary>
      /// Text typed in the search box. Matches the account, the full name or the email; the list follows a
      /// moment after typing stops, so a word is not sent letter by letter.
      /// </summary>
      public string SearchText {
         get => Get(string.Empty);
         set => Set(value, _ => {
            _searchTimer.Stop();
            _searchTimer.Start();
            RaiseCommandsChanged();
         });
      }

      /// <summary>
      /// The status segments beside the list: "All" plus one per state. One state picked shows as its
      /// segment; several states picked in the sheet leave every segment off and show as a chip instead.
      /// </summary>
      public ObservableCollection<FilterOption<UserState?>> QuickStates { get; }

      /// <summary>Which column the list is sorted on; each column header binds to its own entry.</summary>
      public ColumnSortState<UserListSortField> Sort { get; } = new();

      /// <summary>The conditions narrowing the list besides the search, one chip each.</summary>
      public ObservableCollection<ActiveFilterChip> ActiveFilters { get; } = [];

      /// <summary>Whether any chip is shown; drives the chip strip and the badge on the Filter button.</summary>
      public bool HasActiveFilters => ActiveFilters.Count > 0;

      /// <summary>Number of chips, shown on the Filter button so conditions out of sight are not forgotten.</summary>
      public int ActiveFilterCount => ActiveFilters.Count;

      // Anything narrowing the list, the search and a single picked state included.
      private bool HasConditions =>
         !string.IsNullOrWhiteSpace(SearchText) || _states.Length > 0 || _roles.Count > 0 || _adminOnly || _includeDeleted;

      #endregion

      #region Filter sheet

      /// <summary>
      /// Whether the filter sheet is open. Opening it fills the sheet with the conditions in force, so it
      /// always starts from what the list shows.
      /// </summary>
      public bool IsFilterSheetOpen {
         get => Get<bool>();
         set => Set(value, open => {
            if (open) LoadSheet();
         });
      }

      /// <summary>State chips in the sheet; several may be picked at once.</summary>
      public ObservableCollection<FilterOption<UserState>> SheetStates { get; }

      /// <summary>Active roles on the server, ordered by name, as check boxes in the sheet.</summary>
      public ObservableCollection<FilterOption<string>> SheetRoles { get; } = [];

      /// <summary>Whether the server has an active role to filter on at all.</summary>
      public bool HasSheetRoles => SheetRoles.Count > 0;

      /// <summary>Sheet switch: only accounts with administrator privileges.</summary>
      public bool SheetAdminOnly {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>Sheet switch: deleted accounts as well.</summary>
      public bool SheetIncludeDeleted {
         get => Get<bool>();
         set => Set(value);
      }

      #endregion

      #region Paging

      /// <summary>
      /// The choices of rows per page offered by the combo box at the foot of the list.
      /// </summary>
      public IReadOnlyList<PageSizeOption> PageSizes { get; } = [
         PageSizeOption.Of(100),
         PageSizeOption.Of(200),
         PageSizeOption.Of(500),
         PageSizeOption.Of(1000),
         PageSizeOption.Of(2000),
         PageSizeOption.All,
      ];

      /// <summary>
      /// The choice currently selected in the combo box. Every change of choice immediately updates
      /// <see cref="DefaultPageSize"/>.
      /// </summary>
      public PageSizeOption SelectedPageSize {
         get => Get<PageSizeOption>();
         set => Set(value, option => DefaultPageSize = option.Size);
      }

      /// <summary>
      /// Number of rows read for one page.
      /// </summary>
      public int DefaultPageSize {
         get => Get<int>();
         set => Set(value, _ => {
            ClampCurrentPage();
            OnPagingChanged();
         });
      }

      /// <summary>
      /// The page that is open, starting from 1. A value out of range is clamped to the first or last page
      /// automatically, so the jump-to-page field need not validate by itself.
      /// </summary>
      public int CurrentPage {
         get => Get<int>();
         set => Set(ClampPage(value), _ => OnPagingChanged());
      }

      /// <summary>
      /// Number of pages according to <see cref="TotalRecords"/> and <see cref="DefaultPageSize"/>, at least 1
      /// even when the data is empty.
      /// </summary>
      public int PageCount {
         get {
            if (TotalRecords <= 0 || DefaultPageSize <= 0) return 1;

            // long, because the page size for "ALL" is int.MaxValue and would overflow the rounding.
            var pages = ((long)TotalRecords + DefaultPageSize - 1) / DefaultPageSize;
            return pages < 1 ? 1 : (int)pages;
         }
      }

      /// <summary>
      /// The row of page buttons together with their gaps ("..."), rebuilt every time the active page or the
      /// page count changes.
      /// </summary>
      public ObservableCollection<PagerSlot> PagerSlots { get; } = [];

      /// <summary>
      /// Caption of the row range at the foot of the list, e.g. "Showing 201 - 400 of 1,024 users".
      /// </summary>
      public string RangeCaption {
         get {
            if (TotalRecords <= 0) return "No user to show";
            if (Users.Count == 0) return $"Showing 0 of {TotalRecords:N0} users";

            var first = (long)(CurrentPage - 1) * DefaultPageSize + 1;
            return $"Showing {first:N0} - {first + Users.Count - 1:N0} of {TotalRecords:N0} users";
         }
      }

      /// <summary>
      /// The number of users matching the search and filters together with its unit, for the chip in the
      /// toolbar.
      /// </summary>
      public string TotalCaption => $"{TotalRecords:N0} users";

      /// <summary>
      /// Short caption of the page position for the chip in the toolbar, e.g. "Page 3 of 6".
      /// </summary>
      public string PageCaption => $"Page {CurrentPage:N0} of {PageCount:N0}";

      #endregion

      #region Commands

      /// <summary>Goes to the first page.</summary>
      public void FirstPageCommand() => CurrentPage = 1;

      /// <summary>May only run when the active page is not the first page.</summary>
      public bool FirstPageCommandAllowed() => CurrentPage > 1;

      /// <summary>Goes back one page.</summary>
      public void PreviousPageCommand() => CurrentPage--;

      /// <summary>May only run when there is still a page before the active page.</summary>
      public bool PreviousPageCommandAllowed() => CurrentPage > 1;

      /// <summary>Goes forward one page.</summary>
      public void NextPageCommand() => CurrentPage++;

      /// <summary>May only run when there is still a page after the active page.</summary>
      public bool NextPageCommandAllowed() => CurrentPage < PageCount;

      /// <summary>Goes to the last page.</summary>
      public void LastPageCommand() => CurrentPage = PageCount;

      /// <summary>May only run when the active page is not the last page.</summary>
      public bool LastPageCommandAllowed() => CurrentPage < PageCount;

      /// <summary>
      /// Moves a column header one step along ascending, descending and back to the default order, then reads
      /// the list again from its first page: the rows of the page that was open are not the same rows in
      /// another order.
      /// </summary>
      /// <param name="field">The column whose header was clicked, passed through CommandParameter.</param>
      public void SortCommand(UserListSortField field) {
         Sort.Cycle(field);
         ApplyConditions();
      }

      /// <summary>Empties the search box; the list follows straight away rather than after the typing delay.</summary>
      public void ClearSearchCommand() {
         SearchText = string.Empty;
         _searchTimer.Stop();
         ApplyConditions();
      }

      /// <summary>May only run when there is something in the search box.</summary>
      public bool ClearSearchCommandAllowed() => !string.IsNullOrEmpty(SearchText);

      /// <summary>Puts the conditions picked in the sheet in force, closes it, and reads the list again.</summary>
      public void ApplyFilterCommand() {
         _states = [.. SheetStates.Where(o => o.IsSelected).Select(o => o.Value)];

         _roles.Clear();
         _roles.AddRange(SheetRoles.Where(o => o.IsSelected).Select(o => new RoleRef(o.Value, o.Caption)));

         _adminOnly = SheetAdminOnly;
         _includeDeleted = SheetIncludeDeleted;

         IsFilterSheetOpen = false;
         ApplyConditions();
      }

      /// <summary>Empties the sheet and leaves it open, so the next set of conditions can be picked straight away.</summary>
      public void ResetFilterCommand() {
         foreach (var option in SheetStates) option.IsSelected = false;
         foreach (var option in SheetRoles) option.IsSelected = false;
         SheetAdminOnly = false;
         SheetIncludeDeleted = false;
      }

      /// <summary>Closes the sheet without touching the conditions in force.</summary>
      public void CloseFilterSheetCommand() => IsFilterSheetOpen = false;

      /// <summary>Takes back the one condition a chip stands for, then reads the list again.</summary>
      /// <param name="chip">The chip whose close button was clicked, passed through CommandParameter.</param>
      public void RemoveFilterCommand(ActiveFilterChip? chip) {
         chip!.Remove();
         ApplyConditions();
      }

      /// <summary>May only run when a chip is passed as the parameter.</summary>
      public bool RemoveFilterCommandAllowed(ActiveFilterChip? chip) => chip is not null;

      /// <summary>Takes back every chip at once. The search stays: it has a clear button of its own.</summary>
      public void ClearAllFiltersCommand() {
         ClearConditions();
         ApplyConditions();
      }

      /// <summary>May only run when there is a chip to take back.</summary>
      public bool ClearAllFiltersCommandAllowed() => HasActiveFilters;

      #endregion

      #region Methods

      private async Task AddNewUserCommand() {
         var payload = UserEditorNavigationPayload.Create(OnUserCreated);
         await NavigationEntry!.NavigateTo("admin.users.editor", payload);
      }

      private void OnUserCreated(User user) {
         Users.Add(user);
         TotalRecords++;
         NotifyChanged(nameof(IsEmpty));
      }

      /// <summary>
      /// Opens one row on the editor screen. The row is handed over as-is, not read again from the server:
      /// this list and the editor use the same object, so whatever is saved there is immediately visible in
      /// this row without reloading the page.
      /// </summary>
      /// <param name="user">The row that was clicked, passed through CommandParameter.</param>
      public async Task EditUserCommand(User? user) {
         var payload = UserEditorNavigationPayload.Create(user);
         await NavigationEntry!.NavigateTo("admin.users.editor", payload);
      }

      /// <summary>May only run when a row is passed as the parameter.</summary>
      public bool EditUserCommandAllowed(User? user) => user is not null;

      /// <summary>
      /// Reads the roles the sheet offers, then one page of users under the search, filters and sort in force.
      /// Does nothing when the application has not been set yet. A read asked for while another runs waits for
      /// it and runs once more afterwards, so the returned task ends with the list matching the latest state.
      /// </summary>
      public async Task ReloadAsync() {
         if (EmApp == null) return;

         await LoadRolesAsync();
         await LoadPageAsync();
      }

      private async Task LoadRolesAsync() {
         try {
            var service = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();
            var rows = await service.GetVi_Roles();

            // Only active roles: an inactive one grants nothing, so nobody holds it in the sense the list
            // filters on, and offering it would only ever produce an empty list.
            _availableRoles = [.. rows
               .Where(r => r.cRoleState == RoleState.Active)
               .OrderBy(r => r.cRoleName, StringComparer.CurrentCultureIgnoreCase)
               .Select(r => new RoleRef(r.cRoleId, r.cRoleName))];

            // A role picked earlier may have been switched off or renamed since: the first drops out of the
            // conditions, the second keeps its condition under the new name.
            var available = _availableRoles.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
            var kept = _roles.Where(r => available.ContainsKey(r.Id)).Select(r => available[r.Id]).ToArray();
            _roles.Clear();
            _roles.AddRange(kept);

            RebuildActiveFilters();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
      }

      private Task LoadPageAsync() {
         if (EmApp == null) return Task.CompletedTask;

         // Asked by state, not cleared from inside the read: a read that finishes without ever
         // yielding would clear the field before it was even assigned.
         if (_loading is { IsCompleted: false }) {
            _reloadQueued = true;
            return _loading;
         }

         _loading = LoadUntilSettledAsync();
         return _loading;
      }

      private async Task LoadUntilSettledAsync() {
         do {
            _reloadQueued = false;
            await LoadOnePageAsync();
         } while (_reloadQueued);
      }

      private async Task LoadOnePageAsync() {
         var wasSuspended = _pagingSuspended;
         try {
            _pagingSuspended = true;
            WaiterText = "Loading users...";
            InWaiting = IsBusy = true;
            NotifyChanged(nameof(IsEmpty));
            RaiseCommandsChanged();

            var requestedPage = CurrentPage;
            var (users, total) = await User.GetUserListPageAsync(EmApp!, BuildQuery());

            // Fewer matches than before can leave the page that was asked for past the last one; the
            // clamp moves back to a page that exists, and that page is read next.
            TotalRecords = total;
            if (CurrentPage != requestedPage)
               _reloadQueued = true;

            Users.Clear();
            users.EachOf(Users.Add);
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            InWaiting = IsBusy = false;
            _pagingSuspended = wasSuspended;
            RebuildPager();
            RaisePagingDerived();
         }
      }

      private UserListQuery BuildQuery() => new() {
         Page = CurrentPage,
         PageSize = DefaultPageSize,
         SortField = Sort.Field,
         SortDescending = Sort.Descending,
         Filter = new UserListFilter {
            Search = SearchText.Trim(),
            States = _states,
            RoleIds = [.. _roles.Select(r => r.Id)],
            AdminOnly = _adminOnly,
            IncludeDeleted = _includeDeleted
         }
      };

      // Every change of condition lands here: the chips, the segments and the badge are brought in line,
      // then the list is read from its first page, since the page that was open belonged to other rows.
      private void ApplyConditions() {
         RebuildActiveFilters();
         SyncQuickFilter();

         _pagingSuspended = true;
         CurrentPage = 1;
         _pagingSuspended = false;

         _ = LoadPageAsync();
      }

      private void ClearConditions() {
         _states = [];
         _roles.Clear();
         _adminOnly = false;
         _includeDeleted = false;
      }

      private void SearchTimerOnTick(object? sender, EventArgs e) {
         _searchTimer.Stop();
         ApplyConditions();
      }

      // The segments are radio buttons bound two way, so a click arrives here as one option turning
      // on. The ones the group turns off on the way are ignored.
      private void QuickStateOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (_quickFilterSyncing || e.PropertyName != nameof(FilterOption<UserState?>.IsSelected)) return;
         if (sender is not FilterOption<UserState?> { IsSelected: true } option) return;

         _states = option.Value is { } state ? [state] : [];
         ApplyConditions();
      }

      private void SyncQuickFilter() {
         _quickFilterSyncing = true;
         try {
            foreach (var option in QuickStates) {
               option.IsSelected = option.Value is { } state
                  ? _states.Length == 1 && _states[0] == state
                  : _states.Length == 0;
            }
         }
         finally {
            _quickFilterSyncing = false;
         }
      }

      private void LoadSheet() {
         foreach (var option in SheetStates)
            option.IsSelected = _states.Contains(option.Value);

         SheetRoles.Clear();
         foreach (var role in _availableRoles) {
            SheetRoles.Add(new FilterOption<string>(role.Name, role.Id) {
               IsSelected = _roles.Any(r => string.Equals(r.Id, role.Id, StringComparison.OrdinalIgnoreCase))
            });
         }

         SheetAdminOnly = _adminOnly;
         SheetIncludeDeleted = _includeDeleted;
         NotifyChanged(nameof(HasSheetRoles));
      }

      // One chip per condition the segments cannot show. A single state already shows as its own
      // segment, so it only gets a chip when several were picked in the sheet.
      private void RebuildActiveFilters() {
         ActiveFilters.Clear();

         if (_states.Length > 1) {
            var captions = SheetStates.Where(o => _states.Contains(o.Value)).Select(o => o.Caption);
            ActiveFilters.Add(new ActiveFilterChip($"Status: {string.Join(", ", captions)}", () => _states = []));
         }

         foreach (var role in _roles.ToArray())
            ActiveFilters.Add(new ActiveFilterChip($"Role: {role.Name}", () => _roles.Remove(role)));

         if (_adminOnly)
            ActiveFilters.Add(new ActiveFilterChip("Administrators only", () => _adminOnly = false));

         if (_includeDeleted)
            ActiveFilters.Add(new ActiveFilterChip("Including deleted accounts", () => _includeDeleted = false));

         NotifyChanged(nameof(HasActiveFilters));
         NotifyChanged(nameof(ActiveFilterCount));
         NotifyChanged(nameof(EmptyCaption));
         RaiseCommandsChanged();
      }

      private int ClampPage(int page) {
         var pageCount = PageCount;
         if (page < 1) return 1;
         return page > pageCount ? pageCount : page;
      }

      // Called after anything that changes the page count: the page that was open may no longer
      // exist. Assigning through the property is what applies the clamp.
      private void ClampCurrentPage() => CurrentPage = CurrentPage;

      private void OnPagingChanged(bool reload = true) {
         QueuePagerRebuild();
         RaisePagingDerived();

         if (reload && !_pagingSuspended)
            _ = LoadPageAsync();
      }

      private void RaisePagingDerived() {
         NotifyChanged(nameof(PageCount));
         NotifyChanged(nameof(RangeCaption));
         NotifyChanged(nameof(TotalCaption));
         NotifyChanged(nameof(PageCaption));
         NotifyChanged(nameof(IsEmpty));
         NotifyChanged(nameof(EmptyCaption));

         RaiseCommandsChanged();
      }

      // Every command here is limited by where in the list the screen is, or by whether a fetch
      // is already running, so they are re-evaluated as one set.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      // A rebuild replaces every button on the pager, and the call asking for it usually comes
      // from the click on one of those very buttons. Letting the click finish first keeps the
      // button alive for the rest of its own event.
      private void QueuePagerRebuild() {
         if (_pagerRebuildQueued) return;

         var dispatcher = Application.Current?.Dispatcher;
         if (dispatcher == null) {
            RebuildPager();
            return;
         }

         _pagerRebuildQueued = true;
         dispatcher.BeginInvoke(RebuildPager, DispatcherPriority.Background);
      }

      private void RebuildPager() {
         _pagerRebuildQueued = false;

         foreach (var slot in PagerSlots)
            slot.PropertyChanged -= PagerSlotOnPropertyChanged;

         PagerSlots.Clear();

         var current = CurrentPage;
         var previous = 0;

         foreach (var page in PageNumbersAround(current, PageCount)) {
            if (previous > 0 && page - previous > 1)
               PagerSlots.Add(PagerSlot.Gap());

            var slot = PagerSlot.Of(page, page == current);
            slot.PropertyChanged += PagerSlotOnPropertyChanged;
            PagerSlots.Add(slot);
            previous = page;
         }
      }

      // The first page, the last page, and a window around the current one - which is what keeps
      // the pager one row wide however many pages there are.
      private static IEnumerable<int> PageNumbersAround(int current, int pageCount) {
         var pages = new SortedSet<int> { 1, pageCount };

         for (var page = current - PagerWindow; page <= current + PagerWindow; page++) {
            if (page >= 1 && page <= pageCount)
               pages.Add(page);
         }

         return pages;
      }

      // The page buttons are bound two way, so a click arrives here as IsCurrent turning true.
      private void PagerSlotOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName != nameof(PagerSlot.IsCurrent)) return;
         if (sender is not PagerSlot { IsGap: false, IsCurrent: true } slot) return;

         CurrentPage = slot.Page;
      }

      #endregion
   }
}
