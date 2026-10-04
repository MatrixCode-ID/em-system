using System.ComponentModel;
using Em.Ui.Core.Shared;
// Kedua namespace punya INavigation, dan yang dimaksud di sini selalu milik Em.
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Jalur navigasi yang ditampilkan satu host: deretan <see cref="NavigationEntry"/> yang bisa
   /// ditelusuri maju-mundur, ditambah satu home opsional di depannya. Stack utama aplikasi dipegang
   /// <see cref="EmApp.MainStack"/>; membuka layar dilakukan lewat router
   /// (<see cref="EmApp.NavigateTo(string,object?)"/>) atau lewat entri
   /// (<see cref="NavigationEntry.NavigateTo(string,object?)"/>), bukan lewat stack ini langsung.
   /// </summary>
   public sealed class NavigationStack : INavigationStack, INotifyPropertyChanged
   {
      // Titles are keys, and a key that differs only in casing is still the same document.
      internal const StringComparison TitleComparison = StringComparison.OrdinalIgnoreCase;

      private readonly List<NavigationEntry> _entries = [];

      internal NavigationStack(EmApp app, Navigation? home) {
         EmApp = app;
         // The home entry exists from the start, but its body is only built the first time home is
         // actually shown: before anyone has signed in, the home body must not be built at all.
         if (home != null) Home = new NavigationEntry(this, home, home.Title, null);
      }

      #region Explicit INavigationStack Implementation

      IReadOnlyList<INavigationEntry> INavigationStack.Entries => _entries;
      INavigationEntry? INavigationStack.Current => Current;
      INavigationEntry? INavigationStack.Home => Home;

      #endregion

      #region Properties

      /// <summary>Objek aplikasi pemilik stack ini.</summary>
      public EmApp EmApp { get; }

      // Read-only on purpose: every way out of the stack has to go through NavigateHome,
      // ClearForwardStacks or an entry's Close, otherwise the bodies of the dropped entries are never
      // released.
      /// <summary>Entri-entri di jalur ini, urut dari yang pertama dibuka. Home tidak termasuk.</summary>
      public IReadOnlyList<NavigationEntry> Entries => _entries;

      /// <summary>
      /// Entri yang sedang tampil - bisa juga <see cref="Home"/> - atau <c>null</c> kalau belum ada yang
      /// pernah ditampilkan.
      /// </summary>
      public NavigationEntry? Current { get; private set; }

      /// <summary>
      /// Entri home, atau <c>null</c> untuk stack tanpa home. Posisinya di depan jalur (bukan di dalam
      /// <see cref="Entries"/>) dan body-nya tidak pernah dilepas.
      /// </summary>
      public NavigationEntry? Home { get; }

      /// <summary>Apakah <see cref="Backward"/> punya tempat untuk dituju.</summary>
      public bool CanGoBack {
         get {
            var index = IndexOfCurrent();
            return index > 0 || (index == 0 && Home != null);
         }
      }

      /// <summary>Apakah <see cref="Forward"/> punya tempat untuk dituju.</summary>
      public bool CanGoForward {
         get {
            if (Current != null && Current != Home && !_entries.Contains(Current)) return false;
            return IndexOfCurrent() + 1 < _entries.Count;
         }
      }

      /// <summary>
      /// Dipicu setiap kali isi jalur atau posisinya berubah. Host memakainya untuk menghitung ulang
      /// tombol-tombol navigasinya: mengganti <see cref="Current"/> saja belum cukup, karena jalurnya
      /// masih bisa berubah sesudah itu.
      /// </summary>
      public event EventHandler? Changed;

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;

      #endregion

      #region Methods

      /// <inheritdoc />
      public bool IsInStack(string title) => _entries.Any(r => string.Equals(r.Title, title, TitleComparison));

      /// <inheritdoc />
      public Task<bool> Forward() {
         if (!CanGoForward) return Task.FromResult(false);
         return MoveTo(_entries[IndexOfCurrent() + 1]);
      }

      /// <inheritdoc />
      public Task<bool> Backward() {
         var index = IndexOfCurrent();
         var target = index switch {
            > 0 => _entries[index - 1],
            0 => Home,
            _ => null
         };

         return target != null ? MoveTo(target) : Task.FromResult(false);
      }

      /// <inheritdoc />
      public async Task NavigateHome() {
         if (Home == null) return;

         // The stack is snapshotted before the move but released only once the host has actually
         // swapped to home. Releasing first would tear down a body that is still mounted, and would
         // leave the stack destroyed if the move turned out to be refused.
         var released = _entries.ToList();
         if (!await MoveTo(Home)) return;

         _entries.Clear();
         await ReleaseEntries(released);
         RaiseChanged();

         // Home is built once and never released, so without a reload it would keep showing whatever
         // it showed before - including a menu drawn for the previous user.
         await Home.Reload();
      }

      /// <inheritdoc />
      public async Task ClearForwardStacks() {
         // Home sits in front of the stack instead of inside it, so its position is -1 and every
         // entry counts as forward: branching off from home has to start a fresh path.
         var index = IndexOfCurrent();
         if (index < 0 && Current != null && Current != Home) return;

         var firstForward = index + 1;
         var count = _entries.Count - firstForward;
         if (count <= 0) return;

         var removed = _entries.GetRange(firstForward, count);
         _entries.RemoveRange(firstForward, count);
         await ReleaseEntries(removed);
         RaiseChanged();
      }

      // Searched by the router when it enforces one place per title; home counts, because home is a
      // place a title can be shown in as well.
      internal NavigationEntry? FindEntry(string title) {
         if (Home != null && string.Equals(Home.Title, title, TitleComparison)) return Home;
         return _entries.FirstOrDefault(r => string.Equals(r.Title, title, TitleComparison));
      }

      // Moves the position to an entry that is already in this stack. Nothing is dropped from the path
      // and the entry keeps both its data and whatever the user left on it, so there is no reload.
      internal async Task<bool> MoveTo(NavigationEntry target) {
         if (target == Current) return true;

         var source = Current;
         var sender = source?.Navigation ?? target.Navigation;

         // The body being left is the one that gets to object to the move. Home never does: every
         // way out of the stack lands on it, so it must never be able to refuse being left.
         if (source != null && source != Home) {
            var away = new NavigatingEventArgs {
               NavigationItem = target.Navigation,
               Entry = source,
               Data = target.Data
            };
            await source.Body.OnNavigatingAway(sender, away);
            if (away.Cancel) return false;
         }

         var into = new NavigatingEventArgs {
            NavigationItem = target.Navigation,
            Entry = target,
            Data = target.Data
         };
         await target.Body.OnNavigatingIn(sender, into);
         if (into.Cancel) return false;

         SetCurrent(target);
         RaiseNavigated(sender, target);
         return true;
      }

      // Opens a navigation as a brand new entry of this stack. The router has already made sure the
      // title is not in use anywhere else.
      internal async Task<bool> Open(Navigation navigation, object? data, string title) {
         var source = Current;
         var sender = source?.Navigation ?? navigation;

         if (source != null && source != Home) {
            var away = new NavigatingEventArgs {
               NavigationItem = navigation,
               Entry = source,
               Data = data
            };
            await source.Body.OnNavigatingAway(sender, away);
            if (away.Cancel) return false;
         }

         var entry = new NavigationEntry(this, navigation, title, data);
         var into = new NavigatingEventArgs {
            NavigationItem = navigation,
            Entry = entry,
            Data = data
         };
         await entry.Body.OnNavigatingIn(sender, into);
         if (into.Cancel) {
            // Never shown and never added, so nothing else will ever release it.
            await entry.Release();
            return false;
         }

         // Both callbacks above are awaited, and the same title is free to have been opened somewhere
         // else meanwhile. The fresh body is then the one to give way, not the one already shown.
         if (EmApp.FindEntry(title) is { } taken) {
            await entry.Release();
            return await taken.Stack.MoveTo(taken);
         }

         // Only a brand new entry branches off, dropping whatever was ahead of the current position.
         await ClearForwardStacks();
         _entries.Add(entry);
         SetCurrent(entry);
         RaiseNavigated(sender, entry);
         await entry.Reload();
         return true;
      }

      // Opens the navigation and then makes its entry the only one on the path, so there is no way
      // back to anything that was open. Used for the moves that restart the flow of the application -
      // the login screen when it opens and whenever a session ends.
      internal async Task<bool> NavigateToRoot(Navigation navigation, object? data) {
         // Home is never an entry of the path - it sits in front of it - so asking for it as the root
         // is the same as going home, and putting it into the path below would break that rule.
         if (Home != null && navigation == Home.Navigation) {
            await NavigateHome();
            return true;
         }

         // The target is opened first and only then is the rest of the path dropped: the body being
         // left still gets OnNavigatingAway and may still refuse, and a refusal has to leave the stack
         // exactly as it was.
         if (!await EmApp.NavigateTo(navigation, data, this)) return false;
         if (Current is not { } root || root == Home || root.Stack != this) return true;

         var dropped = _entries.Where(r => r != root).ToList();
         if (dropped.Count == 0) return true;

         _entries.Clear();
         _entries.Add(root);
         await ReleaseEntries(dropped);
         RaiseChanged();
         return true;
      }

      // Takes an entry out of the path for good. The entry being looked at first moves the position
      // elsewhere - which is also what gives its body the chance to refuse - while any other entry is
      // asked directly, since leaving it just the same throws away what was left on it.
      internal async Task<bool> Close(NavigationEntry entry) {
         var index = _entries.IndexOf(entry);
         if (index < 0) return false;

         if (entry == Current) {
            var fallback = index > 0 ? _entries[index - 1]
               : Home ?? (index + 1 < _entries.Count ? _entries[index + 1] : null);

            if (fallback != null) {
               if (!await MoveTo(fallback)) return false;
            }
            else if (!await AskToLeave(entry)) return false;
         }
         else if (!await AskToLeave(entry)) return false;

         // Every step above awaited a body, and the path is free to have shifted while it did, so the
         // position taken before them cannot be reused.
         index = _entries.IndexOf(entry);
         if (index < 0) return false;

         _entries.RemoveAt(index);
         if (entry == Current) SetCurrent(null);
         await entry.Release();
         RaiseChanged();
         return true;
      }

      private static async Task<bool> AskToLeave(NavigationEntry entry) {
         var away = new NavigatingEventArgs {
            NavigationItem = entry.Navigation,
            Entry = entry,
            Data = entry.Data
         };
         await entry.Body.OnNavigatingAway(entry.Navigation, away);
         return !away.Cancel;
      }

      // -1 for home and for a stack that has not shown anything yet; otherwise the position of the
      // entry being shown.
      private int IndexOfCurrent() => Current == null || Current == Home ? -1 : _entries.IndexOf(Current);

      // Only ever called for entries that have already left the path, so the body being shown is never
      // among them - releasing a mounted body would run OnRelease on a control the user is still
      // looking at. Released one at a time rather than all at once: a body is free to touch the UI
      // while it tears itself down, and starting them together would let two of them interleave.
      private static async Task ReleaseEntries(IEnumerable<NavigationEntry> entries) {
         foreach (var entry in entries) await entry.Release();
      }

      private void SetCurrent(NavigationEntry? entry) {
         Current = entry;
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
      }

      private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

      private void RaiseNavigated(INavigation sender, NavigationEntry target) {
         RaiseChanged();
         EmApp.RaiseNavigated(sender, new NavigationEventArgs {
            NavigationItem = target.Navigation,
            Entry = target,
            Data = target.Data
         });
      }

      #endregion
   }
}
