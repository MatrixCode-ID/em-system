namespace Em.Ui.Core.Shared
{
   /// <summary>Details of a navigation move that can still be cancelled.</summary>
   public class NavigatingEventArgs : NavigationEventArgs
   {
      // Both are set by the body that handles the event, not by the navigation host that raises
      // it, so they have to stay writable after construction - an init-only setter would make the
      // whole cancellation contract unusable.
      /// <summary>Set to <c>true</c> to cancel the move.</summary>
      public bool Cancel { get; set; }
      /// <summary>Explains why the move was cancelled, to be shown to the user.</summary>
      public string Message { get; set; } = string.Empty;
   }
}
