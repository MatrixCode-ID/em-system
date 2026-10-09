using Em.Api.Core.Models;
using Em.Shared;
using MimeKit;
using MimeKit.Utils;

namespace Em.Api.Core.Smtp;

internal static class SmtpValidation
{
   internal static void Settings(SmtpSettings settings, bool hasPassword, bool requireConfigured = false) {
      if (settings is null) throw Bad("SMTP settings are required.");
      if (!Enum.IsDefined(settings.Security)) throw Bad("Invalid SMTP security mode.");
      if (settings.Port is < 1 or > 65535) throw Bad("SMTP port must be between 1 and 65535.");
      if (settings.TimeoutSeconds is < 5 or > 120) throw Bad("SMTP timeout must be between 5 and 120 seconds.");
      if (settings.Host is null || settings.Host.Length > 255 || settings.Host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
          settings.Host.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0 ||
          (settings.Host.Length > 0 && Uri.CheckHostName(settings.Host) == UriHostNameType.Unknown))
         throw Bad("SMTP host must be a hostname or IP address without a URL scheme.");
      Header(settings.FromName, "Sender name", 255);
      Header(settings.Username, "Username", 255);
      if (!string.IsNullOrEmpty(settings.FromAddress)) Address(settings.FromAddress);
      if (!settings.Enabled && !requireConfigured) return;
      if (string.IsNullOrWhiteSpace(settings.Host)) throw Bad("SMTP host is required.");
      if (settings.Authenticate) {
         if (string.IsNullOrWhiteSpace(settings.Username) || !hasPassword) throw Bad("SMTP username and password are required for authentication.");
         if (settings.Security == SmtpSecurity.None) throw Bad("SMTP authentication requires STARTTLS or TLS on connect.");
      }
   }

   internal static MimeMessage Message(SmtpSettings settings, SmtpMessage input) {
      if (input is null) throw Bad("Email message is required.");
      if (input.To is null || input.Cc is null || input.Bcc is null || input.Attachments is null) throw Bad("Email collections cannot be null.");
      var count = (long)input.To.Length + input.Cc.Length + input.Bcc.Length;
      if (count is < 1 or > 100) throw Bad("Email requires between 1 and 100 recipients.");
      Header(input.Subject, "Subject", 998);
      if ((long)(input.TextBody?.Length ?? 0) + (input.HtmlBody?.Length ?? 0) > 2 * 1024 * 1024)
         throw Bad("Email body exceeds 2 MiB of characters.");
      if (string.IsNullOrEmpty(input.TextBody) && string.IsNullOrEmpty(input.HtmlBody)) throw Bad("Email body is required.");
      if (input.Attachments.Length > 20) throw Bad("Email allows at most 20 attachments.");
      var message = new MimeMessage { Subject = input.Subject, MessageId = MimeUtils.GenerateMessageId() };
      var sender = input.FromAddress ?? settings.FromAddress;
      if (string.IsNullOrWhiteSpace(sender)) throw Bad("Sender email address is required on the message.");
      var senderName = input.FromName ?? (input.FromAddress is null ? settings.FromName : "");
      Header(senderName, "Sender name", 255);
      message.From.Add(new MailboxAddress(senderName, Address(sender).Address));
      foreach (var recipient in input.To) message.To.Add(Address(recipient));
      foreach (var recipient in input.Cc) message.Cc.Add(Address(recipient));
      foreach (var recipient in input.Bcc) message.Bcc.Add(Address(recipient));
      if (!string.IsNullOrEmpty(input.ReplyTo)) message.ReplyTo.Add(Address(input.ReplyTo));
      var body = new BodyBuilder { TextBody = input.TextBody, HtmlBody = input.HtmlBody };
      long bytes = 0;
      foreach (var attachment in input.Attachments) {
         if (attachment is null || attachment.Content is null) throw Bad("Attachment content is required.");
         Header(attachment.FileName, "Attachment file name", 255);
         if (string.IsNullOrWhiteSpace(attachment.FileName) || attachment.FileName.IndexOfAny(['/', '\\']) >= 0 || attachment.FileName is "." or "..")
            throw Bad("Attachment file name must not contain a directory.");
         bytes += attachment.Content.LongLength;
         if (bytes > 10 * 1024 * 1024) throw Bad("Attachments exceed 10 MiB in total.");
         Header(attachment.ContentType, "Attachment content type", 255);
         if (!ContentType.TryParse(attachment.ContentType, out var type)) throw Bad("Invalid attachment content type.");
         body.Attachments.Add(attachment.FileName, attachment.Content, type);
      }
      message.Body = body.ToMessageBody();
      return message;
   }

   internal static MailboxAddress Address(string? value) {
      if (string.IsNullOrWhiteSpace(value) || value.Length > 320 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
          !MailboxAddress.TryParse(value, out var address) || address.Address != value || !value.Contains('@'))
         throw Bad("A valid plain email address is required.");
      return address;
   }
   private static void Header(string? value, string name, int max) {
      if (value is null || value.Length > max || value.Any(char.IsControl)) throw Bad($"{name} is invalid or too long.");
   }
   internal static ActionException Bad(string message) => new(message, 400);
}
