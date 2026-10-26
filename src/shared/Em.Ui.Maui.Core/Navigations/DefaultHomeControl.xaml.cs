using System.Collections.ObjectModel;
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
      public DefaultHomeControl() {
         InitializeComponent();
      }

      /// <summary>The view model of this screen, read back from the BindingContext set in XAML.</summary>
      public DefaultHomeControlVm Vm => (DefaultHomeControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>
   /// One screen card in the home menu. It carries its own open command, so the template that draws it
   /// need not know any view model above it - and so the same card can be used in the top list and inside
   /// a group as deep as needed.
   /// </summary>
   public sealed class MenuNavigationVm : MvvmModelBase
   {
      public MenuNavigationVm(EmApp app, Navigation navigation) {
         EmApp = app;
         Navigation = navigation;
         // Its title and caption are copied once here, not bound to its navigation: the menu must keep reading
         // the same even if the navigation changes its title while this menu is open.
         Title = navigation.Title;
         Subtitle = navigation.Subtitle;
         Description = navigation.Description;

         RegisterCommand(nameof(OpenCommand), OpenCommand);
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
      public string ChevronGlyph => IsExpanded ? FontIcons.ChevronDown : FontIcons.ChevronRight;

      /// <summary>
      /// The shift to the right that shows the depth of this group. Only one level is shifted each time,
      /// because the tree may be as deep as anything and a large shift would run out of screen width.
      /// </summary>
      public Thickness Indent => new((Level - 1) * 12, 0, 0, 0);

      public void ToggleCommand() => IsExpanded = !IsExpanded;
   }

   /// <summary>A visible catalogue row. Group headings share one bounded virtual list with destinations.</summary>
   public sealed class CatalogRow
   {
      public string Header { get; init; } = "";
      public bool IsHeader => Menu is null;
      public bool IsDestination => Menu is not null;
      public MenuNavigationVm? Menu { get; init; }
   }

   /// <summary>The home catalogue, filtered only after access has been checked.</summary>
   public class DefaultHomeControlVm : MvvmModelBase
   {
      public ObservableCollection<MenuNavigationVm> AppMenus { get; } = [];
      public ObservableCollection<MenuNavigationVm> RootAppMenus { get; } = [];
      public ObservableCollection<MenuGroup> AppMenuGroups { get; } = [];
      public ObservableCollection<CatalogRow> VisibleRows { get; } = [];
      private object? _user, _connection;
      public DefaultHomeControlVm() { RegisterCommand(nameof(ClearSearchCommand), ClearSearchCommand); }
      public string SearchText { get => Get<string>() ?? ""; set => Set(value, _ => RebuildMenuTree()); }
      public bool IsModuleListEmpty => AppMenus.Count == 0;
      public bool HasNoResults => AppMenus.Count > 0 && VisibleRows.Count == 0;
      public void ClearSearchCommand() => SearchText = "";
      public Task ReloadAsync() {
         if (EmApp is not { } app) return Task.CompletedTask;
         if (!ReferenceEquals(_user, app.ActiveUser) || !ReferenceEquals(_connection, app.ActiveConnection)) {
            _user = app.ActiveUser; _connection = app.ActiveConnection; SearchText = "";
         }
         AppMenus.Clear();
         foreach (var nav in app.Navigations.Where(x => x.IsMenuVisible && app.CanOpen(x)).OrderBy(x => x.OrderIndex))
            AppMenus.Add(new MenuNavigationVm(app, nav));
         RebuildMenuTree(); NotifyChanged(nameof(IsModuleListEmpty));
         return Task.CompletedTask;
      }
      private void RebuildMenuTree() {
         RootAppMenus.Clear(); AppMenuGroups.Clear(); VisibleRows.Clear();
         var query = SearchText.Trim();
         foreach (var menu in AppMenus) {
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
         foreach (var menu in RootAppMenus) VisibleRows.Add(new CatalogRow { Menu = menu });
         foreach (var group in AppMenuGroups) AddGroup(group, "");
         NotifyChanged(nameof(HasNoResults));
      }
      private void AddGroup(MenuGroup group, string parent) {
         var path = parent.Length == 0 ? group.Header : parent + " / " + group.Header;
         VisibleRows.Add(new CatalogRow { Header = path });
         foreach (var menu in group.Items) VisibleRows.Add(new CatalogRow { Menu = menu });
         foreach (var child in group.Groups) AddGroup(child, path);
      }
   }
}
