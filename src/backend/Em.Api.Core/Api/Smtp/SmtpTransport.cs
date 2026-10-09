using Em.Api.Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Em.Api.Core.Smtp;

internal interface ISmtpTransport
{
   Task<bool> ExecuteAsync(SmtpSettings settings, string? password, MimeMessage? message, CancellationToken ct);
}

internal sealed class SmtpTransport : ISmtpTransport
{
   public async Task<bool> ExecuteAsync(SmtpSettings settings, string? password, MimeMessage? message, CancellationToken ct) {
      using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
      var security = settings.Security switch {
         SmtpSecurity.None => SecureSocketOptions.None,
         SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
         SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
         _ => throw SmtpValidation.Bad("Invalid SMTP security mode.")
      };
      await client.ConnectAsync(settings.Host, settings.Port, security, ct);
      if (settings.Authenticate) await client.AuthenticateAsync(settings.Username, password!, ct);
      var secure = client.IsSecure;
      if (message is not null) await client.SendAsync(message, ct);
      // Once DATA is accepted, a failed QUIT must not turn success into a retryable send failure.
      try { await client.DisconnectAsync(true, ct); }
      catch (Exception ex) when (ex is IOException or SmtpProtocolException or OperationCanceledException) { }
      return secure;
   }
}
