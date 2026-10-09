# SMTP engine

The SMTP engine provides one outgoing email configuration per core database. `ISmtpService` lives in
`Em.Libs`; API modules and UI clients use the same contract. WPF includes **SMTP Manager** under
Administrative Tools. WPF and MAUI register the shared UI proxy automatically; the manager screen is WPF.

## Enable the engine

```csharp
var app = EmApp.BuildApp(args, builder => {
   // Configure the database and other engine services first.
   builder.AddSmtp();
});
```

The sample `Em.Api` host enables it. No new tables or migration are required: the settings and key use
the existing core `ta_Meta` table. Hosts that omit `AddSmtp()` do not declare SMTP claims, so the WPF
manager is hidden. SMTP sending starts disabled until configured and enabled.

## SMTP Manager

Use **Save** to configure the hostname, port, security mode, username/password, sender and timeout.
Changes take effect immediately for subsequent calls. **Test connection** connects and authenticates
against saved settings without sending mail; it can run while sending is disabled. **Test email** opens
the shared side sheet and sends a short message to one recipient. Sending must be enabled. Save edits
before testing. The refresh button reloads the card and discards local edits, including a draft password.

The manager uses the shared Material Design buttons, inputs, card, side-sheet toggles and wait overlay.
The toolbar stays horizontal and scrolls on narrow screens. Escape closes the test sheet.

## Use from a module

```csharp
var smtp = app.ServiceProvider.GetRequiredService<ISmtpService>();
var result = await smtp.SendAsync(new SmtpMessage {
   To = ["recipient@example.com"],
   Subject = "Your document is ready",
   TextBody = "Your document is ready to download.",
   HtmlBody = "<p>Your document is ready to download.</p>",
   Attachments = [new SmtpAttachment {
      FileName = "document.pdf", ContentType = "application/pdf", Content = pdfBytes
   }]
});
```

On the API, resolve the scoped service inside the current request or a new scope for background work.
It does not depend on HTTP initialization and therefore works from other API services and background
jobs. In-process calls are trusted; the owning module must enforce its own business permissions.
The concrete API `SmtpService` also has a cancellation-token overload. On the UI, `SendAsync` calls
the authenticated send action through the active API connection. SMTP credentials never reach the UI.

## Permissions and action map

All actions are in the `Administrative Tools` module.

| Action | Claim | Purpose |
| --- | --- | --- |
| `GetMeta_SmtpSettings` | `SMTP Manager Access` | Read settings, revision and password-presence flag |
| `PostGetMeta_SmtpSettingsSave` | `SMTP Manager Access` | Save settings and optionally replace/clear the password |
| `PostGetMeta_SmtpTestConnection` | `SMTP Manager Access` | Connect and authenticate |
| `PostGetMeta_SmtpTestEmail` | `SMTP Manager Access` | Send a predefined diagnostic email |
| `PostGetMeta_SmtpSend` | `SMTP Send` | Send application email |

The manager claim does not automatically grant general sending over HTTP. Modules can grant `SMTP Send`
without granting access to credentials or configuration. Internal `SendAsync` is not an HTTP action.

## Security, persistence and limits

Settings are JSON in `ta_Meta` at `Em.Smtp.Settings`. The password is encrypted with AES-256-GCM; the
random key is in `Em.Smtp.Key`. Back up both rows together. Database readers can read the key and decrypt
the password, so encryption does not protect against full database access. Passwords and keys are never
included in action results or error messages. Null password retains it; `ClearPassword` removes it.
Empty replacement passwords are rejected. Saves check `ExpectedRevision` in a serializable transaction;
a stale or conflicting write returns 409 and requires reload.

The transport uses [MailKit](https://www.nuget.org/packages/MailKit/4.17.0).
[Required STARTTLS](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm)
and TLS on connect validate the server certificate and never downgrade. Plaintext mode is limited to
unauthenticated relays. Authentication supports username/password or an app password; OAuth token
acquisition is not included.

Each operation opens its own connection and observes a total timeout (5–120 seconds; default 30).
Up to 100 total To/Cc/Bcc recipients, 20 attachments, 10 MiB decoded attachment content and 2 MiB body
characters are accepted. Recipients are plain email addresses; the sender always comes from saved
settings. Bcc recipients are included in the SMTP envelope and omitted from transmitted MIME headers.

A send result means the SMTP server accepted DATA, not that the inbox received it. No automatic retry
or queue is used: a connection drop or cancellation after DATA can leave delivery uncertain. A failed
QUIT after acceptance does not turn a successful send into a failure.

## Maintainer notes

Console tests use fake transports, SQL Server databases created by the integration fixture, and a local
SMTP listener; they do not send external email. Visual render and live UI interaction require explicit
user confirmation under `claude.md`.

Verification on 2026-10-09: backend, WPF and MAUI builds passed. All 141 backend tests and 79 UI tests
passed, including SQL persistence/concurrency, SMTP loopback delivery, required STARTTLS refusal,
shared proxy action serialization/connection switching, manager state and XAML resource loading without
rendering. Live SMTP delivery and visual/GUI checks remain unverified.
