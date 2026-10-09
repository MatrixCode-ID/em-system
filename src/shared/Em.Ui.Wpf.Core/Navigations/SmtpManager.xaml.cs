using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Navigations;

/// <summary>SMTP settings and diagnostics using the shared manager styles and side sheet.</summary>
public partial class SmtpManager : UserControl, INavigationBody
{
   /// <summary>Creates the SMTP Manager.</summary>
   public SmtpManager(EmApp app) {
      InitializeComponent();
      Vm.EmApp = app;
      Vm.PasswordReset += (_, _) => passwordBox.Clear();
      Vm.PropertyChanged += (_, args) => {
         if (args.PropertyName == nameof(Vm.IsTestOpen) && Vm.IsTestOpen)
            Dispatcher.BeginInvoke(() => testRecipient.Focus());
      };
   }
   /// <summary>Screen view model.</summary>
   public SmtpManagerVm Vm => (SmtpManagerVm)DataContext;
   private void OnPasswordChanged(object sender, RoutedEventArgs args) => Vm.NewPassword = ((PasswordBox)sender).Password;
   private void OnPreviewKeyDown(object sender, KeyEventArgs args) {
      if (args.Key == Key.Escape && Vm.IsTestOpen && Vm.IsNotBusy) { Vm.IsTestOpen = false; testToggle.Focus(); args.Handled = true; }
   }
   /// <inheritdoc />
   public Task OnNavigatingIn(INavigation sender, NavigatingEventArgs args) => Vm.IsLoaded ? Task.CompletedTask : Vm.ReloadAsync();
   /// <inheritdoc />
   public Task OnNavigatingAway(INavigation sender, NavigatingEventArgs args) => Task.CompletedTask;
   /// <inheritdoc />
   public Task OnReloadRequested(INavigation sender, NavigationEventArgs args) => Vm.ReloadAsync();
   /// <inheritdoc />
   public Task OnRelease(INavigation sender) { Vm.ResetPassword(); return Task.CompletedTask; }
}

/// <summary>Security mode with its UI caption.</summary>
public sealed record SmtpSecurityOption(SmtpSecurity Value, string Caption);

/// <summary>SMTP Manager state and commands.</summary>
public sealed class SmtpManagerVm : MvvmModelBase
{
   private readonly ISmtpService? _service;
   /// <summary>Creates a view model for navigation.</summary>
   public SmtpManagerVm() { RegisterCommands(); }
   /// <summary>Creates a view model with an explicit service, for composition by other UI hosts.</summary>
   public SmtpManagerVm(ISmtpService service) { _service = service; RegisterCommands(); }
   private ISmtpService Service => _service ?? EmApp!.ServiceProvider.GetRequiredService<ISmtpService>();
   private void RegisterCommands() {
      Settings = new();
      RegisterCommand(nameof(RefreshCommand), RefreshCommand, () => IsNotBusy);
      RegisterCommand(nameof(SaveCommand), SaveCommand, () => CanEdit);
      RegisterCommand(nameof(TestConnectionCommand), TestConnectionCommand, () => CanEdit);
      RegisterCommand(nameof(SendTestCommand), SendTestCommand, () => CanEdit && !string.IsNullOrWhiteSpace(TestRecipient));
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
   /// <summary>Opens the test email side sheet.</summary>
   public bool IsTestOpen { get => Get<bool>(); set => Set(value); }
   /// <summary>Diagnostic recipient.</summary>
   public string TestRecipient { get => Get<string>() ?? ""; set => Set(value, _ => RaiseCommandsChanged()); }
   /// <summary>Result shown beneath the settings.</summary>
   public string Status { get => Get<string>() ?? ""; private set => Set(value); }
   /// <summary>Result shown inside the test sheet.</summary>
   public string TestStatus { get => Get<string>() ?? ""; private set => Set(value); }

   /// <summary>Reads current persisted settings.</summary>
   public Task ReloadAsync() => RunAsync("Loading SMTP settings...", async () => { Apply(await Service.GetMeta_SmtpSettings()); Status = "Settings loaded."; });
   /// <summary>Reload command.</summary>
   public Task RefreshCommand() => ReloadAsync();
   /// <summary>Saves without returning the password to the client.</summary>
   public Task SaveCommand() => RunAsync("Saving SMTP settings...", async () => {
      Apply(await Service.PostGetMeta_SmtpSettingsSave(new() { Settings = Settings, ExpectedRevision = Revision,
         Password = string.IsNullOrEmpty(NewPassword) ? null : NewPassword, ClearPassword = ClearPassword }));
      Status = "SMTP settings saved. Changes apply immediately.";
   });
   /// <summary>Tests saved settings, including authentication.</summary>
   public Task TestConnectionCommand() => RunAsync("Testing SMTP connection...", async () => {
      var result = await Service.PostGetMeta_SmtpTestConnection();
      Status = $"Connection succeeded ({(result.IsSecure ? "TLS" : "plaintext relay")}) at {result.CheckedAtUtc.ToLocalTime():g}.";
   });
   /// <summary>Sends one diagnostic message.</summary>
   public Task SendTestCommand() => RunAsync("Sending test email...", async () => {
      TestStatus = "";
      var result = await Service.PostGetMeta_SmtpTestEmail(TestRecipient.Trim());
      TestStatus = $"SMTP server accepted the test email at {result.AcceptedAtUtc.ToLocalTime():g}.";
   }, true);

   private void Apply(SmtpSettingsDetail detail) {
      Settings = detail.Settings; Revision = detail.Revision; HasPassword = detail.HasPassword;
      NotifyChanged(nameof(Authenticate));
      IsLoaded = true; ClearPassword = false; ResetPassword(); NotifyChanged(nameof(PasswordHint));
   }
   /// <summary>Clears the draft password from the view model and its view.</summary>
   public void ResetPassword() { NewPassword = null; PasswordReset?.Invoke(this, EventArgs.Empty); }
   private async Task RunAsync(string caption, Func<Task> action, bool email = false) {
      if (IsBusy || (_service is null && EmApp is null)) return;
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
   private void UpdateState() { NotifyChanged(nameof(CanEdit)); RaiseCommandsChanged(); }
   private void RaiseCommandsChanged() { foreach (var command in Commands) command.RaiseCanExecuteChanged(); }
}
