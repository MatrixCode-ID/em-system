using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Core;

/// <summary>Shared named SMTP proxy that follows the current API connection.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class SmtpProfileClientService(IEmApp app, Func<ApiClient?> activeClient) : ServiceUiBase(app), ISmtpProfileService
{
   private void Prepare() => ApiClient = activeClient();
   /// <inheritdoc />
   public Task<SmtpProfileList> GetMeta_SmtpProfiles() { Prepare(); return GetAsync<SmtpProfileList>(nameof(GetMeta_SmtpProfiles)); }
   /// <inheritdoc />
   public Task<SmtpProfileDetail> PostGetMeta_SmtpProfileSave(SmtpProfileSave request) { Prepare(); return PostAsync<SmtpProfileDetail>(nameof(PostGetMeta_SmtpProfileSave), request); }
   /// <inheritdoc />
   public Task<SmtpProfileList> PostGetMeta_SmtpProfileDelete(string id, long expectedRevision) { Prepare(); return PostAsync<SmtpProfileList>(nameof(PostGetMeta_SmtpProfileDelete), id, expectedRevision); }
   /// <inheritdoc />
   public Task<SmtpProfileList> PostGetMeta_SmtpDefault(string? id, long expectedRevision) { Prepare(); return PostAsync<SmtpProfileList>(nameof(PostGetMeta_SmtpDefault), id!, expectedRevision); }
   /// <inheritdoc />
   public Task<SmtpConnectionResult> PostGetMeta_SmtpProfileTestConnection(string id) { Prepare(); return PostAsync<SmtpConnectionResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpProfileTestConnection), id); }
   /// <inheritdoc />
   public Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmail(string id, string recipient) { Prepare(); return PostAsync<SmtpSendResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpProfileTestEmail), id, recipient); }
   /// <inheritdoc />
   public Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmailFrom(string id, string sender, string recipient) { Prepare(); return PostAsync<SmtpSendResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpProfileTestEmailFrom), id, sender, recipient); }
   /// <inheritdoc />
   public Task<SmtpSendResult> PostGetMeta_SmtpSendByName(string? name, SmtpMessage message) { Prepare(); return PostAsync<SmtpSendResult>(TimeSpan.FromSeconds(150), nameof(PostGetMeta_SmtpSendByName), name!, message); }
   /// <inheritdoc />
   public Task<SmtpSendResult> SendByNameAsync(string? name, SmtpMessage message) => PostGetMeta_SmtpSendByName(name, message);
}
