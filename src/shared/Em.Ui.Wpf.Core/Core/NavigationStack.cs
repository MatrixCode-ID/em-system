using System.ComponentModel;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Jalur navigasi yang ditampilkan satu host: deretan <see cref="NavigationEntry"/> yang bisa
   /// ditelusuri maju-mundur, ditambah satu home opsional di depannya. Stack utama aplikasi dipegang
   /// <see cref="EmApp.MainStack"/>; membuka layar dilakukan lewat router
   /// (<see cref="EmApp.NavigateTo(string,object?)"/>) atau lewat entri
   /// (<see cref="NavigationEntry.NavigateTo(string,object?)"/>), bukan lewat stack ini langsung.
   /// <para>
   /// Di layout multi-tab, stack dipakai dalam mode bertab: setiap entri adalah satu tab window-nya,
   /// entri baru selalu ditambahkan di ujung tanpa membuang apa pun, dan tidak ada maju-mundur.
   /// </para>
   /// </summary>
   public sealed class NavigationStack : INavigationStack, INotifyPropertyChanged
   {
      // Titles are keys, and a key that differs only in casing is still the same document.
      internal const StringComparison TitleComparison = StringComparison.OrdinalIgnoreCase;

      private readonly List<NavigationEntry> _entries = [];

      // Tabbed stacks only: the entries in the order they were last made current, the most recent
      // last. Closing the tab being shown lands on the one the user was on before it.
      private readonly List<NavigationEntry> _activationHistory = [];

      internal NavigationStack(EmApp app, Navigation? home, bool tabbed = false) {
         EmApp = app;
         IsTabbed = tabbed;
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

      // A tabbed stack backs one window of the multi-tab layout: every entry is a tab, a new entry is
      // appended without dropping anything, and there is no back or forward.
      internal bool IsTabbed { get; }

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
            if (IsTabbed) return false;
            var index = IndexOfCurrent();
            return index > 0 || (index == 0 && Home != null);
         }
      }

      /// <summary>Apakah <see cref="Forward"/> punya tempat untuk dituju.</summary>
      public bool CanGoForward {
         get {
            if (IsTabbed) return false;
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
         if (IsTabbed) return Task.FromResult(false);
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
         // Tabs are not a path: nothing lies "ahead" of the tab being shown.
         if (IsTabbed) return;

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
      // askSource is false only for a detach: the body being left is not left at all, it moves to a
      // window of its own with everything on it, so there is nothing for it to object to.
      internal async Task<bool> MoveTo(NavigationEntry target, bool askSource = true) {
         if (target == Current) return true;

         var source = Current;
         var sender = source?.Navigation ?? target.Navigation;

         // The body being left is the one that gets to object to the move. Home never does: every
         // way out of the stack lands on it, so it must never be able to refuse being left.
         if (askSource && source != null && source != Home) {
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
         // On a tabbed stack it simply becomes the rightmost tab.
         if (!IsTabbed) await ClearForwardStacks();
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
         dropped.ForEach(Forget);
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
            var fallback = IsTabbed ? TabFallback(entry)
               : index > 0 ? _entries[index - 1]
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
         Forget(entry);
         if (entry == Current) SetCurrent(null);
         await entry.Release();
         RaiseChanged();
         return true;
      }

      // Takes the entry being shown out of this path so it can become the root of a stack of its own,
      // body and unsaved input included. Neither OnNavigatingAway nor OnRelease: the entry is moved,
      // not left behind. The position steps back to the previous entry (or home) the same way Back
      // does - without a reload - and nothing is done at all if that would leave the stack empty.
      // A tabbed stack lets any of its tabs go to another window, and may be left empty by it.
      internal async Task<bool> Extract(NavigationEntry entry) {
         if (IsTabbed) return await ExtractTab(entry);
         if (entry != Current) return false;

         var index = _entries.IndexOf(entry);
         if (index < 0) return false;

         var fallback = index > 0 ? _entries[index - 1]
            : Home ?? (index + 1 < _entries.Count ? _entries[index + 1] : null);
         if (fallback == null) return false;

         if (!await MoveTo(fallback, askSource: false)) return false;

         _entries.Remove(entry);
         RaiseChanged();
         return true;
      }

      private async Task<bool> ExtractTab(NavigationEntry entry) {
         if (!_entries.Contains(entry)) return false;

         if (entry == Current) {
            // The same tab closing it would land on, without asking the one that leaves: it is not
            // left, it moves.
            if (TabFallback(entry) is { } fallback) {
               if (!await MoveTo(fallback, askSource: false)) return false;
            }
            else SetCurrent(null);
         }

         _entries.Remove(entry);
         Forget(entry);
         RaiseChanged();
         return true;
      }

      // The other half of a detach: the extracted entry becomes the root, and the one entry, of this
      // stack - which is freshly made for it and has no home.
      internal void Adopt(NavigationEntry entry) {
         entry.Stack = this;
         _entries.Add(entry);
         SetCurrent(entry);
         RaiseChanged();
      }

      // A tab arriving from another window. It is only placed, not shown: the caller then moves to it,
      // which is what gives the body shown here the chance to refuse - the tab stays either way.
      internal void Adopt(NavigationEntry entry, int index) {
         entry.Stack = this;
         _entries.Insert(Math.Clamp(index, 0, _entries.Count), entry);
         RaiseChanged();
      }

      // Reorders the tabs; nothing is shown, left or reloaded, so no body is told.
      internal void Move(NavigationEntry entry, int index) {
         var from = _entries.IndexOf(entry);
         if (from < 0) return;

         index = Math.Clamp(index, 0, _entries.Count - 1);
         if (index == from) return;

         _entries.RemoveAt(from);
         _entries.Insert(index, entry);
         RaiseChanged();
      }

      // The tab to show once the one leaving is gone: the tab the user was on before it, then its
      // right-hand neighbour, then its left-hand one.
      private NavigationEntry? TabFallback(NavigationEntry leaving) {
         for (var i = _activationHistory.Count - 1; i >= 0; i--) {
            var candidate = _activationHistory[i];
            if (candidate != leaving && _entries.Contains(candidate)) return candidate;
         }

         var index = _entries.IndexOf(leaving);
         if (index + 1 < _entries.Count) return _entries[index + 1];
         return index > 0 ? _entries[index - 1] : null;
      }

      private void Forget(NavigationEntry entry) => _activationHistory.Remove(entry);

      // Asked before the window showing this stack closes: only the body being shown can hold input
      // the user is looking at, so it alone gets to refuse.
      internal Task<bool> AskCurrentToLeave() =>
         Current is { } current && current != Home ? AskToLeave(current) : Task.FromResult(true);

      // Empties the stack for good, the body being shown included - only called once the window
      // showing it is closing, so nothing is mounted anywhere any more. Home, if there is one, stays.
      internal async Task ReleaseAll() {
         var released = _entries.ToList();
         if (released.Count == 0 && (Current == null || Current == Home)) return;

         _entries.Clear();
         _activationHistory.Clear();
         SetCurrent(null);
         await ReleaseEntries(released);
         RaiseChanged();
      }

      internal static async Task<bool> AskToLeave(NavigationEntry entry) {
         var away = new NavigatingEventArgs {
            NavigationItem = entry.Navigation,
            Entry = entry,
            Data = entry.Data
         };
         await entry.Body.OnNavigatingAway(entry.Navigation, away);
         return !away.Cancel;
      }

      // The position of an entry on the path, -1 when it is not on it (home never is).
      internal int IndexOf(NavigationEntry entry) => _entries.IndexOf(entry);

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
         if (IsTabbed && entry != null) {
            _activationHistory.Remove(entry);
            _activationHistory.Add(entry);
         }

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
