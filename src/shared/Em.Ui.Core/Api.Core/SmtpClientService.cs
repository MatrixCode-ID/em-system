using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core;

/// <summary>Shared SMTP proxy for WPF, MAUI and other UI hosts. Resolves the active client on every call.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class SmtpClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ISmtpService
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<SmtpSettingsDetail> GetMeta_SmtpSettings() {
      Prepare(); return GetAsync<SmtpSettingsDetail>(nameof(GetMeta_SmtpSettings));
   }
   /// <inheritdoc />
   public Task<SmtpSettingsDetail> PostGetMeta_SmtpSettingsSave(SmtpSettingsSave request) {
      Prepare(); return PostAsync<SmtpSettingsDetail>(nameof(PostGetMeta_SmtpSettingsSave), request);
   }
   /// <inheritdoc />
   public Task<SmtpConnectionResult> PostGetMeta_SmtpTestConnection() {
      Prepare(); return PostAsync<SmtpConnectionResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpTestConnection));
   }
   /// <inheritdoc />
   public Task<SmtpSendResult> PostGetMeta_SmtpTestEmail(string recipient) {
      Prepare(); return PostAsync<SmtpSendResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpTestEmail), recipient);
   }
   /// <inheritdoc />
   public Task<SmtpSendResult> PostGetMeta_SmtpSend(SmtpMessage message) {
      Prepare(); return PostAsync<SmtpSendResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpSend), message);
   }
   /// <inheritdoc />
   public Task<SmtpSendResult> SendAsync(SmtpMessage message) => PostGetMeta_SmtpSend(message);
}
