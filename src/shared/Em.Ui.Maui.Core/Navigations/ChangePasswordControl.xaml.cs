using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Shared;
using INavigation = Em.Ui.Core.Shared.INavigation;
using NavigationEventArgs = Em.Ui.Core.Shared.NavigationEventArgs;

namespace Em.Ui.Maui.Navigations
{
   /// <summary>
   /// The change password screen of the signed-in user: the old password, the new password, and its
   /// repetition. Opened from the account panel, and only changes the password of its own user - not
   /// someone else's, whose place is the user manager screen.
   /// </summary>
   public partial class ChangePasswordControl : ContentView, INavigationBody
   {
      public ChangePasswordControl() {
         InitializeComponent();
      }

      /// <summary>The view model of this screen, read back from the BindingContext set in XAML.</summary>
      public ChangePasswordControlVm Vm => (ChangePasswordControlVm)BindingContext;

      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) => Task.CompletedTask;
   }

   /// <summary>
   /// One password strength rule as shown on screen: its sentence, and whether the password being typed
   /// already meets it.
   /// </summary>
   public sealed class PasswordRuleVm : NotifyPropertyBase
   {
      /// <summary>Kalimat aturannya, mis. "At least 8 characters".</summary>
      public required string Caption { get; init; }

      /// <summary>Whether the password being typed already meets this rule.</summary>
      public bool IsMet {
         get => Get<bool>();
         set => Set(value, _ => {
            NotifyChanged(nameof(Glyph));
            NotifyChanged(nameof(MarkColor));
         });
      }

      /// <summary>A check mark when it is met, a cross when it is not.</summary>
      public string Glyph => IsMet ? FontIcons.Check : FontIcons.Xmark;

      /// <summary>
      /// The marker color of this rule. A translucent gray while not yet met - not red - because a rule that
      /// is not yet met while the password is still being typed is not an error, only unfinished.
      /// </summary>
      public Color MarkColor => IsMet ? Colors.SeaGreen : Color.FromRgba(128, 128, 128, 140);
   }

   /// <summary>View model <see cref="ChangePasswordControl"/>.</summary>
   public class ChangePasswordControlVm : MvvmModelBase
   {
      /// <summary>
      /// The only answer when the old password is refused by the server. Like on the login screen, it says
      /// nothing more than that.
      /// </summary>
      public const string InvalidCurrentPasswordMessage = "The current password is not correct.";

      public ChangePasswordControlVm() {
         RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
      }

      /// <summary>The password strength rules in force, already filtered to those that are really shown.</summary>
      public ObservableCollection<PasswordRuleVm> Rules { get; } = [];

      /// <summary>The account whose password is being changed.</summary>
      public string AccountCaption => EmApp?.ActiveUser is { } user
         ? $"Signed in as {user.cUserAccount}"
         : string.Empty;

      /// <summary>The password that applies now.</summary>
      public string CurrentPassword {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SaveError = null;
            RaiseSaveCommandChanged();
         });
      }

      /// <summary>The new password that is asked for.</summary>
      public string NewPassword {
         get => Get<string>(string.Empty);
         set => Set(value, password => {
            SaveError = null;
            RefreshRules(password);
            NotifyChanged(nameof(Strength));
            NotifyChanged(nameof(HasConfirmMismatch));
            RaiseSaveCommandChanged();
         });
      }

      /// <summary>The repetition of the new password.</summary>
      public string ConfirmPassword {
         get => Get<string>(string.Empty);
         set => Set(value, _ => {
            SaveError = null;
            NotifyChanged(nameof(HasConfirmMismatch));
            RaiseSaveCommandChanged();
         });
      }

      /// <summary>
      /// <c>true</c> when both new password boxes are filled in but their content differs. While the repetition
      /// box is still empty nothing is wrong - the person has not finished typing.
      /// </summary>
      public bool HasConfirmMismatch =>
         ConfirmPassword.Length > 0 && ConfirmPassword != NewPassword;

      /// <summary><c>true</c> when there are password strength rules to draw at all.</summary>
      public bool HasRules => Rules.Count > 0;

      /// <summary>
      /// How many of the rules are met, from 0 to 1, for the password strength meter.
      /// </summary>
      public double Strength {
         get {
            if (EmApp?.PasswordPolicy is not { ShownRuleCount: > 0 } policy) return 0;
            return (double)policy.CountMetShownRules(NewPassword) / policy.ShownRuleCount;
         }
      }

      /// <summary>The last failure message, or <c>null</c> when there is none.</summary>
      public string? SaveError {
         get => Get<string?>();
         set => Set(value, _ => NotifyChanged(nameof(HasSaveError)));
      }

      /// <summary><c>true</c> when there is a failure message to show.</summary>
      public bool HasSaveError => !string.IsNullOrWhiteSpace(SaveError);

      /// <summary>
      /// Empties all three password boxes and rebuilds the rule list. Called every time this screen is
      /// opened: a password left over from a previous visit has no reason to outlive that visit.
      /// </summary>
      public Task ReloadAsync() {
         CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
         SaveError = null;

         RefreshRules(string.Empty);
         NotifyChanged(nameof(AccountCaption));
         NotifyChanged(nameof(Strength));
         RaiseSaveCommandChanged();
         return Task.CompletedTask;
      }

      // The rule list is rebuilt, not just refreshed in value: which rules are shown is decided by the
      // application's PasswordPolicy, and that can only be read after EmApp is attached.
      private void RefreshRules(string password) {
         if (EmApp?.PasswordPolicy is not { } policy) return;

         Rules.Clear();
         if (policy.IsMinLengthShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = $"At least {policy.MinLength} characters",
               IsMet = policy.HasMinLength(password)
            });
         }

         if (policy.IsMixedCaseShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = "Upper and lower case letters",
               IsMet = PasswordPolicy.HasMixedCase(password)
            });
         }

         if (policy.IsDigitShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = "At least one digit",
               IsMet = PasswordPolicy.HasDigit(password)
            });
         }

         if (policy.IsSymbolShown) {
            Rules.Add(new PasswordRuleVm {
               Caption = "At least one symbol",
               IsMet = PasswordPolicy.HasSymbol(password)
            });
         }

         NotifyChanged(nameof(HasRules));
      }

      /// <summary>
      /// Sends the password change to the server. If it succeeds, this screen is left; if not,
      /// <see cref="SaveError"/> is filled and the screen stays where it is - this command never throws an
      /// exception to its caller.
      /// </summary>
      // Nothing may escape this method. ICommand.Execute is void, so UiCommandAsync runs it as
      // async void: an exception leaving here is rethrown on the dispatcher, and there is nothing
      // above it to catch it - the process ends.
      public async Task SaveCommand() {
         SaveError = null;

         try {
            WaiterText = "Saving...";
            IsBusy = InWaiting = true;
            RaiseSaveCommandChanged();

            var app = EmApp!;
            var services = app.ServiceProvider.GetRequiredService<ICredentialServices>();
            await services.PostMeta_ChangeMyPassword(CurrentPassword, NewPassword);

            // The password has changed, so there is nothing left to do here. All three boxes are emptied first so
            // the password that was just typed is not left on a screen that is about to be released.
            CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
            ShowInfo("Password changed", "Your password has been changed.");
            if (NavigationEntry is { } entry) await entry.Stack.Backward();
         }
         catch (ActionException x) when (x.StatusCode == 401) {
            CurrentPassword = string.Empty;
            SaveError = InvalidCurrentPasswordMessage;
         }
         catch (Exception x) {
            SaveError = x.SerializedMessagesDefault();
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseSaveCommandChanged();
         }
      }

      /// <summary>
      /// Save may only run when all three boxes are filled in, both new passwords are the same, the new
      /// password differs from the old one, and every rule whose level is required is met.
      /// </summary>
      public bool SaveCommandAllowed() =>
         IsNotBusy
         && !string.IsNullOrWhiteSpace(CurrentPassword)
         && !string.IsNullOrWhiteSpace(NewPassword)
         && ConfirmPassword == NewPassword
         && NewPassword != CurrentPassword
         && (EmApp?.PasswordPolicy.IsSatisfiedBy(NewPassword) ?? false);

      private void RaiseSaveCommandChanged() =>
         Commands[nameof(SaveCommand)]?.RaiseCanExecuteChanged();
   }
}
