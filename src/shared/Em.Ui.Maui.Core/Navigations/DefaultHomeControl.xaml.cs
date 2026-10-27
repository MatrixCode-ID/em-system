using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Controls;
using Em.Api.Core.Models;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The default home screen: a search box and a tree menu of the screens registered by modules. Used as
   /// long as the application does not provide its own home through <c>EmAppBuilder.UseHomeNavigation</c>.
   /// </summary>
   /// <remarks>
   /// The application identity, account, server, theme, and sign-out are not here - they all live in the
   /// account panel opened from the badge in the top bar, so this screen purely holds the way to other
   /// screens. The application name itself is already readable in the top bar title.
   /// </remarks>
   public partial class DefaultHomeControl : ContentView, INavigationBody
   {
      private EmApp? _taskApp;
      public DefaultHomeControl() {
         InitializeComponent();
         Vm.OpenTasksRequested += OnOpenTasksRequested;
         Vm.ContextOpening += OnContextOpening;
         Vm.PropertyChanged += OnMenuStateChanged;
         SizeChanged += (_, _) => { if (Vm.IsContextMenuOpen) PositionContextMenu(_contextAnchor); };
         ContextPopover.SizeChanged += (_, _) => { if (Vm.IsContextMenuOpen) PositionContextMenu(_contextAnchor); };
      }

      /// <summary>The view model of this screen, read back from the BindingContext set in XAML.</summary>
      public DefaultHomeControlVm Vm => (DefaultHomeControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         UnsubscribeTasks(); _taskApp = Vm.EmApp;
         if (_taskApp is not null) _taskApp.HubTasksChanged += OnHubTasksChanged;
         return Vm.ReloadAsync();
      }

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) { Vm.CloseMenuCommand(); UnsubscribeTasks(); return Task.CompletedTask; }

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) { Vm.CloseMenuCommand(); UnsubscribeTasks(); Vm.OpenTasksRequested -= OnOpenTasksRequested; Vm.ContextOpening -= OnContextOpening; Vm.PropertyChanged -= OnMenuStateChanged; return Task.CompletedTask; }
      private void UnsubscribeTasks() { if (_taskApp is not null) _taskApp.HubTasksChanged -= OnHubTasksChanged; _taskApp = null; }
      private void OnHubTasksChanged(object? sender, EventArgs e) => Dispatcher.Dispatch(() => { if (_taskApp is not null) Vm.RefreshTaskSummary(); });
      private void OnOpenTasksRequested(object? sender, EventArgs e) { if ((Vm.HostPage ?? Vm.EmApp?.RootPage) is SpaNavigationHost host) _ = host.Vm.HubTasksCommand(); }
      private VisualElement? _contextAnchor;
      private void OnContextOpening(object? anchor) { _contextAnchor = anchor as VisualElement; PositionContextMenu(_contextAnchor); }
      private void PositionContextMenu(VisualElement? anchor) {
         var menuWidth = Math.Min(248, Math.Max(0, Width - 24));
         ContextPopover.WidthRequest = menuWidth;
         var x = Math.Max(12, Width - menuWidth - 12); var y = 120d;
#if ANDROID
         if (anchor?.Handler?.PlatformView is Android.Views.View nativeAnchor && Handler?.PlatformView is Android.Views.View nativeRoot) {
            int[] position = new int[2], origin = new int[2]; nativeAnchor.GetLocationInWindow(position); nativeRoot.GetLocationInWindow(origin);
            var density = Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1f;
            x = (position[0] + nativeAnchor.Width - origin[0]) / density - menuWidth;
            y = (position[1] + nativeAnchor.Height - origin[1]) / density + 4;
         }
#endif
         ContextPopover.TranslationX = Math.Clamp(x, 0, Math.Max(0, Width - menuWidth));
         ContextPopover.TranslationY = Math.Clamp(y, 0, Math.Max(0, Height - Math.Max(128, ContextPopover.Height)));
      }
      private void OnMenuStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
         if (e.PropertyName != nameof(DefaultHomeControlVm.IsContextMenuOpen)) return;
         Dispatcher.Dispatch(() => {
#if ANDROID
            if (CatalogContent.Handler?.PlatformView is Android.Views.ViewGroup background) {
               background.ImportantForAccessibility = Vm.IsContextMenuOpen ? Android.Views.ImportantForAccessibility.NoHideDescendants : Android.Views.ImportantForAccessibility.Auto;
               background.DescendantFocusability = Vm.IsContextMenuOpen ? Android.Views.DescendantFocusability.BlockDescendants : Android.Views.DescendantFocusability.AfterDescendants;
            }
#endif
            if (Vm.IsContextMenuOpen) MenuOpenButton.Focus(); else _contextAnchor?.Focus();
         });
      }
   }

   /// <summary>Selects a real summary row or catalogue row without hidden header/footer placeholders.</summary>
   public sealed class HomeRowTemplateSelector : DataTemplateSelector
   {
      /// <summary>The shared task summary row.</summary>
      public DataTemplate TaskSummaryTemplate { get; set; } = null!;
      /// <summary>The existing destination and group rows.</summary>
      public DataTemplate CatalogueTemplate { get; set; } = null!;
      /// <inheritdoc />
      protected override DataTemplate OnSelectTemplate(object item, BindableObject container) =>
         item is CatalogRow { IsTaskSummary: true } ? TaskSummaryTemplate : CatalogueTemplate;
   }

   /// <summary>
   /// One screen card in the home menu. It carries its own open command, so the template that draws it
   /// need not know any view model above it - and so the same card can be used in the top list and inside
   /// a group as deep as needed.
   /// </summary>
   public sealed class MenuNavigationVm : MvvmModelBase
   {
      private readonly Func<MenuNavigationVm, object?, Task>? _more;
      /// <summary>Creates a destination with an optional device-local customization menu.</summary>
      public MenuNavigationVm(EmApp app, Navigation navigation, Func<MenuNavigationVm, object?, Task>? more = null) {
         EmApp = app;
         Navigation = navigation;
         // Its title and caption are copied once here, not bound to its navigation: the menu must keep reading
         // the same even if the navigation changes its title while this menu is open.
         Title = navigation.Title;
         Subtitle = navigation.Subtitle;
         Description = navigation.Description;
         _more = more;

         RegisterCommand(nameof(OpenCommand), OpenCommand);
         RegisterCommand(nameof(MoreCommand), MoreCommand);
      }

      /// <summary>The screen this card opens.</summary>
      public Navigation Navigation { get; }

      /// <summary>The title on the card.</summary>
      public string Title { get; }

      /// <summary>The second line of the card.</summary>
      public string Subtitle { get; }

      /// <summary>The long explanation of this screen.</summary>
      public string Description { get; }

      /// <summary><c>true</c> when there is a second line to draw.</summary>
      public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

      /// <summary>The registered menu image, when supplied by its module.</summary>
      public ImageSource? Icon => Navigation.NavigationIcon;

      /// <summary>Whether the registered image replaces the default app glyph.</summary>
      public bool HasIcon => Icon is not null;

      /// <summary>Whether to show the default app glyph.</summary>
      public bool HasNoIcon => !HasIcon;

      /// <summary>The source group or department code.</summary>
      public string Code => Navigation.MenuPath?.Path ?? "";
      /// <summary>The module-supplied glyph, used only when no image is supplied.</summary>
      public string Glyph => Navigation.MenuGlyph;
      /// <summary>Whether this destination is pinned on this device for this identity.</summary>
      public bool IsPinned { get => Get<bool>(); set => Set(value); }
      /// <summary>Whether this destination is hidden by a local preference.</summary>
      public bool IsHidden { get => Get<bool>(); set => Set(value); }
      /// <summary>Whether this card has a customization menu.</summary>
      public bool HasMoreActions => _more is not null;
      /// <summary>The native context menu for this destination.</summary>
      public Task MoreCommand(object? anchor) => _more?.Invoke(this, anchor) ?? Task.CompletedTask;

      public Task OpenCommand() => EmApp!.NavigateTo(Navigation);
   }

   /// <summary>
   /// One level of the home menu tree, resulting from splitting <c>Navigation.MenuPath</c>. A group may hold
   /// screen cards, other groups, or both.
   /// </summary>
   public sealed class MenuGroup : MvvmModelBase
   {
      public MenuGroup() {
         RegisterCommand(nameof(ToggleCommand), ToggleCommand);
      }

      /// <summary>
      /// The group name, that is, one segment of <c>MenuPath</c>.
      /// </summary>
      public required string Header { get; init; }

      /// <summary>
      /// The depth of the group, starting from 1 for the top group. Used by the view to choose a display style.
      /// </summary>
      public required int Level { get; init; }

      /// <summary>
      /// The child groups under this group.
      /// </summary>
      public ObservableCollection<MenuGroup> Groups { get; } = [];

      /// <summary>The screen cards that sit directly in this group.</summary>
      public ObservableCollection<MenuNavigationVm> Items { get; } = [];

      /// <summary>
      /// Whether the content of this group is open. It starts open: a menu that is entirely closed when the
      /// home screen opens hides precisely what the user is looking for.
      /// </summary>
      public bool IsExpanded {
         get => Get<bool>(true);
         set => Set(value, _ => NotifyChanged(nameof(ChevronGlyph)));
      }

      /// <summary>The chevron in the group header: pointing down when open, to the right when closed.</summary>
      public string ChevronGlyph => IsExpanded ? FontIcons.ChevronUp : FontIcons.ChevronDown;

      /// <summary>
      /// The shift to the right that shows the depth of this group. Only one level is shifted each time,
      /// because the tree may be as deep as anything and a large shift would run out of screen width.
      /// </summary>
      public Thickness Indent => new((Level - 1) * 12, 0, 0, 0);

      public void ToggleCommand() => IsExpanded = !IsExpanded;
   }

   /// <summary>A visible catalogue row. Group headings share one bounded virtual list with destinations.</summary>
   public sealed class CatalogRow : MvvmModelBase
   {
      private readonly Action? _toggle;
      /// <summary>Creates a destination or an independently toggleable group heading.</summary>
      public CatalogRow(Action? toggle = null) {
         _toggle = toggle;
         RegisterCommand(nameof(ToggleCommand), ToggleCommand, ToggleCommandAllowed);
      }
      /// <summary>The complete group path displayed by a heading.</summary>
      public string Header { get; init; } = "";
      /// <summary>Whether this row is a group heading.</summary>
      public bool IsHeader => Menu is null && !IsTaskSummary;
      /// <summary>The live Home summary, present only on its dedicated row.</summary>
      public DefaultHomeControlVm? TaskSummary { get; init; }
      /// <summary>Whether this row renders the shared task summary.</summary>
      public bool IsTaskSummary => TaskSummary is not null;
      /// <summary>Whether this row opens a destination.</summary>
      public bool IsDestination => Menu is not null;
      /// <summary>The destination, or null for a heading.</summary>
      public MenuNavigationVm? Menu { get; init; }
      /// <summary>Whether this destination belongs to the compact full catalogue.</summary>
      public bool IsCatalogueItem { get; init; }
      /// <summary>The actual group nesting level; the root shortlist has no indent.</summary>
      public int Depth { get; init; }
      /// <summary>Visually nests compact module rows under their parent header.</summary>
      public Thickness Indent => new(IsCatalogueItem ? Math.Min(Depth, 3) * 16 : 0, 0, 0, 0);
      /// <summary>The catalogue uses smaller decorative icons than its group headers.</summary>
      public double IconTileSize => IsCatalogueItem ? 32 : 48;
      /// <summary>The glyph fits its visual tile independently of the native touch target.</summary>
      public double GlyphSize => IsCatalogueItem ? 16 : 24;
      /// <summary>Keeps the glyph centred without clipping its em box.</summary>
      public Thickness IconPadding => IsCatalogueItem ? new(4,4,4,4) : new(8,8,8,8);
      /// <summary>Child rows use less vertical padding than their containing group.</summary>
      public Thickness ContentPadding => IsCatalogueItem ? new(12,6,12,6) : new(12,12,12,12);
      /// <summary>The compact catalogue headline differs from the Home destination headline.</summary>
      public double TitleSize => IsCatalogueItem ? 14 : 16;
      /// <summary>Only the full catalogue exposes labelled customization actions.</summary>
      public bool ShowActions => IsCatalogueItem && Menu?.HasMoreActions == true;
      /// <summary>The expansion state captured when the visible list was built.</summary>
      public bool IsExpanded { get; init; }
      /// <summary>Number of matching destinations in the complete subtree.</summary>
      public int Count { get; init; }
      /// <summary>Heading with the actual count rather than mockup sample data.</summary>
      public string Caption => Header + " (" + Count.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US")) + ")";
      /// <summary>The visual expansion indicator.</summary>
      public string ChevronGlyph => IsExpanded ? FontIcons.ChevronUp : FontIcons.ChevronDown;
      /// <summary>The group identity and state announced by accessibility services.</summary>
      public string GroupAccessibleName => Header + (IsExpanded ? ", expanded" : ", collapsed");
      /// <summary>Toggles the group without navigating.</summary>
      public void ToggleCommand() => _toggle?.Invoke();
      /// <summary>Only group headings have a toggle action.</summary>
      public bool ToggleCommandAllowed() => _toggle is not null;
   }

   /// <summary>The home catalogue, filtered only after access has been checked.</summary>
   public class DefaultHomeControlVm : MvvmModelBase
   {
      public ObservableCollection<MenuNavigationVm> AppMenus { get; } = [];
      public ObservableCollection<MenuNavigationVm> RootAppMenus { get; } = [];
      public ObservableCollection<MenuGroup> AppMenuGroups { get; } = [];
      public ObservableCollection<CatalogRow> VisibleRows { get; } = [];
      /// <summary>Only destinations explicitly pinned for the current identity.</summary>
      public ObservableCollection<CatalogRow> HomeRows { get; } = [];
      /// <summary>The pinned rows and exactly one movable summary, in their actual display order.</summary>
      public ObservableCollection<CatalogRow> HomeDisplayRows { get; } = [];
      /// <summary>The single virtual list currently displayed by Home or the catalogue.</summary>
      public ObservableCollection<CatalogRow> DisplayedRows => IsCatalogueOpen ? VisibleRows : HomeDisplayRows;
      /// <summary>Whether the full grouped catalogue replaces the Home shortlist.</summary>
      public bool IsCatalogueOpen { get => Get<bool>(); private set => Set(value, _ => {
         foreach (var name in new[] { nameof(DisplayedRows), nameof(PageTitle), nameof(IsHomeView), nameof(FilterCaption) }) NotifyChanged(name);
         NotifyTaskPlacement();
      }); }
      /// <summary>Whether the greeting and View all action belong in the header.</summary>
      public bool IsHomeView => !IsCatalogueOpen;
      /// <summary>The heading of the active catalogue mode.</summary>
      public string PageTitle => IsCatalogueOpen ? "All Modules" : "Modules";
      /// <summary>The current account name rather than a mockup identity.</summary>
      public string AccountName => EmApp?.ActiveUser?.cContactFullName ?? "";
      /// <summary>A greeting derived from the device's local time.</summary>
      public string Greeting => DateTime.Now.Hour < 12 ? "Good morning" : DateTime.Now.Hour < 18 ? "Good afternoon" : "Good evening";
      /// <summary>Opens the complete catalogue without discarding search or collapse state.</summary>
      public void ViewAllCommand() { CloseMenuCommand(); IsCatalogueOpen = true; }
      /// <summary>Returns to the preferred destinations without navigating to a business module.</summary>
      public void BackToHomeCommand() { CloseMenuCommand(); IsCatalogueOpen = false; }
      private object? _user, _connection;
      private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);
      private readonly HashSet<string> _searchCollapsedGroups = new(StringComparer.OrdinalIgnoreCase);
      private CataloguePreferences _preferences = new([], [], []);
      private string _preferenceKey = "";
      private readonly CatalogRow _taskSummaryRow;
      public DefaultHomeControlVm() {
         _taskSummaryRow = new CatalogRow { TaskSummary = this };
         RegisterCommand(nameof(ClearSearchCommand), ClearSearchCommand);
         RegisterCommand(nameof(FilterCommand), FilterCommand);
         RegisterCommand(nameof(ResetFiltersCommand), ResetFiltersCommand);
         RegisterCommand(nameof(ViewAllCommand), ViewAllCommand);
         RegisterCommand(nameof(BackToHomeCommand), BackToHomeCommand);
         RegisterCommand(nameof(OpenTasksCommand), OpenTasksCommand, OpenTasksCommandAllowed);
         RegisterCommand(nameof(RefreshTasksCommand), RefreshTasksCommand, RefreshTasksCommandAllowed);
         RegisterCommand(nameof(MoveTaskCardCommand), MoveTaskCardCommand, MoveTaskCardCommandAllowed);
         RegisterCommand(nameof(MoveTaskCardBeforeCommand), MoveTaskCardBeforeCommand, MoveTaskCardCommandAllowed);
         RegisterCommand(nameof(MoveTaskCardAfterCommand), MoveTaskCardAfterCommand, MoveTaskCardCommandAllowed);
         RegisterCommand(nameof(CloseMenuCommand), CloseMenuCommand);
         RegisterCommand(nameof(OpenMenuCommand), OpenMenuCommand);
         RegisterCommand(nameof(PinMenuCommand), PinMenuCommand);
      }
      /// <summary>Hands the invoking native view to the popover positioning adapter.</summary>
      public event Action<object?>? ContextOpening;
      private MenuNavigationVm? _contextMenu;
      private object? _menuUser, _menuConnection;
      /// <summary>Whether the local two-action module popover owns input.</summary>
      public bool IsContextMenuOpen { get => Get<bool>(); private set => Set(value, _ => NotifyChanged(nameof(IsMenuClosed))); }
      /// <summary>Whether the underlying catalogue accepts input.</summary>
      public bool IsMenuClosed => !IsContextMenuOpen;
      /// <summary>The pin action reflects the actual selected module state.</summary>
      public string PinMenuCaption => _contextMenu?.IsPinned == true ? "Unpin from home" : "Pin to home";
      /// <summary>The full identity remains available to accessibility despite visual truncation.</summary>
      public string ContextModuleName => _contextMenu?.Title ?? "";
      /// <summary>Closes the popover without dispatching a module action.</summary>
      public void CloseMenuCommand() { IsContextMenuOpen = false; _contextMenu = null; }
      private bool CurrentMenu(MenuNavigationVm? menu) => menu is not null && AppMenus.Contains(menu) && ReferenceEquals(_menuUser, EmApp?.ActiveUser) && ReferenceEquals(_menuConnection, EmApp?.ActiveConnection) && EmApp?.CanOpen(menu.Navigation) == true;
      /// <summary>Opens the selected permitted module and releases the popover.</summary>
      public Task OpenMenuCommand() { var menu = _contextMenu; var current = CurrentMenu(menu); CloseMenuCommand(); return current ? menu!.OpenCommand() : Task.CompletedTask; }
      /// <summary>Pins or unpins the selected module for the current identity.</summary>
      public void PinMenuCommand() { var menu = _contextMenu; var current = CurrentMenu(menu); CloseMenuCommand(); if (current) ApplyCustomization(menu!, menu!.IsPinned ? "Unpin from home" : "Pin to home"); }
      /// <summary>Requests the existing task workspace, leaving overlay ownership to the host.</summary>
      public event EventHandler? OpenTasksRequested;
      /// <summary>The assigned count is unknown until the first successful snapshot.</summary>
      public string TaskCountText => EmApp?.HubTaskRefreshedAt is null ? "—" : EmApp.HubTaskCount.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
      /// <summary>The real number of active task groups, without invented status buckets.</summary>
      public string TaskGroupText => EmApp?.HubTaskRefreshedAt is null ? "—" : EmApp.HubTasks.Count(x => x.Count > 0).ToString(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
      /// <summary>The last successful update; a failed refresh does not advance it.</summary>
      public string TaskUpdatedText => EmApp?.HubTaskRefreshedAt is { } time ? "Updated " + time.ToString("dd-MMM-yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("en-US")) : "Not refreshed yet";
      /// <summary>Marks a failed refresh without inserting a temporary loading row.</summary>
      public string TaskStatusText => EmApp?.HubTaskLoadFailed == true ? "Refresh failed. Showing the last successful snapshot." : "";
      /// <summary>Drives the progress indicator in the reserved refresh action slot.</summary>
      public bool IsTaskRefreshing => EmApp?.HubTaskIsLoading == true;
      /// <summary>Shows the refresh action again when the shared update has finished.</summary>
      public bool IsTaskRefreshIdle => !IsTaskRefreshing;
      /// <summary>Whether a non-empty status message belongs below the counts.</summary>
      public bool HasTaskStatus => TaskStatusText.Length > 0;
      /// <summary>Rebinds the Home summary after the shared cache changes.</summary>
      public void RefreshTaskSummary() {
         foreach (var name in new[] { nameof(TaskCountText), nameof(TaskGroupText), nameof(TaskUpdatedText), nameof(TaskStatusText), nameof(HasTaskStatus), nameof(IsTaskRefreshing), nameof(IsTaskRefreshIdle) }) NotifyChanged(name);
         Commands[nameof(OpenTasksCommand)]?.RaiseCanExecuteChanged(); Commands[nameof(RefreshTasksCommand)]?.RaiseCanExecuteChanged();
      }
      /// <summary>Opens the task workspace rather than making a decision from Home.</summary>
      public void OpenTasksCommand() { if (OpenTasksCommandAllowed()) OpenTasksRequested?.Invoke(this, EventArgs.Empty); }
      /// <summary>Task access remains available at zero tasks after authentication.</summary>
      public bool OpenTasksCommandAllowed() => EmApp?.ActiveUser is not null;
      /// <summary>Refreshes the existing cache used by the badge and task workspace.</summary>
      public async Task RefreshTasksCommand() { if (RefreshTasksCommandAllowed() && EmApp is { } app) { await app.RefreshHubTasksAsync(); RefreshTaskSummary(); } }
      /// <summary>Prevents a second refresh while the shared cache is loading.</summary>
      public bool RefreshTasksCommandAllowed() => EmApp?.ActiveUser is not null && EmApp.HubTaskIsLoading == false;
      /// <summary>Whether the summary precedes the complete pinned application list.</summary>
      public bool TasksBeforePins => _preferences.TasksBeforePins;
      /// <summary>Whether the current identity can reorder its Home summary.</summary>
      public bool CanMoveTaskCard => MoveTaskCardCommandAllowed();
      /// <summary>Changes when the account or server changes, cancelling native held input.</summary>
      public object TaskInteractionScope { get; private set; } = new();
      /// <summary>Places the summary before the whole pinned list after an upward held drag.</summary>
      public void MoveTaskCardBeforeCommand() => SetTaskCardPosition(true);
      /// <summary>Places the summary after the whole pinned list after a downward held drag.</summary>
      public void MoveTaskCardAfterCommand() => SetTaskCardPosition(false);
      /// <summary>Moves the entire summary across the pinned list and persists the account/server preference.</summary>
      public void MoveTaskCardCommand() => SetTaskCardPosition(!TasksBeforePins);
      private void SetTaskCardPosition(bool before) {
         if (!MoveTaskCardCommandAllowed() || TasksBeforePins == before || EmApp is not { } app) return;
         var preferences = _preferences with { TasksBeforePins = before };
         app.Settings.SetString(_preferenceKey, JsonSerializer.Serialize(preferences));
         _preferences = preferences;
         HomeDisplayRows.Move(HomeDisplayRows.IndexOf(_taskSummaryRow), TasksBeforePins ? 0 : HomeDisplayRows.Count - 1);
         NotifyTaskPlacement();
      }
      /// <summary>Refuses stale account/server actions, catalogue moves and moves without pins.</summary>
      public bool MoveTaskCardCommandAllowed() => IsHomeView && HomeRows.Count > 0 && _preferenceKey.Length > 0 &&
         EmApp?.ActiveUser is not null && ReferenceEquals(_user, EmApp.ActiveUser) && ReferenceEquals(_connection, EmApp.ActiveConnection);
      private void NotifyTaskPlacement() {
         NotifyChanged(nameof(TasksBeforePins));
         NotifyChanged(nameof(CanMoveTaskCard));
         Commands[nameof(MoveTaskCardCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(MoveTaskCardBeforeCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(MoveTaskCardAfterCommand)]?.RaiseCanExecuteChanged();
      }
      /// <summary>Restricts the permitted catalogue to preferred modules.</summary>
      public bool OnlyPinned { get => Get<bool>(); private set => Set(value, _ => { NotifyChanged(nameof(FilterCaption)); RebuildMenuTree(); }); }
      /// <summary>Reveals locally hidden modules so they can be restored.</summary>
      public bool ShowHidden { get => Get<bool>(); private set => Set(value, _ => { NotifyChanged(nameof(FilterCaption)); RebuildMenuTree(); }); }
      /// <summary>Explains the actual local filter or shortlist.</summary>
      public string FilterCaption => OnlyPinned ? "Pinned modules" : ShowHidden ? "Including hidden modules" : !IsCatalogueOpen && HomeRows.Any(x => x.Menu?.IsPinned == true) ? "Pinned to home" : "Available application modules";
      /// <summary>A visible acknowledgement of the last local menu action.</summary>
      public string Notice { get => Get<string>() ?? ""; private set => Set(value); }
      public string SearchText { get => Get<string>() ?? ""; set => Set(value, _ => { _searchCollapsedGroups.Clear(); RebuildMenuTree(); }); }
      public bool IsModuleListEmpty => AppMenus.Count == 0;
      public bool HasNoResults => AppMenus.Count > 0 && VisibleRows.Count == 0;
      public void ClearSearchCommand() => SearchText = "";
      /// <summary>Restores the ordinary catalogue after a search or preference filter.</summary>
      public void ResetFiltersCommand() { OnlyPinned = false; ShowHidden = false; SearchText = ""; }
      /// <summary>Chooses a real local filter, including recovery of hidden modules.</summary>
      public async Task FilterCommand() {
         if ((HostPage ?? EmApp?.RootPage) is not { } page) return;
         var user = EmApp?.ActiveUser; var connection = EmApp?.ActiveConnection;
         var choice = await page.DisplayActionSheetAsync("Module filter", "Cancel", null, "All modules", "Pinned modules", "Include hidden modules");
         if (!ReferenceEquals(user, EmApp?.ActiveUser) || !ReferenceEquals(connection, EmApp?.ActiveConnection)) return;
         if (choice == "All modules") { OnlyPinned = false; ShowHidden = false; }
         else if (choice == "Pinned modules") { OnlyPinned = true; ShowHidden = false; }
         else if (choice == "Include hidden modules") { OnlyPinned = false; ShowHidden = true; }
         NotifyChanged(nameof(FilterCaption));
      }
      private Task ShowMenuAsync(MenuNavigationVm menu, object? anchor) {
         if (EmApp is not { } app || !app.CanOpen(menu.Navigation)) return Task.CompletedTask;
         _contextMenu = menu; _menuUser = app.ActiveUser; _menuConnection = app.ActiveConnection;
         NotifyChanged(nameof(PinMenuCaption)); NotifyChanged(nameof(ContextModuleName));
         ContextOpening?.Invoke(anchor); IsContextMenuOpen = true;
         return Task.CompletedTask;
      }
      internal void ApplyCustomization(MenuNavigationVm menu, string action) {
         if (EmApp is not { } app || !AppMenus.Contains(menu) || !app.CanOpen(menu.Navigation)) return;
         var name = menu.Navigation.Name;
         var pinned = _preferences.Pinned.ToList(); var hidden = _preferences.Hidden.ToList(); var order = _preferences.Order.ToList();
         switch (action) {
            case "Pin to home": if (!pinned.Contains(name)) pinned.Add(name); hidden.Remove(name); break;
            case "Unpin from home": pinned.Remove(name); break;
            case "Move to top": order.Remove(name); order.Insert(0, name); break;
            case "Hide from list": hidden.Add(name); pinned.Remove(name); break;
            case "Show in list": hidden.RemoveAll(x => x == name); break;
            default: return;
         }
         _preferences = _preferences with { Pinned = pinned.Distinct().ToArray(), Hidden = hidden.Distinct().ToArray(), Order = order.ToArray() };
         app.Settings.SetString(_preferenceKey, JsonSerializer.Serialize(_preferences));
         foreach (var item in AppMenus) { item.IsPinned = _preferences.Pinned.Contains(item.Navigation.Name); item.IsHidden = _preferences.Hidden.Contains(item.Navigation.Name); }
         Notice = menu.Title + " · " + action; RebuildMenuTree();
      }
      public Task ReloadAsync() {
         if (EmApp is not { } app) return Task.CompletedTask;
         CloseMenuCommand();
         if (!ReferenceEquals(_user, app.ActiveUser) || !ReferenceEquals(_connection, app.ActiveConnection)) {
            TaskInteractionScope = new(); NotifyChanged(nameof(TaskInteractionScope));
            _collapsedGroups.Clear(); _searchCollapsedGroups.Clear();
            _user = app.ActiveUser; _connection = app.ActiveConnection; OnlyPinned = false; ShowHidden = false; Notice = ""; SearchText = ""; IsCatalogueOpen = false;
         }
         _preferenceKey = "Catalogue." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((app.ActiveConnection?.Host ?? "") + "\n" + (app.ActiveUser?.cUserId ?? "")))) ;
         try { _preferences = JsonSerializer.Deserialize<CataloguePreferences>(app.Settings.GetString(_preferenceKey) ?? "null") ?? new([], [], []); }
         catch (JsonException) { _preferences = new([], [], []); }
         if (_preferences.Pinned is null || _preferences.Hidden is null || _preferences.Order is null) _preferences = new([], [], []);
         _preferences = _preferences with { Hidden = [], Order = [] };
         AppMenus.Clear();
         foreach (var nav in app.Navigations.Where(x => x.IsMenuVisible && app.CanOpen(x)).OrderBy(x => x.OrderIndex))
            AppMenus.Add(new MenuNavigationVm(app, nav, ShowMenuAsync) { IsPinned = _preferences.Pinned.Contains(nav.Name), IsHidden = _preferences.Hidden.Contains(nav.Name) });
         RebuildMenuTree(); NotifyChanged(nameof(IsModuleListEmpty));
         NotifyChanged(nameof(AccountName)); NotifyChanged(nameof(Greeting));
         RefreshTaskSummary();
         return Task.CompletedTask;
      }
      private void RebuildMenuTree() {
         RootAppMenus.Clear(); AppMenuGroups.Clear(); VisibleRows.Clear();
         var query = SearchText.Trim();
         foreach (var menu in AppMenus.OrderBy(x => x.IsPinned ? 0 : 1).ThenBy(x => { var index = Array.IndexOf(_preferences.Order, x.Navigation.Name); return index < 0 ? int.MaxValue : index; })) {
            if ((!ShowHidden && menu.IsHidden) || (OnlyPinned && !menu.IsPinned)) continue;
            var path = menu.Navigation.MenuPath;
            var segments = path is null || path.IsEmptyPath ? Array.Empty<string>() : path.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (query.Length > 0 && !string.Join(" ", menu.Title, menu.Subtitle, string.Join(" / ", segments)).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            if (segments.Length == 0) { RootAppMenus.Add(menu); continue; }
            var groups = AppMenuGroups;
            MenuGroup? group = null;
            for (var i = 0; i < segments.Length; i++) {
               group = groups.FirstOrDefault(x => string.Equals(x.Header, segments[i], StringComparison.OrdinalIgnoreCase));
               if (group is null) { group = new MenuGroup { Header = segments[i], Level = i + 1 }; groups.Add(group); }
               groups = group.Groups;
            }
            group!.Items.Add(menu);
         }
         RebuildVisibleRows();
         HomeRows.Clear();
         foreach (var menu in AppMenus.Where(x => x.IsPinned && !x.IsHidden)) HomeRows.Add(new CatalogRow { Menu = menu });
         HomeDisplayRows.Clear();
         if (TasksBeforePins) HomeDisplayRows.Add(_taskSummaryRow);
         foreach (var row in HomeRows) HomeDisplayRows.Add(row);
         if (!TasksBeforePins) HomeDisplayRows.Add(_taskSummaryRow);
         NotifyTaskPlacement();
         NotifyChanged(nameof(FilterCaption));
      }
      private HashSet<string> ActiveCollapsedGroups => string.IsNullOrWhiteSpace(SearchText) ? _collapsedGroups : _searchCollapsedGroups;
      private void ToggleGroup(string path) {
         if (!ActiveCollapsedGroups.Add(path)) ActiveCollapsedGroups.Remove(path);
         RebuildVisibleRows();
      }
      private void RebuildVisibleRows() {
         VisibleRows.Clear();
         foreach (var menu in RootAppMenus) VisibleRows.Add(new CatalogRow { Menu = menu, IsCatalogueItem = true });
         foreach (var group in AppMenuGroups) AddGroup(group, "");
         NotifyChanged(nameof(HasNoResults));
      }
      private void AddGroup(MenuGroup group, string parent) {
         var path = parent.Length == 0 ? group.Header : parent + " / " + group.Header;
         var expanded = !ActiveCollapsedGroups.Contains(path);
         VisibleRows.Add(new CatalogRow(() => ToggleGroup(path)) { Header = path, IsExpanded = expanded, Count = CountGroup(group) });
         if (!expanded) return;
         foreach (var menu in group.Items) VisibleRows.Add(new CatalogRow { Menu = menu, IsCatalogueItem = true, Depth = group.Level });
         foreach (var child in group.Groups) AddGroup(child, path);
      }
      private static int CountGroup(MenuGroup group) => group.Items.Count + group.Groups.Sum(CountGroup);
   }

   internal sealed record CataloguePreferences(string[] Pinned, string[] Hidden, string[] Order, bool TasksBeforePins = false);
}
