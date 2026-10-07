using System.ComponentModel;
using Em.Ui.Core.Shared;
// Both namespaces have an INavigation, and the one meant here is always Em's.
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// One place in a <see cref="NavigationStack"/>: a navigation that is open together with its own body,
   /// data, and title. Created by the stack when a screen is opened with a title that does not yet exist;
   /// modules do not create it themselves but receive it through <see cref="NavigationEventArgs.Entry"/> or
   /// <see cref="Shared.MvvmModelBase.NavigationEntry"/>.
   /// </summary>
   public sealed class NavigationEntry : INavigationEntry, INotifyPropertyChanged
   {
      private INavigationBody? _body;
      private bool _released;

      internal NavigationEntry(NavigationStack stack, Navigation navigation, string title, object? data) {
         Stack = stack;
         Navigation = navigation;
         Title = title;
         Data = data;
      }

      #region Explicit INavigationEntry Implementation

      INavigation INavigationEntry.Navigation => Navigation;
      INavigationStack INavigationEntry.Stack => Stack;
      Task<bool> INavigationEntry.NavigateTo(INavigation navigation, object? data) =>
         NavigateTo((Navigation)navigation, data);

      #endregion

      /// <summary>The definition of the screen this entry opens.</summary>
      public Navigation Navigation { get; }

      /// <summary>The stack that holds this entry.</summary>
      public NavigationStack Stack { get; }

      /// <summary>The application object that owns this entry.</summary>
      public EmApp EmApp => Stack.EmApp;

      /// <summary>
      /// The title shown, which is also this entry's unique key across the whole application. Changed through
      /// <see cref="SetTitle"/>, not written directly, so its uniqueness stays guarded.
      /// </summary>
      public string Title { get; private set; }

      /// <summary>The parameter used when this entry was opened, or <c>null</c> when there is none.</summary>
      public object? Data { get; }

      /// <summary>
      /// The body owned by this entry. An entry opened through navigation already built it when it was
      /// created; only home waits until it is really shown, because before anyone has signed in the home body
      /// must not be built yet.
      /// </summary>
      /// <exception cref="InvalidOperationException">When this entry has already been released from its stack.</exception>
      public INavigationBody Body {
         get {
            // A released entry has left every stack for good; building a fresh body for it here would
            // bring back a control nobody is ever going to release.
            if (_released) throw new InvalidOperationException($"Navigation entry '{Title}' has already been released.");
            return _body ??= Navigation.BodyType.Create(this);
         }
      }

      // Reading Body builds it on demand, so anything that only wants to look at or release what
      // already exists has to ask this first instead of touching the property.
      internal bool HasBody => _body != null;

      /// <inheritdoc />
      public Task Reload() =>
         Body.OnReloadRequested(Navigation, new NavigationEventArgs {
            NavigationItem = Navigation,
            Entry = this,
            Data = Data,
         });

      /// <inheritdoc />
      public bool SetTitle(string title) {
         ArgumentException.ThrowIfNullOrWhiteSpace(title);
         if (string.Equals(Title, title, StringComparison.Ordinal)) return true;

         // The same entry may be renamed to a different casing of its own title, which is still the
         // same key and so must not count as a clash with itself.
         var owner = EmApp.FindEntry(title);
         if (owner != null && owner != this) return false;

         Title = title;
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
         return true;
      }

      /// <inheritdoc />
      public Task<bool> Close() => Stack.Close(this);

      /// <inheritdoc cref="INavigationEntry.NavigateTo(string,object?)" />
      public Task<bool> NavigateTo(string name, object? data = null) => EmApp.NavigateTo(name, data, Stack);

      /// <inheritdoc cref="INavigationEntry.NavigateTo(INavigation,object?)" />
      public Task<bool> NavigateTo(Navigation navigation, object? data = null) =>
         EmApp.NavigateTo(navigation, data, Stack);

      // Called by the stack once this entry has left it and its body is no longer mounted anywhere.
      // Dropping the reference is what actually frees the control, so OnRelease is only there for
      // what would otherwise outlive it - subscriptions to long-lived publishers, timers, caches.
      internal async Task Release() {
         // The home body is the one control that always stays alive: every path out of the stack
         // lands on it, so it must never be in a state where it has to be rebuilt first.
         if (_released || this == Stack.Home) return;

         _released = true;
         if (_body == null) return;

         // The reference is dropped only after OnRelease has finished, so the body is still whole for
         // as long as it is tearing itself down.
         var body = _body;
         await body.OnRelease(Navigation);
         _body = null;
      }

      /// <inheritdoc />
      public event PropertyChangedEventHandler? PropertyChanged;
   }
}
