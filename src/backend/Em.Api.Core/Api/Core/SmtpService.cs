using System.Net.Sockets;
using System.Security.Authentication;
using Em.Api.Core.Models;
using Em.Api.Core.Smtp;
using Em.Shared;
using MailKit;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core;

/// <summary>SMTP actions and trusted in-process sending. Dependencies work without HTTP request initialization.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class SmtpService : ServicesBase, ISmtpService
{
   private readonly ISmtpSettingsStore _store;
   private readonly ISmtpTransport _transport;
   /// <summary>Creates the service in the request or background scope.</summary>
   public SmtpService(IDbContextFactory<ApiCoreContext> factory) : this(new SmtpSettingsStore(factory), new SmtpTransport()) { }
   internal SmtpService(ISmtpSettingsStore store, ISmtpTransport transport) { _store = store; _transport = transport; }

   /// <inheritdoc />
   [GetAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpSettingsDetail> GetMeta_SmtpSettings() => _store.ReadAsync(AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpSettingsDetail> PostGetMeta_SmtpSettingsSave(SmtpSettingsSave request) => _store.SaveAsync(request, AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public async Task<SmtpConnectionResult> PostGetMeta_SmtpTestConnection() {
      var (settings, password) = await _store.CredentialsAsync(AbortToken);
      SmtpValidation.Settings(settings, !string.IsNullOrEmpty(password), true);
      var secure = await ExecuteAsync(settings, password, null, AbortToken);
      return new() { CheckedAtUtc = DateTime.UtcNow, IsSecure = secure };
   }
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.ManagerClaim)]
   public Task<SmtpSendResult> PostGetMeta_SmtpTestEmail(string recipient) => SendCoreAsync(new() {
      To = [recipient], Subject = "SMTP test email", TextBody = "This test email was sent by the Em SMTP Manager."
   }, AbortToken);
   /// <inheritdoc />
   [PostAction(claim: ISmtpService.SendClaim)]
   public Task<SmtpSendResult> PostGetMeta_SmtpSend(SmtpMessage message) => SendCoreAsync(message, AbortToken);
   /// <inheritdoc />
   public Task<SmtpSendResult> SendAsync(SmtpMessage message) => SendCoreAsync(message, CancellationToken.None);
   /// <summary>Trusted server-side send with caller cancellation. Cancellation after DATA may leave delivery uncertain.</summary>
   public Task<SmtpSendResult> SendAsync(SmtpMessage message, CancellationToken cancellationToken) => SendCoreAsync(message, cancellationToken);

   private async Task<SmtpSendResult> SendCoreAsync(SmtpMessage message, CancellationToken ct) {
      var (settings, password) = await _store.CredentialsAsync(ct);
      return await SendSavedAsync(settings, password, message, ct);
   }

   internal async Task<SmtpConnectionResult> TestSavedAsync(SmtpSettings settings, string? password, CancellationToken ct) {
      SmtpValidation.Settings(settings, !string.IsNullOrEmpty(password), true);
      return new() { CheckedAtUtc = DateTime.UtcNow, IsSecure = await ExecuteAsync(settings, password, null, ct) };
   }

   internal async Task<SmtpSendResult> SendSavedAsync(SmtpSettings settings, string? password, SmtpMessage message, CancellationToken ct) {
      if (!settings.Enabled) throw new ActionException("SMTP sending is disabled. Enable it in SMTP Manager.", 409);
      SmtpValidation.Settings(settings, !string.IsNullOrEmpty(password), true);
      using var mime = SmtpValidation.Message(settings, message);
      await ExecuteAsync(settings, password, mime, ct);
      return new() { MessageId = mime.MessageId!, AcceptedAtUtc = DateTime.UtcNow };
   }

   private async Task<bool> ExecuteAsync(SmtpSettings settings, string? password, MimeMessage? message, CancellationToken ct) {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
      try { return await _transport.ExecuteAsync(settings, password, message, timeout.Token); }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
         throw new ActionException("SMTP operation timed out. Delivery may be uncertain; check before sending again.", 504);
      }
      catch (Exception ex) when (ex is MailKit.Security.AuthenticationException or ServiceNotAuthenticatedException) {
         throw new ActionException("SMTP authentication failed. Check the saved username and password.", 502);
      }
      catch (Exception ex) when (ex is AuthenticationException or MailKit.Security.SslHandshakeException) {
         throw new ActionException("SMTP TLS negotiation failed. Check the security mode and server certificate.", 502);
      }
      catch (Exception ex) when (ex is SocketException or IOException or SmtpCommandException or SmtpProtocolException or NotSupportedException) {
         throw new ActionException("SMTP operation failed. Check the server, port, security mode and recipient. Delivery may be uncertain.", 502);
      }
   }
}
