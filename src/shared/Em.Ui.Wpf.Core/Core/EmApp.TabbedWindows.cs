using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp
   {
      // Windows a tab was torn off into on the multi-tab layout, each with a tabbed stack of its own.
      // The main window is not among them.
      private readonly List<TabbedMainWindow> _tearOffWindows = [];

      internal void RegisterTearOffWindow(TabbedMainWindow window) => _tearOffWindows.Add(window);

      internal void UnregisterTearOffWindow(TabbedMainWindow window) => _tearOffWindows.Remove(window);

      internal bool HasTearOffWindows => _tearOffWindows.Count > 0;

      // Asks every tab of a tabbed stack whether it may be left, the tab being shown first. Every
      // other tab is brought into view before it is asked, so the user sees the input the question is
      // about; each body is asked once, and one refusal stops everything with nothing closed.
      internal async Task<bool> ConfirmCloseTabsAsync(NavigationStack stack) {
         if (stack.Entries.Count == 0) return true;

         BringToFront(stack);
         var shown = stack.Current;
         if (!await stack.AskCurrentToLeave()) return false;

         foreach (var entry in stack.Entries.ToList()) {
            if (entry == shown) continue;
            if (!await stack.MoveTo(entry, askSource: false)) return false;
            if (!await NavigationStack.AskToLeave(entry)) return false;
         }

         return true;
      }

      // Asked before the main window of the multi-tab layout closes, window by window.
      internal async Task<bool> ConfirmCloseTearOffWindowsAsync() {
         foreach (var window in _tearOffWindows.ToList()) {
            if (window.Stack is { } stack && !await ConfirmCloseTabsAsync(stack)) return false;
         }

         return true;
      }

      // Closes every torn-off window without asking and releases every body on them. Used when the
      // session is gone, and after the main window's closing has been agreed to.
      internal async Task CloseTearOffWindowsAsync() {
         foreach (var window in _tearOffWindows.ToList()) await window.CloseWithoutAskingAsync();
      }
   }
}
