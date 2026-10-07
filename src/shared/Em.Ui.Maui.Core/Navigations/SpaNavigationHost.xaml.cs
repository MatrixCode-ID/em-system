using System.ComponentModel;
using Microsoft.Maui.Controls;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The main page of the application: the top bar, the account panel entering from the right, and one
   /// empty place that is filled with the body of the screen being opened. This is the one and only page of
   /// the application - changing screen means replacing the content of that place, not stacking a new page.
   /// </summary>
   public partial class SpaNavigationHost : ContentPage
   {
      // The duration of the account panel's curtain animation, and how dark it is when the panel is fully
      // open. The numbers follow standard Material motion: opening is slightly slower than closing.
      private const uint OpenDuration = 260;
      private const uint CloseDuration = 200;
      private const double ScrimOpacity = 0.4;

      public SpaNavigationHost(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.HostPage = this;
         Vm.PropertyChanged += VmOnPropertyChanged;
         Vm.Stack = app.MainStack;

         AccountPanelView.Vm.EmApp = app;
         AccountPanelView.Vm.HostPage = this;
         // A panel that closes itself would only slide out without its curtain fading with it: that curtain
         // belongs to this page. So the panel asks for it, and this page does the work.
         AccountPanelView.Vm.CloseRequested += (_, _) => Vm.IsAccountPanelOpen = false;

         app.ActiveUserChanged += (_, _) => Vm.RefreshAccount();
         app.ActiveConnectionChanged += (_, _) => Vm.RefreshAccount();
         app.ThemeChanged += (_, _) => AccountPanelView.Vm.RefreshTheme();
         // A session can end without anyone pressing the sign-out button - its lifetime ran out, or it was
         // revoked from elsewhere. Both must land on the login screen through exactly the same way.
         app.SessionEnded += OnSessionEnded;
      }

      /// <summary>The view model of this page, read back from the BindingContext set in XAML.</summary>
      public SpaNavigationHostVm Vm => (SpaNavigationHostVm)BindingContext;

      // The two things below are indeed display business, not the view model's: attaching a control to a slot
      // moves its instance, not a value, and sliding the panel together with its curtain is an animation on
      // concrete elements. Neither can be expressed as a binding.
      private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         switch (e.PropertyName) {
            case nameof(SpaNavigationHostVm.Entry):
               BodyHost.Content = Vm.Entry?.Body as View;
               break;
            case nameof(SpaNavigationHostVm.IsAccountPanelOpen):
               _ = AnimateAccountPanelAsync(Vm.IsAccountPanelOpen);
               break;
         }
      }

      private async Task AnimateAccountPanelAsync(bool open) {
         // The curtain is made touchable first when opening, and only released after it is fully closed when
         // closing - so there is never a moment where the panel is still visible but touches already pass
         // through to the page content behind it.
         if (open) Scrim.InputTransparent = false;

         var duration = open ? OpenDuration : CloseDuration;
         var easing = open ? Easing.CubicOut : Easing.CubicIn;

         await Task.WhenAll(
            AccountPanelView.AnimateAsync(open, Vm.Navigation),
            Scrim.FadeToAsync(open ? ScrimOpacity : 0, duration, easing));

         if (!open) Scrim.InputTransparent = true;
      }

      // The device's back button is the exit used most often on Android, so it must understand the state of
      // the screen: close the account panel if it is open, go back one screen if there is still a path to
      // walk, and only give up to the default behavior otherwise.
      protected override bool OnBackButtonPressed() {
         if (Vm.IsAccountPanelOpen) {
            Vm.IsAccountPanelOpen = false;
            return true;
         }

         if (Vm.Stack is not { CanGoBack: true } stack) return base.OnBackButtonPressed();

         _ = stack.Backward();
         return true;
      }

      private void OnSessionEnded(object? sender, SessionEndedEventArgs e) {
         Dispatcher.Dispatch(() => {
            Vm.IsAccountPanelOpen = false;
            _ = Vm.EmApp!.ShowLoginScreen(e.Reason);
         });
      }
   }

   /// <summary>View model of the single-page navigation host.</summary>
   public class SpaNavigationHostVm : MvvmModelBase
   {
      public SpaNavigationHostVm() {
         RegisterCommand(nameof(LeadingCommand), LeadingCommand, LeadingCommandAllowed);
         RegisterCommand(nameof(HomeCommand), HomeCommand, HomeCommandAllowed);
         RegisterCommand(nameof(ForwardCommand), ForwardCommand, ForwardCommandAllowed);
         RegisterCommand(nameof(ReloadCommand), ReloadCommand, ReloadCommandAllowed);
         RegisterCommand(nameof(AccountPanelCommand), AccountPanelCommand, AccountPanelCommandAllowed);
         RegisterCommand(nameof(CloseAccountPanelCommand), CloseAccountPanelCommand);
      }

      /// <summary>Whether the account panel is open. The page animates the change.</summary>
      public bool IsAccountPanelOpen {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// The stack shown by this host. The host follows that stack itself - the entry being shown and the
      /// content of its path - so no one else needs to push changes into it.
      /// </summary>
      public NavigationStack? Stack {
         get;
         set {
            if (field != null) {
               field.PropertyChanged -= StackPropertyChanged;
               field.Changed -= StackChanged;
            }

            field = value;
            if (field != null) {
               field.PropertyChanged += StackPropertyChanged;
               field.Changed += StackChanged;
            }

            Entry = field?.Current;
         }
      }

      /// <summary>The entry being shown, or <c>null</c> before anything has ever been shown.</summary>
      public NavigationEntry? Entry {
         get;
         private set {
            if (field == value) return;

            // The title belongs to the entry and may be renamed while it is shown, so the page has to
            // stop listening to the one it is leaving - an entry that stays alive in the stack keeps
            // raising changes, and an old subscription would let it repaint a bar it no longer owns.
            if (field != null) field.PropertyChanged -= EntryPropertyChanged;
            field = value;
            if (field != null) field.PropertyChanged += EntryPropertyChanged;

            NotifyChanged();
            NotifyChanged(nameof(Navigation));
            NotifyChanged(nameof(IsToolbarVisible));
            NotifyChanged(nameof(IsTitleVisible));
            NotifyChanged(nameof(IsReloadVisible));
            RefreshNavigationCommands();
         }
      }

      /// <summary>The definition of the screen being shown; the source of the toolbar switches.</summary>
      public Navigation? Navigation => Entry?.Navigation;

      private void StackPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationStack.Current)) Entry = Stack?.Current;
      }

      private void StackChanged(object? sender, EventArgs e) => RefreshNavigationCommands();

      private void EntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
         if (e.PropertyName == nameof(NavigationEntry.Title)) NotifyChanged(nameof(Title));
      }

      // Home is never an entry of the navigation path, so "being at home" means that what is shown is the
      // home entry itself - or that nothing has been shown at all yet.
      private bool IsAtHome => Stack?.Current is null || Stack.Current == Stack.Home;

      /// <summary>The title in the top bar: the application name at home, the title of its entry on another screen.</summary>
      public string Title => IsAtHome
         ? EmApp?.Branding.DisplayTitle ?? string.Empty
         : Entry?.Title ?? string.Empty;

      /// <summary>The caption below the title.</summary>
      public string Subtitle => IsAtHome
         ? EmApp?.Branding.DisplayTagline ?? string.Empty
         : Navigation?.Subtitle ?? string.Empty;

      /// <summary><c>true</c> when there is a caption to draw below the title.</summary>
      public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

      /// <summary>Whether there is still a screen ahead that can be gone to.</summary>
      /// <remarks>
      /// There is no button for it in the Android top bar, but the path is deliberately kept: this host can be
      /// used again in MAUI desktop, where going forward has meaning and its own place.
      /// </remarks>
      public bool CanGoForward {
         get {
            if (Navigation is null) return false;
            if (!Navigation.IsForwardVisible) return false;

            return Stack?.CanGoForward == true;
         }
      }

      // Every top bar switch is owned by its navigation. Without a navigation at all there is nothing to draw,
      // so the answer is false - not a default value owned by Navigation.
      public bool IsToolbarVisible => Navigation?.IsToolbarVisible ?? false;
      public bool IsTitleVisible => Navigation?.IsTitleVisible ?? false;
      public bool IsReloadVisible => Navigation?.IsReloadVisible ?? false;

      /// <summary>
      /// Whether the back button needs to be drawn. At home the answer is always no: its navigation path is
      /// still empty, and a back button that leads nowhere only confuses.
      /// </summary>
      public bool IsBackVisible => !IsAtHome && (Navigation?.IsBackVisible ?? false);

      /// <summary>Whether the home button needs to be drawn; like back, it is of no use at home.</summary>
      public bool IsHomeVisible => !IsAtHome && (Navigation?.IsHomeVisible ?? false);

      /// <summary>
      /// Whether the account badge needs to be drawn. Unlike back and home, this badge is on every screen
      /// including home - it is the one and only way to the account panel.
      /// </summary>
      public bool IsUserVisible => Navigation?.IsUserVisible ?? false;

      /// <summary>
      /// The initials of the signed-in account, the content of the badge circle. The only account information
      /// this page still draws - the rest belongs to the account panel. The full name comes first, and the
      /// account name is the fallback when the contact has no name yet.
      /// </summary>
      public string AccountInitials => EmApp?.ActiveUser is { } user
         ? (user.cContactFullName is { Length: > 0 } fullName ? fullName : user.cUserAccount).ToInitials()
         : "?";

      /// <summary>
      /// Asks all top bar buttons to recompute whether they may still be pressed. Called every time the
      /// content of the navigation path changes.
      /// </summary>
      public void RefreshNavigationCommands() {
         NotifyChanged(nameof(CanGoForward));
         NotifyChanged(nameof(IsBackVisible));
         NotifyChanged(nameof(IsHomeVisible));
         NotifyChanged(nameof(IsUserVisible));
         NotifyChanged(nameof(Title));
         NotifyChanged(nameof(Subtitle));
         NotifyChanged(nameof(HasSubtitle));
         Commands[nameof(LeadingCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(HomeCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(ForwardCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(ReloadCommand)]?.RaiseCanExecuteChanged();
      }

      /// <summary>Redraws the account badge after the user or the server changed.</summary>
      public void RefreshAccount() => NotifyChanged(nameof(AccountInitials));

      /// <summary>The leftmost button of the top bar: go back one screen.</summary>
      public Task LeadingCommand() => Stack!.Backward();
      public bool LeadingCommandAllowed() => Stack?.CanGoBack == true;

      /// <summary>Go home from any screen, without having to go back step by step.</summary>
      public Task HomeCommand() => Stack!.NavigateHome();
      public bool HomeCommandAllowed() => Stack?.Home is not null && !IsAtHome;

      public Task ForwardCommand() => Stack!.Forward();
      public bool ForwardCommandAllowed() => CanGoForward;

      public Task ReloadCommand() => Entry?.Reload() ?? Task.CompletedTask;
      public bool ReloadCommandAllowed() => Entry is not null;

      /// <summary>Opens - or closes again - the account panel through the badge in the top bar.</summary>
      public void AccountPanelCommand() => IsAccountPanelOpen = !IsAccountPanelOpen;
      public bool AccountPanelCommandAllowed() => EmApp is not null;

      public void CloseAccountPanelCommand() => IsAccountPanelOpen = false;
   }
}
