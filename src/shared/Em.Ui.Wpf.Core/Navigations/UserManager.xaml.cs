using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Api.Core.Models;
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

      #region Temporary for designing UI

      // Nothing below belongs to the control. These handlers only move visual state around
      // so the filter layout can be tried out while it is being designed: there is no view
      // model yet, and no query is built or sent anywhere. They go away with the move to
      // the shared filter control, where the sheet, the chips and the badge all bind
      // instead.

      private void FilterSheetClose_Click(object sender, RoutedEventArgs e) => CloseFilterSheet();

      private void FilterScrim_MouseDown(object sender, MouseButtonEventArgs e) => CloseFilterSheet();

      // Apply would send the query and close the sheet; here only the closing part exists.
      private void FilterApply_Click(object sender, RoutedEventArgs e) => CloseFilterSheet();

      // Reset empties the sheet and leaves it open, so the next set of conditions can be
      // picked straight away.
      private void FilterReset_Click(object sender, RoutedEventArgs e) => ClearSheet();

      // Every chip hands its own container to the close button through Tag, which saves
      // walking the tree to find what was asked to be removed.
      private void ActiveFilterRemove_Click(object sender, RoutedEventArgs e) {
         if (sender is FrameworkElement source && source.Tag is UIElement chip) {
            chip.Visibility = Visibility.Collapsed;
            UpdateActiveFilterState();
         }
      }

      private void ActiveFilterClearAll_Click(object sender, RoutedEventArgs e) {
         foreach (UIElement chip in activeFilterPanel.Children)
            chip.Visibility = Visibility.Collapsed;

         ClearSheet();
         UpdateActiveFilterState();
      }

      private void CloseFilterSheet() => filterToggle.IsChecked = false;

      // The date editors hold a value rather than a toggle state, so they are emptied by
      // hand alongside the walk over the chips and check boxes.
      private void ClearSheet() {
         ClearToggles(filterSheetContent);
         dateFromEdit.SelectedDate = null;
         dateToEdit.SelectedDate = null;
      }

      // Keeps the badge on the toolbar button, the chip strip and the clear-all button in
      // step with how many conditions are left.
      private void UpdateActiveFilterState() {
         var remaining = 0;
         foreach (UIElement chip in activeFilterPanel.Children) {
            if (chip.Visibility == Visibility.Visible)
               remaining++;
         }

         var hasFilters = remaining > 0;
         filterCountText.Text = remaining.ToString();
         filterBadge.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
         activeFilterGroup.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
         clearAllFiltersButton.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
      }

      // Switches every chip and check box in the sheet off. Radio buttons are left alone:
      // they are exclusive sets where one option always has to stay picked.
      private static void ClearToggles(DependencyObject root) {
         var count = VisualTreeHelper.GetChildrenCount(root);
         for (var i = 0; i < count; i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ToggleButton toggle && child is not System.Windows.Controls.RadioButton)
               toggle.IsChecked = false;

            ClearToggles(child);
         }
      }

      #endregion
   }

   /// <summary>View model of the user manager screen.</summary>
   public class UserManagerVm : MvvmModelBase
   {
      // Pages either side of the current one that always stay on the pager; anything further out
      // collapses into a gap.
      private const int PagerWindow = 2;

      // Set while the view model rewrites its own paging state, so the clamping it does along the
      // way does not queue a second fetch behind the one already running.
      private bool _pagingSuspended;
      private bool _pagerRebuildQueued;

      /// <summary>Creates a new instance of <see cref="UserManagerVm"/>.</summary>
      public UserManagerVm() {
         RegisterCommand(nameof(FirstPageCommand), FirstPageCommand, FirstPageCommandAllowed);
         RegisterCommand(nameof(PreviousPageCommand), PreviousPageCommand, PreviousPageCommandAllowed);
         RegisterCommand(nameof(NextPageCommand), NextPageCommand, NextPageCommandAllowed);
         RegisterCommand(nameof(LastPageCommand), LastPageCommand, LastPageCommandAllowed);
         RegisterCommand(nameof(AddNewUserCommand), AddNewUserCommand);
         RegisterCommand<User?>(nameof(EditUserCommand), EditUserCommand, EditUserCommandAllowed);
         _pagingSuspended = true;
         DefaultPageSize = 200;
         CurrentPage = 1;
         SelectedPageSize = PageSizes.First(option => option.Size == DefaultPageSize);
         _pagingSuspended = false;

         RebuildPager();
      }

      #region Data

      /// <summary>
      /// The rows currently shown, which is the content of one page from the latest read.
      /// </summary>
      public ObservableCollection<User> Users { get; } = [];

      /// <summary>
      /// Total number of users in the database, not just those on this page. Used to compute the page count
      /// and to compose the range caption at the foot of the list.
      /// </summary>
      public int TotalRecords {
         get => Get<int>();
         set => Set(value, _ => {
            ClampCurrentPage();
            OnPagingChanged(reload: false);
         });
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
      /// The number of users shown now together with its unit, for the chip in the toolbar.
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

      #endregion

      #region Methods

      private async Task AddNewUserCommand() {
         var payload = UserEditorNavigationPayload.Create(OnUserCreated);
         await NavigationEntry!.NavigateTo("admin.users.editor", payload);
      }

      private void OnUserCreated(User user) {
         Users.Add(user);
         TotalRecords++;
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
      /// Reads the total number of users then fills <see cref="Users"/> with one page of data. Does nothing
      /// when the application has not been set yet or another read is still running.
      /// </summary>
      public async Task ReloadAsync() {
         if (EmApp == null || IsBusy) return;

         var wasSuspended = _pagingSuspended;
         try {
            _pagingSuspended = true;
            WaiterText = "Loading users...";
            InWaiting = IsBusy = true;
            await Task.Delay(2000);
            RaiseCommandsChanged();

            // Despite its name this returns the row count, not a page count; how many pages that
            // makes depends on the page size the screen is on, and is worked out here.
            TotalRecords = await User.GetUsers_PageCountAsync(EmApp);

            Users.Clear();
            var rows = await User.GetUsers_InPageAsync(EmApp, CurrentPage, DefaultPageSize);
            rows.EachOf(Users.Add);
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
            _ = ReloadAsync();
      }

      private void RaisePagingDerived() {
         NotifyChanged(nameof(PageCount));
         NotifyChanged(nameof(RangeCaption));
         NotifyChanged(nameof(TotalCaption));
         NotifyChanged(nameof(PageCaption));

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
