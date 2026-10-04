namespace Em.Ui.Core.Shared
{
   public interface INavigationBody
   {
      Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args);
      Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args);
      Task OnReloadRequested(INavigation sender, NavigationEventArgs args);
      Task OnRelease(INavigation sender);
   }
}