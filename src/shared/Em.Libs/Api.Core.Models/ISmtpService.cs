using Em.Shared;

namespace Em.Api.Core.Models;

/// <summary>Server-managed SMTP, usable by API modules and UI clients.</summary>
public interface ISmtpService : IServices
{
   /// <summary>Claim for SMTP configuration and diagnostics.</summary>
   const string ManagerClaim = "SMTP Manager Access";
   /// <summary>Claim for sending application email through the HTTP API.</summary>
   const string SendClaim = "SMTP Send";
   /// <summary>Reads settings without the stored password.</summary>
   Task<SmtpSettingsDetail> GetMeta_SmtpSettings();
   /// <summary>Saves settings immediately, checking the expected revision.</summary>
   Task<SmtpSettingsDetail> PostGetMeta_SmtpSettingsSave(SmtpSettingsSave request);
   /// <summary>Connects and authenticates using saved settings, without sending mail.</summary>
   Task<SmtpConnectionResult> PostGetMeta_SmtpTestConnection();
   /// <summary>Sends a diagnostic message using saved settings; requires the manager claim.</summary>
   Task<SmtpSendResult> PostGetMeta_SmtpTestEmail(string recipient);
   /// <summary>Sends a message using saved settings; requires the send claim over HTTP.</summary>
   Task<SmtpSendResult> PostGetMeta_SmtpSend(SmtpMessage message);
   /// <summary>Convenient entry point for modules. UI calls the send action; API calls are trusted in-process calls.</summary>
   Task<SmtpSendResult> SendAsync(SmtpMessage message);
}
