namespace Em.Api.Core.Models;

/// <summary>Explicit SMTP transport security; TLS modes never downgrade to plaintext.</summary>
public enum SmtpSecurity
{
   /// <summary>Plaintext, for unauthenticated relays only.</summary>
   None,
   /// <summary>Require STARTTLS after connecting (usually port 587).</summary>
   StartTls,
   /// <summary>Require TLS from the start (usually port 465).</summary>
   SslOnConnect
}

/// <summary>SMTP settings shared by all modules using this database.</summary>
public sealed class SmtpSettings
{
   /// <summary>Allows sending mail. Disabled by default.</summary>
   public bool Enabled { get; set; }
   /// <summary>SMTP hostname or IP, without a URL scheme.</summary>
   public string Host { get; set; } = "";
   /// <summary>SMTP port.</summary>
   public int Port { get; set; } = 587;
   /// <summary>Connection security.</summary>
   public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
   /// <summary>Whether to authenticate with a username and password/app password.</summary>
   public bool Authenticate { get; set; } = true;
   /// <summary>Authentication username.</summary>
   public string Username { get; set; } = "";
   /// <summary>Optional legacy sender fallback. New callers supply the sender on each message.</summary>
   public string FromAddress { get; set; } = "";
   /// <summary>Optional legacy sender display name.</summary>
   public string FromName { get; set; } = "";
   /// <summary>Total time limit for one connection or send, in seconds (5–120).</summary>
   public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Settings visible to a manager; never contains the password or encryption key.</summary>
public sealed class SmtpSettingsDetail
{
   /// <summary>Current settings.</summary>
   public SmtpSettings Settings { get; set; } = new();
   /// <summary>Revision for optimistic concurrency; zero before the first save.</summary>
   public long Revision { get; set; }
   /// <summary>Whether a password is stored.</summary>
   public bool HasPassword { get; set; }
}

/// <summary>Settings update. Null password retains it; ClearPassword explicitly removes it.</summary>
public sealed class SmtpSettingsSave
{
   /// <summary>Replacement settings.</summary>
   public SmtpSettings Settings { get; set; } = new();
   /// <summary>Revision read by the caller.</summary>
   public long ExpectedRevision { get; set; }
   /// <summary>New password, sent only on save. Null keeps the existing password.</summary>
   public string? Password { get; set; }
   /// <summary>Remove the stored password. Cannot be combined with a new password.</summary>
   public bool ClearPassword { get; set; }
}

/// <summary>Email submitted to the server, including its sender.</summary>
public sealed class SmtpMessage
{
   /// <summary>Sender email address. Null uses the legacy saved sender, if present.</summary>
   public string? FromAddress { get; set; }
   /// <summary>Sender display name. Null uses the legacy name only when the legacy address is used.</summary>
   public string? FromName { get; set; }
   /// <summary>Primary recipients, one plain email address per entry.</summary>
   public string[] To { get; set; } = [];
   /// <summary>Carbon copy recipients.</summary>
   public string[] Cc { get; set; } = [];
   /// <summary>Blind copy recipients; omitted from the transmitted MIME headers.</summary>
   public string[] Bcc { get; set; } = [];
   /// <summary>Optional reply-to address.</summary>
   public string? ReplyTo { get; set; }
   /// <summary>Subject, up to 998 characters, without line breaks.</summary>
   public string Subject { get; set; } = "";
   /// <summary>Plain text body or fallback for HTML.</summary>
   public string? TextBody { get; set; }
   /// <summary>Optional HTML body.</summary>
   public string? HtmlBody { get; set; }
   /// <summary>Attachments, at most 20 and 10 MiB of decoded content in total.</summary>
   public SmtpAttachment[] Attachments { get; set; } = [];
}

/// <summary>Attachment content; JSON serializes the bytes as Base64.</summary>
public sealed class SmtpAttachment
{
   /// <summary>File name only, without directories.</summary>
   public string FileName { get; set; } = "";
   /// <summary>MIME content type.</summary>
   public string ContentType { get; set; } = "application/octet-stream";
   /// <summary>Decoded file bytes.</summary>
   public byte[] Content { get; set; } = [];
}

/// <summary>Successful connection/authentication diagnostic.</summary>
public sealed class SmtpConnectionResult
{
   /// <summary>UTC completion time.</summary>
   public DateTime CheckedAtUtc { get; set; }
   /// <summary>Whether TLS was negotiated.</summary>
   public bool IsSecure { get; set; }
}

/// <summary>SMTP server accepted the message; this does not guarantee inbox delivery.</summary>
public sealed class SmtpSendResult
{
   /// <summary>MIME message ID.</summary>
   public string MessageId { get; set; } = "";
   /// <summary>UTC time of acceptance.</summary>
   public DateTime AcceptedAtUtc { get; set; }
}
