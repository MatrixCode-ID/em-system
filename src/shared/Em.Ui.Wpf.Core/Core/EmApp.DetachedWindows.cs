using System.Windows;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Core
{
   public partial class EmApp
   {
      private readonly List<DetachedWindow> _detachedWindows = [];

      /// <summary>
      /// Window detach yang sedang terbuka, masing-masing dengan stack-nya sendiri. Selalu kosong di
      /// layout multi-tab, karena detach hanya ada di layout satu halaman.
      /// </summary>
      public IReadOnlyList<DetachedWindow> DetachedWindows => _detachedWindows;

      // Every stack a title can be shown in: the main one first, then one per window a tab was torn
      // off into, then one per detached window.
      private IEnumerable<NavigationStack> AllStacks =>
         _tearOffWindows.Select(r => r.Stack!)
            .Concat(_detachedWindows.Select(r => r.Stack))
            .Prepend(MainStack);

      /// <summary>
      /// Apakah <paramref name="entry"/> boleh dikeluarkan ke window sendiri saat ini. Yang boleh hanya
      /// entri yang sedang tampil, dengan navigasi yang tidak mematikan
      /// <see cref="Navigation.IsDetachVisible"/> - dan tidak pernah home, layar login, maupun akar
      /// sebuah window detach, karena window itu akan jadi kosong.
      /// </summary>
      /// <param name="entry">Entri yang hendak di-detach.</param>
      public bool CanDetach(NavigationEntry entry) {
         if (ApplicationLayout != ApplicationLayout.SinglePage) return false;

         var stack = entry.Stack;
         if (entry != stack.Current || entry == stack.Home) return false;
         if (!entry.Navigation.IsDetachVisible || entry.Navigation.Name == LogonNavigationName) return false;

         var index = stack.IndexOf(entry);
         return index > 0 || (index == 0 && stack.Home != null);
      }

      /// <summary>
      /// Mengeluarkan <paramref name="entry"/> dari stack-nya ke window baru, menjadi akar stack window
      /// itu. Body-nya dipindah apa adanya - isian yang belum disimpan ikut - dan stack asal mundur ke
      /// entri sebelumnya tanpa memuat ulang apa pun.
      /// </summary>
      /// <param name="entry">Entri yang sedang tampil dan hendak di-detach.</param>
      /// <returns><c>false</c> kalau entri ini tidak boleh di-detach (lihat <see cref="CanDetach"/>).</returns>
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
      /// Window yang menampilkan <paramref name="stack"/>: window utama untuk <see cref="MainStack"/>,
      /// window hasil tab yang ditarik keluar atau window detach untuk stack lainnya, atau <c>null</c>
      /// kalau stack itu tidak sedang tampil di mana pun.
      /// </summary>
      /// <param name="stack">Stack yang dicari window-nya.</param>
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
