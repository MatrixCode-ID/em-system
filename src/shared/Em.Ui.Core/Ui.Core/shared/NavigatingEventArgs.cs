namespace Em.Ui.Core.Shared
{
   public class NavigatingEventArgs : NavigationEventArgs
   {
      // Both are set by the body that handles the event, not by the navigation host that raises
      // it, so they have to stay writable after construction - an init-only setter would make the
      // whole cancellation contract unusable.
      public bool Cancel { get; set; }
      public string Message { get; set; } = string.Empty;
   }
}
