using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;

namespace Em.Ui.Wpf.Dialogs
{
   /// <summary>
   /// Debug only: the dialog behind Tools > Switch User. Lists the debugger account, the built-in
   /// administrator and every stored user, and makes the one picked the active user without its password.
   /// </summary>
   public partial class SwitchUserDialog : EmWindow
   {
      /// <summary>Creates the dialog and starts loading the accounts.</summary>
      /// <param name="app">The application whose active user is switched.</param>
      public SwitchUserDialog(EmApp app) {
         InitializeComponent();
         Vm.EmApp = app;
         Vm.MainWindow = this;
         Vm.CloseRequested += () => DialogResult = true;
         Closed += (_, _) => Vm.Release();
         _ = Vm.LoadAsync();
      }

      /// <summary>The view model of this dialog.</summary>
      public SwitchUserDialogVm Vm => (SwitchUserDialogVm)DataContext;
   }

   /// <summary>View model of <see cref="SwitchUserDialog"/>.</summary>
   public class SwitchUserDialogVm : MvvmModelBase
   {
      // The list is read through a client of its own that carries the debug token but no user
      // identity, so the server answers as the debugger: the account acted as right now may well be
      // one that is not allowed to read the user list.
      private ApiClient? _client;
      private int _loadEpoch;
      private IReadOnlyList<SwitchUserRowVm> _rows = [];

      /// <summary>Creates the view model and registers its commands.</summary>
      public SwitchUserDialogVm() {
         RegisterCommand(nameof(SwitchCommand), SwitchCommand, SwitchCommandAllowed);
         RegisterCommand(nameof(RefreshCommand), RefreshCommand, RefreshCommandAllowed);
      }

      /// <summary>Asks the dialog to close after a successful switch.</summary>
      public event Action? CloseRequested;

      /// <summary>The account name of the user acting now, for the chip in the banner.</summary>
      public string CurrentAccount => EmApp?.ActiveUser?.cUserAccount ?? "-";

      /// <summary>Text typed in the search box; matches account, full name and e-mail.</summary>
      public string Filter {
         get => Get(string.Empty);
         set => Set(value, _ => RefreshRows());
      }

      /// <summary>The rows that match <see cref="Filter"/>, system accounts first.</summary>
      public IReadOnlyList<SwitchUserRowVm> VisibleRows {
         get => Get<IReadOnlyList<SwitchUserRowVm>>([]);
         private set => Set(value);
      }

      /// <summary>The row picked in the list.</summary>
      public SwitchUserRowVm? SelectedRow {
         get => Get<SwitchUserRowVm?>();
         set => Set(value, _ => RaiseCommands());
      }

      /// <summary>Whether the account list is being loaded.</summary>
      public bool IsLoading {
         get => Get<bool>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(IsEmpty));
            RaiseCommands();
         });
      }

      /// <summary>Why the account list could not be loaded, or <c>null</c>.</summary>
      public string? Error {
         get => Get<string?>();
         private set => Set(value, _ => {
            NotifyChanged(nameof(HasError));
            NotifyChanged(nameof(IsEmpty));
         });
      }

      /// <summary>Whether <see cref="Error"/> has something to say.</summary>
      public bool HasError => !string.IsNullOrWhiteSpace(Error);

      /// <summary>Whether the list loaded but nothing matches the search.</summary>
      public bool IsEmpty => !IsLoading && !HasError && VisibleRows.Count == 0;

      /// <summary>
      /// Loads every account. The two system accounts are always there, even when the server cannot be
      /// reached; stored users follow once the server answers.
      /// </summary>
      public async Task LoadAsync() {
         if (EmApp is not { } app) return;

         var epoch = ++_loadEpoch;
         Error = null;
         IsLoading = true;
         var system = new[] {
            new SwitchUserRowVm(EmApp.CreateDebuggerUser(app), isSystem: true, app.ActiveUser),
            new SwitchUserRowVm(EmApp.CreateAdminUser(app), isSystem: true, app.ActiveUser)
         };

         try {
            if (app.ActiveConnection is not { } connection) {
               _rows = system;
               Error = "Pick a debug connection first.";
               return;
            }

            _client?.Dispose();
            _client = connection.CreateApiClient();
            var users = await _client.GetAsync<vi_User[]>(
               Defaults.CredentialModuleName, nameof(ICredentialServices.GetVi_Users));
            if (epoch != _loadEpoch) return;

            _rows = [
               .. system,
               .. users
                  .Where(u => u.cUserId is not (Defaults.DebuggerUserId or Defaults.AdminUserId))
                  .OrderBy(u => u.cUserAccount, StringComparer.CurrentCultureIgnoreCase)
                  .Select(u => new SwitchUserRowVm(User.Build(app, u), isSystem: false, app.ActiveUser))
            ];
         }
         catch (Exception x) {
            if (epoch != _loadEpoch) return;
            _rows = system;
            Error = $"The users could not be loaded: {x.Message}";
         }
         finally {
            if (epoch == _loadEpoch) {
               IsLoading = false;
               RefreshRows();
               NotifyChanged(nameof(CurrentAccount));
            }
         }
      }

      /// <summary>Reloads the account list.</summary>
      public Task RefreshCommand() => LoadAsync();

      /// <summary>May run while nothing is loading or switching.</summary>
      public bool RefreshCommandAllowed() => !IsLoading && IsNotBusy;

      /// <summary>
      /// Acts as the picked account. Closes the dialog when the switch happened, stays open without a word when
      /// a screen with unsaved changes refused to close, and shows the server's refusal otherwise.
      /// </summary>
      public async Task SwitchCommand() {
         if (EmApp is not { } app || SelectedRow is not { IsSelectable: true, IsCurrent: false } row) return;

         try {
            IsBusy = true;
            RaiseCommands();
            if (await app.SwitchDebugUserAsync(row.User)) CloseRequested?.Invoke();
         }
         catch (Exception x) {
            AlertError(x);
            // The debugger may have been restored, so the CURRENT chip moves with it.
            await LoadAsync();
         }
         finally {
            IsBusy = false;
            RaiseCommands();
         }
      }

      /// <summary>May run for a selectable account that is not the current one.</summary>
      public bool SwitchCommandAllowed() =>
         IsNotBusy && !IsLoading && SelectedRow is { IsSelectable: true, IsCurrent: false };

      /// <summary>Releases the private API client.</summary>
      public void Release() {
         ++_loadEpoch;
         _client?.Dispose();
         _client = null;
      }

      private void RefreshRows() {
         var filter = Filter.Trim();
         VisibleRows = filter.Length == 0 ? _rows : [.. _rows.Where(r => r.Matches(filter))];
         if (SelectedRow is { } selected && !VisibleRows.Contains(selected)) SelectedRow = null;
         NotifyChanged(nameof(IsEmpty));
      }

      private void RaiseCommands() {
         Commands[nameof(SwitchCommand)]?.RaiseCanExecuteChanged();
         Commands[nameof(RefreshCommand)]?.RaiseCanExecuteChanged();
      }
   }

   /// <summary>One account in <see cref="SwitchUserDialog"/>.</summary>
   public class SwitchUserRowVm
   {
      /// <summary>Creates a row for <paramref name="user"/>.</summary>
      /// <param name="user">The account.</param>
      /// <param name="isSystem">Whether it is the debugger or the built-in administrator.</param>
      /// <param name="activeUser">The user acting now, to mark the current row.</param>
      public SwitchUserRowVm(User user, bool isSystem, User? activeUser) {
         User = user;
         IsSystem = isSystem;
         IsCurrent = activeUser?.cUserId == user.cUserId;
      }

      /// <summary>The account this row stands for.</summary>
      public User User { get; }

      /// <summary>The account name.</summary>
      public string Account => User.cUserAccount;

      /// <summary>The full name of the contact behind the account.</summary>
      public string FullName => User.cContactFullName;

      /// <summary>The account state as text.</summary>
      public string StateCaption => User.cUserState.ToString();

      /// <summary>Whether the account is an administrator or a plain user.</summary>
      public string RoleCaption => User.cUserIsAdmin ? "Admin" : "User";

      /// <summary>Whether this is one of the two system accounts.</summary>
      public bool IsSystem { get; }

      /// <summary>Whether this is the account acting now.</summary>
      public bool IsCurrent { get; }

      /// <summary>
      /// Whether the server would act as this account: not for a suspended, pending or deleted account.
      /// </summary>
      public bool IsSelectable => User.cUserState is not (UserState.Suspended or UserState.Pending or UserState.Deleted);

      /// <summary>Why the row cannot be picked, or <c>null</c> when it can.</summary>
      public string? DisabledReason =>
         IsSelectable ? null : $"The server refuses to act as a {User.cUserState.ToString().ToLowerInvariant()} account.";

      /// <summary>Whether <see cref="DisabledReason"/> has something to say.</summary>
      public bool HasDisabledReason => !IsSelectable;

      /// <summary>Whether the row matches the search text.</summary>
      public bool Matches(string filter) =>
         Contains(Account, filter) || Contains(FullName, filter) || Contains(User.cCommValue, filter);

      private static bool Contains(string? value, string filter) =>
         value?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true;
   }
}
