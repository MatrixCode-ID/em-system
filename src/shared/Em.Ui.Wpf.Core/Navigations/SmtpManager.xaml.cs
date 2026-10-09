using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.ObjectModel;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

/// <summary>SMTP settings and diagnostics using shared manager styles and a test toolbar.</summary>
public partial class SmtpManager : UserControl, INavigationBody
{
   private bool _historyInitialized;
   /// <summary>Creates the SMTP Manager.</summary>
   public SmtpManager(EmApp app) {
      InitializeComponent();
      Vm.EmApp = app;
      Vm.PasswordReset += (_, _) => passwordBox.Clear();
      Vm.ConfirmDelete = profile => (Window.GetWindow(this) ?? app.MainWindow).ShowMboxDecideWarning(
         $"Delete SMTP '{profile.Name}'?" + (profile.IsDefault ? " Sending without an SMTP name will fail until a default is selected." : ""), "Delete SMTP") == MessageBoxResult.Yes;
   }
   /// <summary>Screen view model.</summary>
   public SmtpManagerVm Vm => (SmtpManagerVm)DataContext;
   private void OnPasswordChanged(object sender, RoutedEventArgs args) => Vm.NewPassword = ((PasswordBox)sender).Password;
   private void OnRowDoubleClick(object sender, MouseButtonEventArgs args) {
      if (args.ChangedButton != MouseButton.Left || !Vm.CanBrowse ||
          sender is not ListBoxItem { DataContext: SmtpProfileDetail profile }) return;
      Vm.SelectedProfile = profile;
      Vm.EditCommand();
      args.Handled = true;
   }
   private void OnPreviewKeyDown(object sender, KeyEventArgs args) {
      if (args.Key == Key.Escape && Vm.IsEditorOpen && Vm.IsNotBusy) { Vm.CancelCommand(); args.Handled = true; }
   }
   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) {
      if (!_historyInitialized && Vm.EmApp is { } app) {
         Vm.InitializeEmailHistory(new SmtpEmailHistory(app)); _historyInitialized = true;
      }
      return Vm.IsLoaded ? Task.CompletedTask : Vm.ReloadAsync();
   }
   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();
   /// <inheritdoc />
   public Task OnRelease(INavigation sender) { Vm.CancelCommand(); Vm.ResetPassword(); return Task.CompletedTask; }
}

/// <summary>Security mode with its UI caption.</summary>
public sealed record SmtpSecurityOption(SmtpSecurity Value, string Caption);

/// <summary>SMTP Manager state and commands.</summary>
public sealed class SmtpManagerVm : MvvmModelBase
{
   private readonly ISmtpService? _service;
   private readonly ISmtpProfileService? _profiles;
   private SmtpEmailHistory? _emailHistory;
   private string? _editId;
   private long _editorRevision;
   /// <summary>Creates a view model for navigation.</summary>
   public SmtpManagerVm() { RegisterCommands(); }
   /// <summary>Creates a view model with an explicit service, for composition by other UI hosts.</summary>
   public SmtpManagerVm(ISmtpService service) { _service = service; RegisterCommands(); }
   /// <summary>Creates a named-profile manager with an explicit service.</summary>
   public SmtpManagerVm(ISmtpProfileService service) { _profiles = service; RegisterCommands(); }
   private ISmtpService Service => _service ?? EmApp!.ServiceProvider.GetRequiredService<ISmtpService>();
   private ISmtpProfileService? ProfileService => _service is not null ? null : _profiles ?? EmApp?.ServiceProvider.GetRequiredService<ISmtpProfileService>();
   private void RegisterCommands() {
      Settings = new();
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, () => IsNotBusy);
      RegisterCommand(nameof(SaveCommand), SaveCommand, () => CanEdit && (_service is not null || IsEditorOpen));
      RegisterCommand(nameof(TestConnectionCommand), TestConnectionCommand, () => _service is not null ? CanEdit : CanAct);
      RegisterCommand(nameof(SendTestCommand), SendTestCommand, () => CanSendTest);
      RegisterCommand(nameof(ClearHistoryCommand), ClearHistoryCommand, () => CanBrowse && (FromHistory.Count > 0 || ToHistory.Count > 0));
      RegisterCommand(nameof(NewCommand), NewCommand, () => CanBrowse);
      RegisterCommand(nameof(EditCommand), EditCommand, () => CanAct);
      RegisterCommand(nameof(CancelCommand), CancelCommand, () => IsNotBusy);
      RegisterCommand(nameof(SetDefaultCommand), SetDefaultCommand, () => CanAct && SelectedProfile?.IsDefault == false);
      RegisterCommand(nameof(ClearDefaultCommand), ClearDefaultCommand, () => CanBrowse && Profiles.Any(x => x.IsDefault));
      RegisterCommand(nameof(DeleteCommand), DeleteCommand, () => CanAct);
   }
   /// <summary>Saved profiles without credentials.</summary>
   public ObservableCollection<SmtpProfileDetail> Profiles { get; } = [];
   /// <summary>Selected table row.</summary>
   public SmtpProfileDetail? SelectedProfile { get => Get<SmtpProfileDetail>(); set { Set(value); NotifyChanged(nameof(CanAct)); RaiseCommandsChanged(); } }
   /// <summary>Whether the list and new action can be used.</summary>
   public bool CanBrowse => IsLoaded && IsNotBusy && !IsEditorOpen;
   /// <summary>Whether an action can use the selected saved row.</summary>
   public bool CanAct => CanBrowse && SelectedProfile is not null;
   /// <summary>Shows the profile editor.</summary>
   public bool IsEditorOpen { get => Get<bool>(); private set { Set(value); UpdateState(); } }
   /// <summary>Editor name used for named sending.</summary>
   public string ProfileName { get => Get<string>() ?? ""; set => Set(value); }
   /// <summary>Optional profile note.</summary>
   public string? ProfileNote { get => Get<string>(); set => Set(value); }
   /// <summary>Explains current default selection.</summary>
   public string DefaultStatus => Profiles.FirstOrDefault(x => x.IsDefault) is { } p
      ? $"Default SMTP: {p.Name}" : "No default SMTP. Sending without an SMTP name will fail.";
   /// <summary>Host-supplied confirmation for deletion.</summary>
   public Func<SmtpProfileDetail, bool>? ConfirmDelete { get; set; }
   /// <summary>Creates a draft without changing the saved default.</summary>
   public void NewCommand() {
      if (!CanBrowse) return;
      _editId = null; _editorRevision = Revision; ProfileName = ""; ProfileNote = null;
      Apply(new() { Revision = Revision }); IsEditorOpen = true;
   }
   /// <summary>Copies the selected saved row to a separate draft.</summary>
   public void EditCommand() {
      if (!CanAct || SelectedProfile is not { } p) return;
      _editId = p.Id; _editorRevision = Revision; ProfileName = p.Name; ProfileNote = p.Note;
      Apply(new() { Settings = JsonSerializer.Deserialize<SmtpSettings>(JsonSerializer.Serialize(p.Settings))!, Revision = Revision, HasPassword = p.HasPassword });
      IsEditorOpen = true;
   }
   /// <summary>Discards the draft and clears its password.</summary>
   public void CancelCommand() { if (IsBusy) return; IsEditorOpen = false; ResetPassword(); ClearPassword = false; }
   /// <summary>Selects the saved row as default.</summary>
   public Task SetDefaultCommand() => RunAsync("Selecting default SMTP...", async () => {
      if (SelectedProfile is not { } p || ProfileService is not { } service) return;
      ApplyList(await service.PostGetMeta_SmtpDefault(p.Id, Revision), p.Id);
   });
   /// <summary>Clears the default without deleting any configuration.</summary>
   public Task ClearDefaultCommand() => RunAsync("Clearing default SMTP...", async () => {
      if (ProfileService is { } service) ApplyList(await service.PostGetMeta_SmtpDefault(null, Revision), SelectedProfile?.Id);
   });
   /// <summary>Deletes the selected row after host confirmation.</summary>
   public Task DeleteCommand() {
      if (!CanAct || SelectedProfile is not { } p || ConfirmDelete?.Invoke(p) != true) return Task.CompletedTask;
      return RunAsync("Deleting SMTP...", async () => { if (ProfileService is { } service) ApplyList(await service.PostGetMeta_SmtpProfileDelete(p.Id, Revision)); });
   }
   /// <summary>Raised after load/save/release so the view clears its PasswordBox.</summary>
   public event EventHandler? PasswordReset;
   /// <summary>Editable settings, replaced after a reload or save.</summary>
   public SmtpSettings Settings { get => Get<SmtpSettings>() ?? new(); private set => Set(value); }
   /// <summary>Authentication toggle, with notification for the credentials group's enabled state.</summary>
   public bool Authenticate {
      get => Settings.Authenticate;
      set { if (Settings.Authenticate == value) return; Settings.Authenticate = value; NotifyChanged(nameof(Authenticate)); }
   }
   /// <summary>Available security choices.</summary>
   public SmtpSecurityOption[] SecurityModes { get; } = [new(SmtpSecurity.StartTls, "STARTTLS (required)"), new(SmtpSecurity.SslOnConnect, "TLS on connect"), new(SmtpSecurity.None, "None (relay only)")];
   /// <summary>True after a successful settings read.</summary>
   public bool IsLoaded { get => Get<bool>(); private set => Set(value); }
   /// <summary>Whether controls can currently be edited.</summary>
   public bool CanEdit => IsLoaded && IsNotBusy;
   /// <summary>Current persisted revision.</summary>
   public long Revision { get; private set; }
   /// <summary>Whether the server stores a password.</summary>
   public bool HasPassword { get; private set; }
   /// <summary>Explains empty password behavior.</summary>
   public string PasswordHint => HasPassword ? "A password is stored. Leave the new password blank to keep it." : "No password is stored. Enter one if authentication is enabled.";
   /// <summary>New secret, held only until save or reload.</summary>
   public string? NewPassword { get; set; }
   /// <summary>Explicitly remove the stored password on save.</summary>
   public bool ClearPassword { get => Get<bool>(); set => Set(value); }
   /// <summary>Saved profile selected independently for the test toolbar.</summary>
   public SmtpProfileDetail? TestProfile { get => Get<SmtpProfileDetail>(); set { Set(value); TestStatus = ""; RaiseCommandsChanged(); } }
   /// <summary>Whether a diagnostic message can be sent from the toolbar.</summary>
   public bool CanSendTest => CanBrowse && !string.IsNullOrWhiteSpace(TestRecipient) &&
      (_service is not null || (TestProfile is not null && Profiles.Contains(TestProfile) && !string.IsNullOrWhiteSpace(TestSender)));
   /// <summary>Diagnostic recipient.</summary>
   public string TestRecipient { get => Get<string>() ?? ""; set => Set(value, _ => RaiseCommandsChanged()); }
   /// <summary>Sender used only for the diagnostic email, never persisted on the profile.</summary>
   public string TestSender { get => Get<string>() ?? ""; set => Set(value, _ => RaiseCommandsChanged()); }
   /// <summary>Previously used test sender addresses, newest first.</summary>
   public ObservableCollection<string> FromHistory { get; } = [];
   /// <summary>Previously used test recipient addresses, newest first.</summary>
   public ObservableCollection<string> ToHistory { get; } = [];
   /// <summary>Loads history from the host's Registry storage.</summary>
   public void InitializeEmailHistory(SmtpEmailHistory history) {
      _emailHistory = history;
      TryHistory(LoadEmailHistory, "Email history could not be loaded.");
   }
   /// <summary>Clears persisted history while retaining the current test inputs.</summary>
   public void ClearHistoryCommand() {
      if (!CanBrowse) return;
      TryHistory(() => {
         _emailHistory?.Clear();
         ReplaceHistory([], []);
         TestStatus = "Email history cleared.";
      }, "Email history could not be cleared.");
   }
   /// <summary>Result shown beneath the settings.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }
   /// <summary>Result shown beneath the test toolbar.</summary>
   public string TestStatus { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Reads current persisted settings.</summary>
   public Task ReloadAsync() => RunAsync("Loading SMTP settings...", async () => {
      if (ProfileService is { } profiles) {
         ApplyList(await profiles.GetMeta_SmtpProfiles(), SelectedProfile?.Id); IsEditorOpen = false; ResetPassword();
      } else Apply(await Service.GetMeta_SmtpSettings());
      Status = "Settings loaded.";
   });
   /// <summary>Reload command.</summary>
   public Task RefreshCommand() => ReloadAsync();
   /// <summary>Saves without returning the password to the client.</summary>
   public Task SaveCommand() => RunAsync("Saving SMTP settings...", async () => {
      if (ProfileService is { } profiles) {
         var saved = await profiles.PostGetMeta_SmtpProfileSave(new() { Id = _editId, Name = ProfileName, Note = ProfileNote,
            Settings = Settings, ExpectedRevision = _editorRevision, Password = string.IsNullOrEmpty(NewPassword) ? null : NewPassword, ClearPassword = ClearPassword });
         ResetPassword(); ClearPassword = false; IsEditorOpen = false;
         ApplyList(await profiles.GetMeta_SmtpProfiles(), saved.Id);
      } else Apply(await Service.PostGetMeta_SmtpSettingsSave(new() { Settings = Settings, ExpectedRevision = Revision,
         Password = string.IsNullOrEmpty(NewPassword) ? null : NewPassword, ClearPassword = ClearPassword }));
      Status = "SMTP settings saved. Changes apply immediately.";
   });
   /// <summary>Tests saved settings, including authentication.</summary>
   public Task TestConnectionCommand() => RunAsync("Testing SMTP connection...", async () => {
      var result = ProfileService is { } profiles && SelectedProfile is { } p
         ? await profiles.PostGetMeta_SmtpProfileTestConnection(p.Id) : await Service.PostGetMeta_SmtpTestConnection();
      Status = $"Connection succeeded ({(result.IsSecure ? "TLS" : "plaintext relay")}) at {result.CheckedAtUtc.ToLocalTime():g}.";
   });
   /// <summary>Sends one diagnostic message.</summary>
   public Task SendTestCommand() {
      if (!CanSendTest) return Task.CompletedTask;
      return RunAsync("Sending test email...", async () => {
         TestStatus = "";
         var from = TestSender.Trim(); var to = TestRecipient.Trim();
         var result = ProfileService is { } profiles && TestProfile is { } profile
            ? await profiles.PostGetMeta_SmtpProfileTestEmailFrom(profile.Id, from, to)
            : await Service.PostGetMeta_SmtpTestEmail(to);
         TestStatus = $"SMTP server accepted the test email at {result.AcceptedAtUtc.ToLocalTime():g}.";
         if (_emailHistory is not null) TryHistory(() => {
            _emailHistory.Remember(from, to); LoadEmailHistory();
         }, TestStatus + " Email history could not be saved.");
      }, true);
   }

   private void LoadEmailHistory() {
      if (_emailHistory is null) return;
      var history = _emailHistory.Read(); ReplaceHistory(history.From, history.To);
   }
   private void ReplaceHistory(string[] from, string[] to) {
      var sender = TestSender; var recipient = TestRecipient;
      FromHistory.Clear(); foreach (var address in from) FromHistory.Add(address);
      ToHistory.Clear(); foreach (var address in to) ToHistory.Add(address);
      TestSender = sender; TestRecipient = recipient; RaiseCommandsChanged();
   }
   private void TryHistory(Action action, string failure) {
      try { action(); }
      catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException) {
         TestStatus = failure;
      }
   }

   private void Apply(SmtpSettingsDetail detail) {
      Settings = detail.Settings; Revision = detail.Revision; HasPassword = detail.HasPassword;
      NotifyChanged(nameof(Authenticate));
      IsLoaded = true; ClearPassword = false; ResetPassword(); NotifyChanged(nameof(PasswordHint));
   }
   private void ApplyList(SmtpProfileList list, string? selectedId = null) {
      var testId = TestProfile?.Id;
      Profiles.Clear(); foreach (var p in list.Profiles) Profiles.Add(p);
      TestProfile = Profiles.FirstOrDefault(x => x.Id == testId);
      Revision = list.Revision; SelectedProfile = Profiles.FirstOrDefault(x => x.Id == selectedId);
      IsLoaded = true; NotifyChanged(nameof(DefaultStatus)); UpdateState();
   }
   /// <summary>Clears the draft password from the view model and its view.</summary>
   public void ResetPassword() { NewPassword = null; PasswordReset?.Invoke(this, EventArgs.Empty); }
   private async Task RunAsync(string caption, Func<Task> action, bool email = false) {
      if (IsBusy || (_service is null && _profiles is null && EmApp is null)) return;
      try {
         IsBusy = InWaiting = true; WaiterText = caption; UpdateState();
         await action();
      }
      catch (ActionException ex) {
         var message = ex.Message.Replace("Server Error: ", "");
         if (email) TestStatus = message; else Status = message;
      }
      catch (Exception ex) {
         if (email) TestStatus = "The test did not complete. Check before sending again.";
         else Status = "The operation did not complete. Try reloading the settings.";
         if (EmApp is not null) AlertError(ex);
      }
      finally { InWaiting = IsBusy = false; UpdateState(); }
   }
   private void UpdateState() { NotifyChanged(nameof(CanEdit)); NotifyChanged(nameof(CanBrowse)); NotifyChanged(nameof(CanAct)); NotifyChanged(nameof(CanSendTest)); RaiseCommandsChanged(); }
   private void RaiseCommandsChanged() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
}
