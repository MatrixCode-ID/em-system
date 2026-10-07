namespace Em.Ui.Core.Shared
{
   /// <summary>The content of a navigation entry. Receives the navigation callbacks of its entry.</summary>
   public interface INavigationBody
   {
      /// <summary>Called when the entry is about to be shown.</summary>
      Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args);
      /// <summary>Called when the entry is about to be left; it may cancel the move.</summary>
      Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args);
      /// <summary>Called when the entry is asked to reload its content.</summary>
      Task OnReloadRequested(INavigation sender, NavigationEventArgs args);
      /// <summary>Called when the entry is closed and its body is released.</summary>
      Task OnRelease(INavigation sender);
   }
}