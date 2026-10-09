using Em.Api.Core.Models;
using Em.Api.Core.Smtp;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>Additional named-profile actions without changing the legacy SMTP service.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class SmtpProfileService : ServicesBase, ISmtpProfileService
{
   private readonly ISmtpProfileStore _profiles;
   private readonly SmtpService _sender;
   /// <summary>Creates profile actions in the request or background scope.</summary>
   public SmtpProfileService(IDbContextFactory<ApiCoreContext> factory) {
      var store = new SmtpSettingsStore(factory); _profiles = store;
      _sender = new SmtpService(store, new SmtpTransport());
   }
   internal SmtpProfileService(ISmtpProfileStore profiles, SmtpService sender) { _profiles = profiles; _sender = sender; }
   /// <inheritdoc />
   [GetAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpProfileList> GetMeta_SmtpProfiles() => _profiles.ProfilesAsync(AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpProfileDetail> PostGetMeta_SmtpProfileSave(SmtpProfileSave request) => _profiles.SaveProfileAsync(request, AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpProfileList> PostGetMeta_SmtpProfileDelete(string id, long expectedRevision) => _profiles.DeleteAsync(id, expectedRevision, AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpProfileList> PostGetMeta_SmtpDefault(string? id, long expectedRevision) => _profiles.DefaultAsync(id, expectedRevision, AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public async Task<SmtpConnectionResult> PostGetMeta_SmtpProfileTestConnection(string id) {
      var (settings, password) = await _profiles.SelectedCredentialsAsync(id, true, AbortToken);
      return await _sender.TestSavedAsync(settings, password, AbortToken);
   }
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public async Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmail(string id, string recipient) {
      var (settings, password) = await _profiles.SelectedCredentialsAsync(id, true, AbortToken);
      return await _sender.SendSavedAsync(settings, password, new() { To = [recipient], Subject = "SMTP test email",
         TextBody = "This test email was sent by the Em SMTP Manager." }, AbortToken);
   }
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public async Task<SmtpSendResult> PostGetMeta_SmtpProfileTestEmailFrom(string id, string sender, string recipient) {
      var (settings, password) = await _profiles.SelectedCredentialsAsync(id, true, AbortToken);
      return await _sender.SendSavedAsync(settings, password, new() { FromAddress = sender, To = [recipient],
         Subject = "SMTP test email", TextBody = "This test email was sent by the Em SMTP Manager." }, AbortToken);
   }
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.SendClaim)]
   public Task<SmtpSendResult> PostGetMeta_SmtpSendByName(string? name, SmtpMessage message) => SendCoreAsync(name, message, AbortToken);
   /// <inheritdoc />
   public Task<SmtpSendResult> SendByNameAsync(string? name, SmtpMessage message) => SendCoreAsync(name, message, CancellationToken.None);
   /// <summary>Sends using a named profile with caller cancellation, without HTTP initialization.</summary>
   public Task<SmtpSendResult> SendByNameAsync(string? name, SmtpMessage message, CancellationToken cancellationToken) => SendCoreAsync(name, message, cancellationToken);
   private async Task<SmtpSendResult> SendCoreAsync(string? name, SmtpMessage message, CancellationToken ct) {
      var (settings, password) = await _profiles.SelectedCredentialsAsync(name, false, ct);
      return await _sender.SendSavedAsync(settings, password, message, ct);
   }
}
