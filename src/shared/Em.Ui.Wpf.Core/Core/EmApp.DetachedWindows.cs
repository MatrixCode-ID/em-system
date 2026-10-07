using System.Windows;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp
   {
      private readonly List<DetachedWindow> _detachedWindows = [];

      /// <summary>
      /// The detached windows that are currently open, each with its own stack. Always empty in the multi-tab
      /// layout, because detach only exists in the single-page layout.
      /// </summary>
      public IReadOnlyList<DetachedWindow> DetachedWindows => _detachedWindows;

      // Every stack a title can be shown in: the main one first, then one per window a tab was torn
      // off into, then one per detached window.
      private IEnumerable<NavigationStack> AllStacks =>
         _tearOffWindows.Select(r => r.Stack!)
            .Concat(_detachedWindows.Select(r => r.Stack))
            .Prepend(MainStack);

      /// <summary>
      /// Whether <paramref name="entry"/> may currently be taken out into its own window. Only the entry
      /// being shown may, with a navigation that does not turn off <see cref="Navigation.IsDetachVisible"/> -
      /// and never home, the login screen, or the root of a detached window, because that window would become
      /// empty.
      /// </summary>
      /// <param name="entry">The entry about to be detached.</param>
      public bool CanDetach(NavigationEntry entry) {
         if (ApplicationLayout != ApplicationLayout.SinglePage) return false;

         var stack = entry.Stack;
         if (entry != stack.Current || entry == stack.Home) return false;
         if (!entry.Navigation.IsDetachVisible || entry.Navigation.Name == LogonNavigationName) return false;

         var index = stack.IndexOf(entry);
         return index > 0 || (index == 0 && stack.Home != null);
      }

      /// <summary>
      /// Takes <paramref name="entry"/> out of its stack into a new window, becoming the root of that
      /// window's stack. Its body is moved as-is - unsaved input comes along - and the original stack goes
      /// back to the previous entry without reloading anything.
      /// </summary>
      /// <param name="entry">The entry being shown that is about to be detached.</param>
      /// <returns><c>false</c> when this entry may not be detached (see <see cref="CanDetach"/>).</returns>
      public async Task<bool> DetachAsync(NavigationEntry entry) {
         if (!CanDetach(entry)) return false;

         var source = entry.Stack;
         var sourceWindow = WindowOf(source) ?? MainWindow;
         if (!await source.Extract(entry)) return false;

         // The source host has already swapped to the previous entry, but its ContentPresenter only lets
         // go of the old body on the next layout pass - and a control cannot be the visual child of two
         // hosts at once, so the pass is forced before the body is mounted anywhere else.
         sourceWindow.UpdateLayout();

         var stack = new NavigationStack(this, home: null);
         stack.Adopt(entry);

         var window = new DetachedWindow(this, stack);
         window.PlaceBeside(sourceWindow, _detachedWindows);
         _detachedWindows.Add(window);
         window.Show();
         window.Activate();
         return true;
      }

      internal void UnregisterDetachedWindow(DetachedWindow window) => _detachedWindows.Remove(window);

      /// <summary>
      /// The window that shows <paramref name="stack"/>: the main window for <see cref="MainStack"/>, the
      /// window born from a dragged-out tab or a detached window for other stacks, or <c>null</c> when that
      /// stack is not being shown anywhere.
      /// </summary>
      /// <param name="stack">The stack whose window is looked for.</param>
      public Window? WindowOf(NavigationStack stack) =>
         stack == _mainStack ? MainWindow
         : (Window?)_tearOffWindows.FirstOrDefault(r => r.Stack == stack)
           ?? _detachedWindows.FirstOrDefault(r => r.Stack == stack);

      // "Bringing a window up": a minimised one is restored first, since activating it alone leaves it
      // on the taskbar.
      internal void BringToFront(NavigationStack stack) {
         if (WindowOf(stack) is not { } window) return;

         if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
         window.Activate();
      }

      // Asked before the main window closes. Each detached window is brought up before its body is
      // asked, so the user sees the input the question is about; one refusal stops everything, and
      // nothing has been closed or released by then.
      internal async Task<bool> ConfirmCloseDetachedWindowsAsync() {
         foreach (var window in _detachedWindows.ToList()) {
            BringToFront(window.Stack);
            if (!await window.Stack.AskCurrentToLeave()) return false;
         }

         return true;
      }

      // Closes every detached window without asking and releases every body on them. Used when the
      // session is gone - there is no one left to save anything for - and after the main window's
      // closing has been agreed to.
      internal async Task CloseDetachedWindowsAsync() {
         foreach (var window in _detachedWindows.ToList()) await window.CloseWithoutAskingAsync();
      }
   }
}
