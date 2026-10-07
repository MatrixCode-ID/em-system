using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Clipboard = System.Windows.Clipboard;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations
{
   public partial class UserEditor : UserControl, INavigationBody
   {
      private EmApp _app;
      /// <summary>Creates a new instance of <see cref="UserEditor"/>.</summary>
      public UserEditor(EmApp app) {
         InitializeComponent();
         _app = app;
         Vm.AttachApp(_app);
         Vm.PasswordBoxSyncRequested += SyncPasswordBoxes;
      }

      /// <summary>The vm.</summary>
      public UserEditorVm Vm => (UserEditorVm)DataContext;

      // Only the payload is taken here, never the record: the host raises OnNavigatingIn on every
      // way into this screen, back and forward included, and those two re-enter a form the user may
      // have half filled in. Opening the record belongs to OnReloadRequested, which only a real
      // navigation raises.
      /// <inheritdoc />
      public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
         if (args.Data is not UserEditorNavigationPayload payload) {
            args.Cancel = true;
            args.Message = "The user editor can only be opened with a user payload.";
            return Task.CompletedTask;
         }

         Vm.Payload = payload;
         return Task.CompletedTask;
      }

      // Leaving the screen is what throws unsaved edits away, so the body being left is the one
      // that has to ask - which is exactly the chance the host gives it here.
      /// <inheritdoc />
      public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) {
         if (!Vm.HasUnsavedChanges) return Task.CompletedTask;

         var answer = (Vm.DialogOwner ?? _app.MainWindow).ShowMboxDecideWarning(
            "This user has changes that have not been saved. Leave the screen and lose them?",
            "Unsaved changes");

         if (answer == MessageBoxResult.Yes) return Task.CompletedTask;

         args.Cancel = true;
         args.Message = "The user still has unsaved changes.";
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) {
         if (args.Data is UserEditorNavigationPayload payload)
            Vm.LoadFrom(payload);

         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task OnRelease(INavigation sender) {
         Vm.PasswordBoxSyncRequested -= SyncPasswordBoxes;
         Vm.Release();
         return Task.CompletedTask;
      }

      #region Password boxes

      // A PasswordBox keeps its value out of the property system on purpose, so there is no
      // Password dependency property to bind to. These two handlers are the whole exception: each
      // one only hands the typed value to the view model, which owns every rule built on top of it.

      private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.NewPassword = ((PasswordBox)sender).Password;

      private void RepeatPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
         Vm.RepeatPassword = ((PasswordBox)sender).Password;

      // The other half of the same exception: a value the view model changed on its own - the text
      // typed into the revealed box, or a password cleared after it was set - cannot reach a
      // PasswordBox through a binding, so the view model asks for it to be written here instead.
      // The comparison is what stops the write coming straight back as a PasswordChanged.
      private void SyncPasswordBoxes() {
         if (NewPasswordBox.Password != Vm.NewPassword)
            NewPasswordBox.Password = Vm.NewPassword;

         if (RepeatPasswordBox.Password != Vm.RepeatPassword)
            RepeatPasswordBox.Password = Vm.RepeatPassword;
      }

      #endregion
   }

   /// <summary>View model of the user editor screen.</summary>
   public class UserEditorVm : MvvmModelBase
   {
      // How many bars the strength meter draws. Fixed, unlike the number of rules: a password that
      // meets every rule in force fills the meter, whether that is one rule or four.
      private const int PasswordRuleCount = 4;

      // Shown wherever a value belongs to a record that does not exist yet, or to a part of the
      // system that is not storing anything so far.
      private const string NoValue = "-";

      /// <summary>Creates a new instance of <see cref="UserEditorVm"/>.</summary>
      public UserEditorVm() {
         RegisterCommand(nameof(SaveCommand), SaveCommand, SaveCommandAllowed);
         RegisterCommand(nameof(DiscardCommand), DiscardCommand, DiscardCommandAllowed);
         RegisterCommand(nameof(SuspendCommand), SuspendCommand, SuspendCommandAllowed);
         RegisterCommand(nameof(CopyRecordIdCommand), CopyRecordIdCommand, CopyRecordIdCommandAllowed);
         RegisterCommand(nameof(PickContactCommand), PickContactCommand, PickContactCommandAllowed);
         RegisterCommand(nameof(SetPasswordCommand), SetPasswordCommand, SetPasswordCommandAllowed);
         RegisterCommand(nameof(TogglePasswordRevealCommand), TogglePasswordRevealCommand);
      }

      /// <summary>
      /// Requested by the view model when the content of the password box on screen needs to be synchronized
      /// again with the value it holds. PasswordBox deliberately does not expose its value through binding, so
      /// only the code-behind can write it back.
      /// </summary>
      public event Action? PasswordBoxSyncRequested;

      /// <summary>
      /// Connects this view model to the application, then tells the UI to re-evaluate the password rule
      /// bindings. The notification is required: XAML creates this view model together with all its bindings
      /// before <see cref="MvvmModelBase.EmApp"/> could be set, so without it the rule list would be read from
      /// the default value too early and never follow the application's rules.
      /// </summary>
      /// <param name="app">The application object that owns this view model.</param>
      public void AttachApp(EmApp app) {
         EmApp = app;
         RefreshPasswordPolicy();
      }

      // The rules themselves do not change while the screen is open, so this is only called once - but all
      // the displays it builds are announced as one set, just like RefreshPasswordRules.
      private void RefreshPasswordPolicy() {
         NotifyChanged(nameof(IsPasswordPolicyShown));
         NotifyChanged(nameof(IsMinLengthRuleShown));
         NotifyChanged(nameof(IsMixedCaseRuleShown));
         NotifyChanged(nameof(IsDigitRuleShown));
         NotifyChanged(nameof(IsSymbolRuleShown));
         NotifyChanged(nameof(MinLengthRuleCaption));

         RefreshPasswordRules();
      }

      /// <summary>
      /// The navigation data that this screen has open: one user to edit, or a request to create a new user.
      /// Filled by the control when the navigation comes in, and the only link back to the user list that
      /// opened it.
      /// </summary>
      public UserEditorNavigationPayload? Payload { get; internal set; }

      #region Record

      /// <summary>
      /// The user row that is open, and the one and only place the input of this screen is stored - including
      /// for a new user, who already has an object of their own since the form opened even though its row is
      /// not yet in the database. For that reason every field on the screen can bind directly to it, with no
      /// copy that has to be copied back when saving.
      /// </summary>
      public User? Data {
         get => Get<User?>();
         private set => Set(value);
      }

      /// <summary>
      /// Indicates there are unsaved changes. Taken as-is from the open row, which already tracks it itself.
      /// </summary>
      public bool HasUnsavedChanges => Data?.IsDirty == true;

      /// <summary>
      /// Indicates that the row really exists in the database. A row that has been written carries the server
      /// time of its creation, while a new row's date is still empty.
      /// </summary>
      public bool IsStored => Data != null && Data.datestamp != default;

      // The colon is escaped rather than typed plain: left to itself a custom format takes the
      // time separator from the current culture, and an Indonesian one writes 23.15 where the
      // screen is meant to read 23:15. Escaping pins the clock to the same shape everywhere.

      /// <summary>Caption of when this row was last saved, for the chip in the tool strip.</summary>
      public string SavedCaption =>
         IsStored ? $"Saved {Data!.ustamp:dd MMM yyyy, HH\\:mm}" : "Not saved yet";

      /// <summary>Caption of when this row was created.</summary>
      public string CreatedCaption => IsStored ? $"{Data!.datestamp:dd MMM yyyy}" : NoValue;

      /// <summary>
      /// Caption of when this row was last changed, complete with the time in 24-hour format.
      /// </summary>
      public string LastUpdatedCaption =>
         IsStored ? $"{Data!.ustamp:dd MMM yyyy, HH\\:mm}" : NoValue;

      #endregion

      #region Field rules

      // A field nobody has typed into yet is not wrong, it is only empty, so every rule below
      // passes on an empty value: what is required is decided by SaveCommandAllowed, and what is
      // well formed is decided here. Only a value that is actually there has to hold its shape.

      /// <summary>
      /// The shape of an e-mail address that is accepted: one @ sign, something on its left and right, and a
      /// domain part with at least one dot that has letters on both sides. Deliberately not the full RFC rule
      /// - what is caught here is a visible typo, not a strange address that is theoretically valid.
      /// </summary>
      private static readonly Regex EmailPattern =
         new(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$", RegexOptions.Compiled);

      /// <summary>
      /// The shape of a postal code that is accepted: 3 to 10 characters, letters and digits, optionally
      /// separated by a space or a hyphen in the middle. Deliberately loose because postal codes differ from
      /// country to country - only the length is guarded, matching the width of its column.
      /// </summary>
      private static readonly Regex ZipPattern =
         new(@"^[A-Za-z0-9][A-Za-z0-9 -]{1,8}[A-Za-z0-9]$", RegexOptions.Compiled);

      /// <summary>
      /// Indicates the typed e-mail address does not yet have the shape of a valid address. Used by the screen
      /// to turn its input box red and hold back the save button.
      /// </summary>
      public bool HasEmailError =>
         !string.IsNullOrWhiteSpace(Data?.cCommValue) && !EmailPattern.IsMatch(Data!.cCommValue.Trim());

      /// <summary>
      /// Indicates the typed postal code does not yet have the shape of a plausible postal code. Used by the
      /// screen to turn its input box red and hold back the save button.
      /// </summary>
      public bool HasZipError =>
         !string.IsNullOrWhiteSpace(Data?.cAddressZip) && !ZipPattern.IsMatch(Data!.cAddressZip!.Trim());

      #endregion

      #region Account state

      // The state is one value on the record, but the segmented control that sets it is four
      // buttons, so each button gets its own view of that one value. Only the button being switched
      // on says anything: the one being switched off is just the group making room for it.

      /// <summary>Marker of the "Active" button on the account state picker.</summary>
      public bool IsStateActive {
         get => Data?.cUserState == UserState.Active;
         set => SetStateWhenChecked(value, UserState.Active);
      }

      /// <summary>Marker of the "Pending" button on the account state picker.</summary>
      public bool IsStatePending {
         get => Data?.cUserState == UserState.Pending;
         set => SetStateWhenChecked(value, UserState.Pending);
      }

      /// <summary>Marker of the "Suspended" button on the account state picker.</summary>
      public bool IsStateSuspended {
         get => Data?.cUserState == UserState.Suspended;
         set => SetStateWhenChecked(value, UserState.Suspended);
      }

      /// <summary>Marker of the "Inactive" button on the account state picker.</summary>
      public bool IsStateInactive {
         get => Data?.cUserState == UserState.Inactive;
         set => SetStateWhenChecked(value, UserState.Inactive);
      }

      #endregion

      #region Fields with nowhere to be stored yet

      // Everything in this region is asked for by the screen but has no column, no service call and
      // no table behind it yet, so for now it lives on the view model and nowhere else. Each one is
      // a question still to be settled - whether it becomes a column of its own, or rides along in
      // the free-form data column - and until that is decided none of them is written anywhere.

      /// <summary>
      /// Screens that can be made the first destination after signing in to the application. For now there is
      /// only one, but the list is already here so it only needs to be added to.
      /// </summary>
      public IReadOnlyList<string> LandingNavigations { get; } = ["Home"];

      /// <summary>
      /// The navigation opened right after this account signs in to the application. For now there is only one
      /// choice and that choice is not yet stored anywhere.
      /// </summary>
      public string LandingNavigation {
         get => Get("Home");
         set => Set(value);
      }

      /// <summary>
      /// Caption of when this account last signed in to the application. Nobody records it yet, so its
      /// content is still fixed - the place is provided so it only needs to be filled once the recording
      /// exists.
      /// </summary>
      public string LastSignInCaption => "Not recorded yet";

      /// <summary>The new password that was typed, passed on from the password box on screen.</summary>
      public string NewPassword {
         get => Get(string.Empty);
         set => Set(value, _ => RefreshPasswordRules());
      }

      /// <summary>The repeat of the new password, used to make sure there is no typo.</summary>
      public string RepeatPassword {
         get => Get(string.Empty);
         set => Set(value, _ => RefreshPasswordRules());
      }

      /// <summary>
      /// Indicates the new password is being shown as-is, not as dots. When it is hidden again, the input that
      /// was typed is sent back to the password box so the two displays do not differ in content.
      /// </summary>
      public bool IsPasswordRevealed {
         get => Get<bool>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(IsPasswordMasked));
            NotifyChanged(nameof(PasswordRevealCaption));
            PasswordBoxSyncRequested?.Invoke();
         });
      }

      /// <summary>The opposite of <see cref="IsPasswordRevealed"/>, used by the password box that is hidden.</summary>
      public bool IsPasswordMasked => !IsPasswordRevealed;

      /// <summary>Caption of the eye button: it says what will happen if the button is pressed.</summary>
      public string PasswordRevealCaption => IsPasswordRevealed ? "Hide the password" : "Show the password";

      /// <summary>
      /// The password rules that apply. Read from the application, with the default value as a safety net:
      /// XAML creates this view model together with all its bindings before
      /// <see cref="MvvmModelBase.EmApp"/> could be set - see <see cref="AttachApp"/>.
      /// </summary>
      private PasswordPolicy Policy => EmApp?.PasswordPolicy ?? FallbackPolicy;

      private static readonly PasswordPolicy FallbackPolicy = new();

      /// <summary>Indicates the password length meets the minimum.</summary>
      public bool PasswordHasMinLength => Policy.HasMinLength(NewPassword);

      /// <summary>Indicates the password contains both uppercase and lowercase letters.</summary>
      public bool PasswordHasMixedCase => PasswordPolicy.HasMixedCase(NewPassword);

      /// <summary>Indicates the password contains at least one digit.</summary>
      public bool PasswordHasDigit => PasswordPolicy.HasDigit(NewPassword);

      /// <summary>Indicates the password contains at least one punctuation mark or symbol.</summary>
      public bool PasswordHasSymbol => PasswordPolicy.HasSymbol(NewPassword);

      /// <summary>Indicates the minimum length rule is in effect, so its row needs to be shown.</summary>
      public bool IsMinLengthRuleShown => Policy.IsMinLengthShown;

      /// <summary>Indicates the upper/lowercase rule is in effect.</summary>
      public bool IsMixedCaseRuleShown => Policy.IsMixedCaseShown;

      /// <summary>Indicates the digit rule is in effect.</summary>
      public bool IsDigitRuleShown => Policy.IsDigitShown;

      /// <summary>Indicates the symbol rule is in effect.</summary>
      public bool IsSymbolRuleShown => Policy.IsSymbolShown;

      /// <summary>
      /// Indicates there is still a rule in effect. When there is none at all, the password strength meter and
      /// the rule list are both hidden - neither has anything to say.
      /// </summary>
      public bool IsPasswordPolicyShown => Policy.ShownRuleCount > 0;

      /// <summary>
      /// Text of the minimum length rule row, e.g. "At least 12 characters". Assembled here because the number
      /// comes from the rule in effect, not from a fixed number on the screen.
      /// </summary>
      public string MinLengthRuleCaption => $"At least {Policy.MinLength} characters";

      /// <summary>
      /// Indicates the password satisfies all the required rules. A rule that is only a suggestion does not
      /// hold it back, so this is not the same thing as the strength meter being completely full.
      /// </summary>
      public bool PasswordMeetsPolicy => Policy.IsSatisfiedBy(NewPassword);

      /// <summary>Indicates both password boxes hold the same text and it is not empty.</summary>
      public bool PasswordsMatch => NewPassword.Length > 0 && NewPassword == RepeatPassword;

      /// <summary>
      /// Number of bars lit on the password strength meter, 0 to 4. Not the number of rules met but their
      /// share: only two rules may be left, and meeting both still means the meter is full. With all four
      /// default rules on, the two happen to be exactly the same.
      /// </summary>
      public int PasswordStrength {
         get {
            var shown = Policy.ShownRuleCount;
            if (shown == 0) return 0;

            var met = Policy.CountMetShownRules(NewPassword);
            return (int)Math.Round((double)PasswordRuleCount * met / shown, MidpointRounding.AwayFromZero);
         }
      }

      /// <summary>Name of the password strength currently reached, e.g. "Fair" or "Strong".</summary>
      public string PasswordStrengthCaption => PasswordStrength switch {
         1 => "Weak",
         2 => "Fair",
         3 => "Good",
         PasswordRuleCount => "Strong",
         _ => string.Empty
      };

      /// <summary>Indicates the first bar of the password strength meter is lit.</summary>
      public bool PasswordBar1 => PasswordStrength >= 1;

      /// <summary>Indicates the second bar of the password strength meter is lit.</summary>
      public bool PasswordBar2 => PasswordStrength >= 2;

      /// <summary>Indicates the third bar of the password strength meter is lit.</summary>
      public bool PasswordBar3 => PasswordStrength >= 3;

      /// <summary>Indicates the fourth bar of the password strength meter is lit.</summary>
      public bool PasswordBar4 => PasswordStrength >= PasswordRuleCount;

      #endregion

      #region Commands

      /// <summary>
      /// Saves the open row. A new row and an existing row go through the same door: the model itself knows
      /// which of the two applies.
      /// </summary>
      public async Task SaveCommand() {
         if (Data == null) return;

         try {
            WaiterText = "Saving user...";
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();

            var wasNew = Payload?.DataState == DataState.NewData;
            await Data.SaveAsync();

            // Reported only after the row really exists, and only once - which is also what turns
            // the payload from a request for a new user into one that carries a stored user.
            if (wasNew) {
               Payload!.SetNewUser(Data);
               // The screen was opened under the title of a user that did not exist yet; now that it
               // does, the entry takes the stored user's title, so opening that user again lands here.
               if (Payload.Title is { } title) NavigationEntry?.SetTitle(title);
            }
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseRecordChanged();
         }
      }

      /// <summary>May only run when there are changes and its required fields are filled in.</summary>
      public bool SaveCommandAllowed() =>
         IsNotBusy
         && Data != null
         && Data.IsDirty
         && !string.IsNullOrWhiteSpace(Data.cUserAccount)
         && !string.IsNullOrWhiteSpace(Data.cContactFullName)
         && !HasEmailError
         && !HasZipError;

      /// <summary>
      /// Returns all input to the stored values. For a new user that has never been saved, that means the form
      /// is emptied again as it was when first opened.
      /// </summary>
      public void DiscardCommand() => Data?.RollBack();

      /// <summary>May only run when there really are changes that can be discarded.</summary>
      public bool DiscardCommandAllowed() => IsNotBusy && HasUnsavedChanges;

      /// <summary>Suspends the account so it cannot be used to sign in, then saves it.</summary>
      public Task SuspendCommand() {
         Data!.cUserState = UserState.Suspended;
         return SaveCommand();
      }

      /// <summary>May only run for a row that has been saved and is not yet suspended.</summary>
      public bool SuspendCommandAllowed() =>
         IsNotBusy && IsStored && Data!.cUserState != UserState.Suspended;

      /// <summary>Copies this row's identity number to the clipboard.</summary>
      public void CopyRecordIdCommand() => Clipboard.SetText(Data!.cUserId);

      /// <summary>May only run when the row really has an identity number.</summary>
      public bool CopyRecordIdCommandAllowed() => IsStored;

      /// <summary>Chooses the contact this account represents from the contact list.</summary>
      public void PickContactCommand() {
      }

      /// <summary>
      /// Always closed for now: the contact picker dialog does not exist yet, and a contact must not be typed
      /// freely - one account must point to a contact that is really on record.
      /// </summary>
      public bool PickContactCommandAllowed() => false;

      /// <summary>
      /// Sets a new password for this account. The password is handed to the server as-is - the server hashes
      /// and stores it, because a password hash must never leave there. Both password boxes are emptied after
      /// success so the password is not left on screen.
      /// </summary>
      public async Task SetPasswordCommand() {
         if (Data == null) return;

         try {
            WaiterText = "Updating password...";
            IsBusy = InWaiting = true;
            RaiseCommandsChanged();

            var services = EmApp!.ServiceProvider.GetRequiredService<ICredentialServices>();
            await services.PostMeta_ResetPassword(Data.cUserId, NewPassword);
            ClearPassword();
         }
         catch (Exception x) {
            x.ViewExceptionDetail();
         }
         finally {
            IsBusy = InWaiting = false;
            RaiseRecordChanged();
         }
      }

      /// <summary>
      /// May only run for an account whose row already exists in the database - a password is attached to an
      /// account, so the account must have been saved first - and when both password boxes are the same and
      /// the length is enough. The remaining three rules are only shown through the password strength meter,
      /// they do not hold anything back.
      /// </summary>
      public bool SetPasswordCommandAllowed() =>
         IsNotBusy && IsStored && PasswordsMatch && PasswordMeetsPolicy;

      /// <summary>
      /// Opens and closes the display of the new password. Always allowed: what changes is only how the input
      /// is shown, not its content.
      /// </summary>
      public void TogglePasswordRevealCommand() => IsPasswordRevealed = !IsPasswordRevealed;

      #endregion

      #region Methods

      /// <summary>
      /// Opens the row carried by the navigation data. A new user request comes without a row, and the row is
      /// created here - empty and not yet in the database, but already a complete object so the screen does
      /// not need to treat a new user differently from an existing one.
      /// </summary>
      /// <param name="payload">Navigation data holding the user to edit, or a request for a new user.</param>
      public void LoadFrom(UserEditorNavigationPayload payload) {
         Payload = payload;
         Attach(payload.Data ?? User.CreateNewUser(EmApp!));

         ClearPassword();
      }

      /// <summary>
      /// Releases this screen from the row that is open. Called when its control is released by the host, so a
      /// row still alive in the user list no longer holds a screen that no longer exists.
      /// </summary>
      public void Release() => Attach(null);

      // The screen listens to the record rather than copying it, so anything that changes a value -
      // a field being typed into, a rollback, a save - reaches the parts of the screen that are
      // worked out from it. Swapping records has to unhook the old one first: a record outlives the
      // screen, and a subscription left behind would keep the whole editor alive with it.
      private void Attach(User? user) {
         if (Data is { } previous)
            previous.PropertyChanged -= RecordPropertyChanged;

         Data = user;

         if (user != null)
            user.PropertyChanged += RecordPropertyChanged;

         RaiseRecordChanged();
      }

      private void RecordPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseRecordChanged();

      // Everything the screen works out from the record is re-announced as one set. The record has
      // few enough columns that telling the screen exactly which of them moved would cost more to
      // maintain than it saves.
      private void RaiseRecordChanged() {
         NotifyChanged(nameof(HasUnsavedChanges));
         NotifyChanged(nameof(IsStored));
         NotifyChanged(nameof(SavedCaption));
         NotifyChanged(nameof(CreatedCaption));
         NotifyChanged(nameof(LastUpdatedCaption));
         NotifyChanged(nameof(HasEmailError));
         NotifyChanged(nameof(HasZipError));
         NotifyChanged(nameof(IsStateActive));
         NotifyChanged(nameof(IsStatePending));
         NotifyChanged(nameof(IsStateSuspended));
         NotifyChanged(nameof(IsStateInactive));

         RaiseCommandsChanged();
      }

      private void SetStateWhenChecked(bool isChecked, UserState state) {
         // A segmented control switches the old button off before it switches the new one on, and
         // that first half carries no information: only the button turning on names the state.
         if (isChecked && Data != null) Data.cUserState = state;
      }

      // Clearing the two values is only half the job: the boxes on screen hold their own copy, and
      // a password left revealed would carry over to whatever record is opened next.
      private void ClearPassword() {
         NewPassword = string.Empty;
         RepeatPassword = string.Empty;
         IsPasswordRevealed = false;

         PasswordBoxSyncRequested?.Invoke();
      }

      // Every rule, the meter and its caption are read off the same typed password, so they are
      // re-announced as one set.
      private void RefreshPasswordRules() {
         NotifyChanged(nameof(PasswordHasMinLength));
         NotifyChanged(nameof(PasswordHasMixedCase));
         NotifyChanged(nameof(PasswordHasDigit));
         NotifyChanged(nameof(PasswordHasSymbol));
         NotifyChanged(nameof(PasswordMeetsPolicy));
         NotifyChanged(nameof(PasswordsMatch));
         NotifyChanged(nameof(PasswordStrength));
         NotifyChanged(nameof(PasswordStrengthCaption));
         NotifyChanged(nameof(PasswordBar1));
         NotifyChanged(nameof(PasswordBar2));
         NotifyChanged(nameof(PasswordBar3));
         NotifyChanged(nameof(PasswordBar4));

         RaiseCommandsChanged();
      }

      // Every command on this screen is limited either by what is on the record or by whether a
      // save is already running, so they are re-evaluated as one set.
      private void RaiseCommandsChanged() {
         foreach (var command in Commands)
            command.RaiseCanExecuteChanged();
      }

      #endregion
   }
}
