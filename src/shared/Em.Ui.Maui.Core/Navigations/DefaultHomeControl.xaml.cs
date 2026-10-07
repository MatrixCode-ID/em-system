using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
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
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

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

   /// <summary>View model <see cref="DefaultHomeControl"/>.</summary>
   public class DefaultHomeControlVm : MvvmModelBase
   {
      /// <summary>
      /// The flat list of all screens shown in the menu. Just fill this collection:
      /// <see cref="RootAppMenus"/> and <see cref="AppMenuGroups"/> are rebuilt by themselves from
      /// <c>Navigation.MenuPath</c> every time its content changes.
      /// </summary>
      public ObservableCollection<MenuNavigationVm> AppMenus { get; } = [];

      /// <summary>Screens without a <c>MenuPath</c>, drawn directly at the top without a group.</summary>
      public ObservableCollection<MenuNavigationVm> RootAppMenus { get; } = [];

      /// <summary>
      /// The top level menu groups resulting from splitting <c>MenuPath</c>; each group holds its own
      /// sub-groups and menus.
      /// </summary>
      public ObservableCollection<MenuGroup> AppMenuGroups { get; } = [];

      /// <summary><c>true</c> when no module screen is shown yet.</summary>
      public bool IsModuleListEmpty => AppMenus.Count == 0;

      /// <summary>
      /// Rebuilds the content of the screen: the list of screens and the menu tree. Called every time this
      /// screen is opened or asked to reload.
      /// </summary>
      public Task ReloadAsync() {
         if (EmApp is not { } app) return Task.CompletedTask;

         AppMenus.Clear();
         app.Navigations
            .Where(r => r.IsMenuVisible && app.CanOpen(r))
            .OrderBy(r => r.OrderIndex)
            .Select(r => new MenuNavigationVm(app, r))
            .EachOf(AppMenus.Add);

         RebuildMenuTree();
         NotifyChanged(nameof(IsModuleListEmpty));
         return Task.CompletedTask;
      }

      private void RebuildMenuTree() {
         RootAppMenus.Clear();
         AppMenuGroups.Clear();

         foreach (var menu in AppMenus) {
            var segments = SplitMenuPath(menu.Navigation.MenuPath);
            // A navigation without a usable path is not an error - it simply sits above the tree.
            if (segments.Length == 0) {
               RootAppMenus.Add(menu);
               continue;
            }

            ResolveGroup(segments).Items.Add(menu);
         }
      }

      // "SALES/Administration" -> ["SALES", "Administration"]. Empty segments are dropped so a separator that
      // is doubled or left dangling never produces a group without a name.
      private static string[] SplitMenuPath(MenuPath? path) {
         if (path == null || path.IsEmptyPath) return [];
         return path.Path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      }

      // Walks the path segment by segment, creating the groups that do not exist yet, and returns
      // the deepest one. Matching is case-insensitive so "Sales" and "SALES" stay one group.
      private MenuGroup ResolveGroup(string[] segments) {
         var groups = AppMenuGroups;
         MenuGroup? current = null;

         for (var level = 0; level < segments.Length; level++) {
            var header = segments[level];
            current = groups.FirstOrDefault(r => string.Equals(r.Header, header, StringComparison.OrdinalIgnoreCase));
            if (current == null) {
               current = new MenuGroup { Header = header, Level = level + 1 };
               groups.Add(current);
            }

            groups = current.Groups;
         }

         return current!;
      }
   }
}
