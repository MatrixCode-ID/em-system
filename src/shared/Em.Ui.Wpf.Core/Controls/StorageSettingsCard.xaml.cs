using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Microsoft.Extensions.DependencyInjection;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;

namespace Em.Ui.Wpf.Controls;

public partial class StorageSettingsCard : UserControl
{
   private EmApp? _app;
   private bool _nuget;
   private bool _cdn, _busy, _loading, _dirty;
   private StorageSettingsDetail? _detail;
   /// <summary>Creates a new instance of <see cref="StorageSettingsCard"/>.</summary>
   public StorageSettingsCard() { InitializeComponent(); }
   /// <summary>Attaches the card to the NuGet feed settings.</summary>
   public void AttachNuGet(EmApp app) { _nuget=true; Attach(app,true); }
   private INuPakServices NuGet => _app!.ServiceProvider.GetRequiredService<INuPakServices>();
   private string SettingsClaim => _nuget ? INuPakServices.SettingsClaim : _cdn ? ICdnServices.SettingsClaim : ICtnServices.SettingsClaim;
   /// <summary>Attaches the card to the CDN or container registry settings.</summary>
   public void Attach(EmApp app, bool cdn) {
      _app = app; _cdn = cdn;
      limitPanel.Visibility = cdn ? Visibility.Visible : Visibility.Collapsed;
      Loaded += async (_, _) => {
         app.ActiveUserChanged -= OnActiveUserChanged;
         app.ActiveUserChanged += OnActiveUserChanged;
         await RefreshAsync(false);
      };
      Unloaded += (_, _) => app.ActiveUserChanged -= OnActiveUserChanged;
   }
   private void OnActiveUserChanged(object? sender, EventArgs args) {
      editor.Visibility = Visibility.Collapsed; _detail = null; paths.Text = ""; _dirty = false;
      save.IsEnabled = validate.IsEnabled = false;
      status.Text = "Session changed. Refresh settings with the current user's permissions.";
   }
   private bool CanManage => _app is not null && _app.AllClaims.Any(c => c.ModuleName == Defaults.AdministrativeToolsModuleName && c.Name == SettingsClaim)
      && new ClaimCollection(Defaults.AdministrativeToolsModuleName, _app.AllClaims, _app.ActiveUser)[SettingsClaim];
   private ICdnServices Cdn => _app!.ServiceProvider.GetRequiredService<ICdnServices>();
   private ICtnServices Registry => _app!.ServiceProvider.GetRequiredService<ICtnServices>();
   private bool Confirm(string text) => MessageBox.Show(Window.GetWindow(this), text, "Storage settings",
      MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
   /// <summary>Asks whether the screen may be left; <c>false</c> when there are unsaved changes that the user chose to keep.</summary>
   public bool ConfirmLeave() {
      if (_busy) { message.Text = "Wait for the settings request to finish before leaving."; return false; }
      if (!_dirty) return true;
      if (!Confirm("There are unsaved storage settings. Discard this draft and leave?")) return false;
      if (_detail is not null) Apply(_detail);
      return true;
   }
   private void DraftChanged(object sender, RoutedEventArgs e) {
      if (_loading || _detail is null) return;
      _dirty = true; message.Text = "Unsaved draft. Save applies only after API restart.";
   }
   private async Task RunAsync(Func<Task> action) {
      if (_busy || _app is null) return;
      _busy = true; refresh.IsEnabled = validate.IsEnabled = save.IsEnabled = false;
      directory.IsEnabled = limit.IsEnabled = enabled.IsEnabled = false;
      try { await action(); }
      catch (Exception ex) {
         status.Text = "Settings request failed. Refresh to retry/reload.";
         if (ex is ActionException denied && denied.StatusCode is 401 or 403) {
            editor.Visibility = Visibility.Collapsed; _detail = null; paths.Text = ""; _dirty = false;
         }
         message.Text = ex.Message + " Refresh to retry/reload; unsaved drafts are retained while authorized.";
      }
      finally {
         _busy = false; refresh.IsEnabled = true;
         validate.IsEnabled = save.IsEnabled = directory.IsEnabled = limit.IsEnabled = enabled.IsEnabled = _detail?.Managed == true && CanManage;
      }
   }
   /// <summary>Reads the settings again from the server, optionally discarding unsaved edits.</summary>
   public Task RefreshAsync(bool discard) => RunAsync(async () => {
      if (_dirty && !discard) return;
      var general = _nuget ? await NuGet.GetMeta_NuPakStorageStatus() : _cdn ? await Cdn.GetMeta_CdnStatus() : await Registry.GetMeta_CtnStatus();
      ShowStatus(general);
      if (!CanManage) {
         editor.Visibility = Visibility.Collapsed; _detail = null; paths.Text = ""; _dirty = false;
         message.Text = "Settings require the separate settings management claim."; return;
      }
      var detail = _nuget ? await NuGet.GetMeta_NuPakSettings() : _cdn ? await Cdn.GetMeta_CdnSettings() : await Registry.GetMeta_CtnSettings();
      Apply(detail);
   });
   private void ShowStatus(StorageFeatureStatus general) => status.Text =
      $"{(_nuget ? "NuGet" : _cdn ? "CDN" : "Registry")}: {(general.ActiveEnabled ? "active" : "disabled")} · " +
      (general.RequiresRestart ? "saved changes awaiting API restart" : "no pending changes") +
      (general.Managed ? "" : " · static host configuration");
   private void Apply(StorageSettingsDetail detail) {
      _loading = true; _detail = detail;
      try {
         enabled.IsChecked = detail.Saved.Enabled; directory.Text = detail.Saved.Directory;
         limit.Text = detail.Saved.MaxUploadMb.ToString(CultureInfo.InvariantCulture);
         paths.Text = $"Active: {detail.ActiveAbsoluteDirectory}" + (_cdn ? $" · {detail.Active.MaxUploadMb} MB upload limit" : "")
            + $"\nSaved: {detail.SavedAbsoluteDirectory}\nRevision: {detail.Revision}";
         ShowStatus(new(detail.Managed, detail.Active.Enabled, detail.Saved.Enabled, detail.RequiresRestart));
         editor.Visibility = detail.Managed ? Visibility.Visible : Visibility.Collapsed;
         message.Text = detail.Managed ? "Changes take effect after the operator restarts the API." : "This host uses static configuration; UI Save is unavailable.";
         _dirty = false;
      }
      finally { _loading = false; }
   }
   private StorageFeatureSettings Draft() {
      if (!int.TryParse(limit.Text, out var mb) || mb <= 0) throw new ArgumentException("Max upload MB must be a positive integer.");
      return new() { Enabled = enabled.IsChecked == true, Directory = directory.Text, MaxUploadMb = mb };
   }
   private async void RefreshClick(object sender, RoutedEventArgs e) {
      if (_busy || (_dirty && !Confirm("Refresh will discard your unsaved draft and reload the latest revision. Continue?"))) return;
      await RefreshAsync(true);
   }
   private async void ValidateClick(object sender, RoutedEventArgs e) => await RunAsync(async () => {
      var result = _nuget ? await NuGet.PostGetMeta_NuPakValidateDirectory(Draft()) : _cdn ? await Cdn.PostGetMeta_CdnValidateDirectory(Draft()) : await Registry.PostGetMeta_CtnValidateDirectory(Draft());
      message.Text = (result.Valid ? "Valid: " + result.AbsoluteDirectory + "\n" : "Invalid: ") + result.Message;
   });
   private async void SaveClick(object sender, RoutedEventArgs e) => await RunAsync(async () => {
      if (_detail is null) return;
      var draft = Draft();
      if (!string.Equals(draft.Directory.Trim(), _detail.Saved.Directory, StringComparison.Ordinal)
          && !Confirm("Changing the server directory does not move any data. Old files remain in their current location; after restart only the selected directory is used. Registry requires a complete manual copy. Save this change?")) return;
      var request = new StorageSettingsSave(_detail.Revision, draft);
      Apply(_nuget ? await NuGet.PostGetMeta_NuPakSettingsSave(request) : _cdn ? await Cdn.PostGetMeta_CdnSettingsSave(request) : await Registry.PostGetMeta_CtnSettingsSave(request));
   });
   private void PanelCollapsed(object sender, RoutedEventArgs e) {
      if (e.OriginalSource == panel && _busy) { panel.IsExpanded = true; return; }
      if (e.OriginalSource != panel || !_dirty) return;
      if (!Confirm("There are unsaved settings. Discard them and close the panel?")) { panel.IsExpanded = true; return; }
      if (_detail is not null) Apply(_detail);
   }
}
