namespace Em.Ui.Core.Shared
{
   /// <summary>Details of a navigation move, passed to the body and to the router's listeners.</summary>
   public class NavigationEventArgs : EventArgs
   {
      /// <summary>The target navigation of this move.</summary>
      public required INavigation  NavigationItem { get; init; }

      /// <summary>
      /// The entry belonging to the body that receives this callback - for <c>OnNavigatingAway</c> it is the
      /// entry being left, for other callbacks the destination entry. Through this entry the body changes its
      /// title, closes itself, or opens another screen.
      /// </summary>
      public required INavigationEntry Entry { get; init; }

      /// <summary>Parameter for the target navigation, or <c>null</c> when there is none.</summary>
      public object? Data { get; set; }
   }

}
