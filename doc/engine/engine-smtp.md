# SMTP engine

The SMTP engine manages multiple named outgoing email configurations per core database in `ta_Smtp`.
`ISmtpService` remains the stable default-SMTP contract. `ISmtpProfileService` adds named sending and
profile management. WPF provides **SMTP Manager** under Administrative Tools; WPF and MAUI register
both client proxies automatically. The manager UI is WPF only.

## Enable and upgrade

```csharp
builder.AddSmtp();
```

The sample `Em.Api` host enables the engine. Hosts that omit it do not declare SMTP claims, so the
manager is hidden. Before using the updated API, install
[`tables/050-smtp.sql`](../sqlscript/mssql/tables/050-smtp.sql) after `010-core.sql`, then
[`views/vi_Smtp.sql`](../sqlscript/mssql/views/vi_Smtp.sql). Both are safe to run again and require schema
creation permissions. A missing table produces an actionable 503 error. Other database providers
need an equivalent schema; the supplied installation scripts target SQL Server.

On first use, a serializable transaction converts the old `Em.Smtp.Settings` document into a profile
named `default`, preserving its settings, enabled state, encrypted password and encryption key. Its
legacy revision remains a `long`, even if greater than `int.MaxValue`. The original document remains
an archive. `Em.Smtp.Profiles.Revision` marks completed migration and holds the global concurrency
token. Never delete or reset this marker: it prevents deleted profiles from being re-imported.
A missing key or conflicting pre-existing profiles aborts migration rather than overwriting data.

Stop older API processes before upgrading. An older API still writes the archived singleton document;
it cannot participate in the new profile transaction protocol. Old clients can call the updated API.
A fresh database starts with an empty list. New profiles start disabled and are not automatically default.

## SMTP Manager (WPF)

The manager displays Name, Host, Port, Security, Enabled and Default in a table. **New SMTP**
remains available with an existing default or an empty list. **Edit** or a left-button double-click on a row opens a separate draft in a side
sheet. The editor configures the connection only; no sender is required to save, including enabled profiles. **Save** persists it immediately; **Cancel** or Escape discards it and clears the draft password.
Names contain 1-255 printable ASCII characters, are trimmed, and are unique case-insensitively. Rename
changes the name used by callers; stable IDs remain unchanged. The optional note is limited to 500 characters.

Select a saved row for **Set default**, **Test connection**, or **Delete**. Default is
independent of Enabled. At most one default exists; selecting a replacement is atomic. **Clear default**
keeps all profiles. Deleting the default also leaves no default. Disabling it keeps its default marker
and makes sending fail. Nothing automatically selects another profile.

**Test connection** uses the selected table row and can run while sending is disabled.
The second toolbar replaces the old Test email button and side sheet. Select a saved SMTP by name
in its combo box, enter **From** and **To**, then click **Send**. This choice is independent of the
selected table row and the default SMTP. Sending requires an enabled saved profile; From and To
are editable combo boxes: type an address or select one from local history. These addresses
are used only for the predefined test message and are not persisted on the profile. Results and
errors appear beneath this toolbar. Controls are disabled during requests or while editing, and
Send remains unavailable until a profile and both addresses are supplied.

Refresh reloads the entire list and discards local edits after a successful response; it remains
available after a failed request. The test selection is preserved by profile ID across refreshes,
and cleared if that profile is deleted. Both toolbars scroll horizontally on narrow screens;
the table supports horizontal/vertical scrolling.

After server acceptance, the From and To addresses are remembered separately, newest first,
trimmed and deduplicated case-insensitively, with at most 20 addresses per list. History is
stored as `REG_MULTI_SZ` values `FromHistory` and `ToHistory` under `SmtpManager` in the host
application's `EmApp.BaseRegKey`; the feature never creates a separate Registry root. History
belongs to the current Windows user and host application, and is shared across SMTP/API profiles.
Failed tests are not added. **Clear history** removes both saved lists and dropdown suggestions
while retaining the current input text and SMTP selection. Other Registry settings are preserved.
If history storage fails after sending, the acceptance result remains visible with a history error.

## Send from a module

Existing calls keep working through the default profile:

```csharp
var smtp = app.ServiceProvider.GetRequiredService<ISmtpService>();
var result = await smtp.SendAsync(new SmtpMessage {
   FromAddress = "notifications@example.com", FromName = "Notifications",
   To = ["recipient@example.com"],
   Subject = "Your document is ready",
   TextBody = "Your document is ready to download.",
   HtmlBody = "<p>Your document is ready to download.</p>",
   Attachments = [new SmtpAttachment {
      FileName = "document.pdf", ContentType = "application/pdf", Content = pdfBytes
   }]
});
```

To select a named profile:

```csharp
var profiles = app.ServiceProvider.GetRequiredService<ISmtpProfileService>();
await profiles.SendByNameAsync("notifications", new SmtpMessage {
   FromAddress = "notifications@example.com",
   To = ["recipient@example.com"], Subject = "Notification", TextBody = "Ready."
});
```

A null/empty/whitespace name selects the default. No default produces 409, even with exactly one profile.
An unknown name produces 404. A disabled selected profile produces 409. There is no fallback to another
profile or the archived singleton. Names are compared case-insensitively. The consuming module supplies `SmtpMessage.FromAddress` and optionally `FromName` for each email. A null sender falls back to the old saved sender for compatibility; without either, sending returns 400. Connection testing and profile saving do not require a sender. The SMTP server may restrict which sender addresses it accepts.

Resolve API services in the current request scope or a new background scope. Trusted in-process calls
work without HTTP initialization; the owning module enforces business permissions. Concrete API services
also offer cancellation-token overloads. UI proxies call the authenticated send action on the active
connection, requiring `SMTP Send`; SMTP credentials never reach the UI.

## Actions and compatibility

All actions belong to `Administrative Tools`. Existing DTO fields remain available; `SmtpMessage` adds optional `FromAddress` and `FromName` fields. The original interface, action names, parameter
order and types, return types, HTTP verbs and claims remain available. Legacy settings read/save operate
on the default and keep their single-settings response. Reading without a default returns empty disabled
settings. Saving through the legacy action with no default creates a new default profile, preserving the
old client's initial configuration workflow. This does not change New SMTP's manual default selection.

| Action | Parameters | Claim |
| --- | --- | --- |
| `GetMeta_SmtpSettings` (GET) | none | `SMTP Manager Access` |
| `PostGetMeta_SmtpSettingsSave` | `SmtpSettingsSave request` | `SMTP Manager Access` |
| `PostGetMeta_SmtpTestConnection` | none | `SMTP Manager Access` |
| `PostGetMeta_SmtpTestEmail` | `string recipient` | `SMTP Manager Access` |
| `PostGetMeta_SmtpSend` | `SmtpMessage message` | `SMTP Send` |
| `GetMeta_SmtpProfiles` (GET) | none | `SMTP Manager Access` |
| `PostGetMeta_SmtpProfileSave` | `SmtpProfileSave request` | `SMTP Manager Access` |
| `PostGetMeta_SmtpProfileDelete` | `string id`, `long expectedRevision` | `SMTP Manager Access` |
| `PostGetMeta_SmtpDefault` | `string? id`, `long expectedRevision` | `SMTP Manager Access` |
| `PostGetMeta_SmtpProfileTestConnection` | `string id` | `SMTP Manager Access` |
| `PostGetMeta_SmtpProfileTestEmailFrom` | `string id, string sender, string recipient` | `SMTP Manager Access` |
| `PostGetMeta_SmtpProfileTestEmail` | `string id`, `string recipient` | `SMTP Manager Access` |
| `PostGetMeta_SmtpSendByName` | `string? name`, `SmtpMessage message` | `SMTP Send` |

All actions except the two GETs are POST. `SendAsync` and `SendByNameAsync` are convenience methods,
not HTTP actions. The manager claim does not automatically grant general HTTP sending. Existing
implementations of `ISmtpService` do not need to implement the new profile interface.

Every list/profile response supplies a global `long` revision. Mutations require that token and return
409 for stale reads, including a default switch after a legacy client read. Any profile mutation invalidates
prior tokens; reload before retrying a conflict. `cSmtpRevision` is a separate per-profile `int` revision.
Concurrent saves and default selection are serialized in the database, not just within one API process.

## Persistence, security and limits

The key stays in `ta_Meta` at `Em.Smtp.Key`; `cSmtpPassword` holds AES-256-GCM ciphertext. Back up
`ta_Smtp`, key, revision marker and legacy archive together. Database readers can obtain the key and
therefore decrypt passwords. The archive retains the original encrypted credentials; it is never used
for runtime sending after migration. Profile responses omit ciphertext and keys. `vi_Smtp` exposes only
`cvSmtpHasPassword`, not the password column. The application enforces claims independently of the view.

Null password preserves it; `ClearPassword` removes the active profile's password. Empty replacement
passwords are rejected. Required STARTTLS and TLS on connect validate the server certificate and do not
downgrade. Plaintext mode is restricted to unauthenticated relays. Username/password and app passwords
are supported; OAuth token acquisition is not included. Transport uses MailKit 4.17.0.

Each operation opens its own connection with a total timeout of 5-120 seconds (default 30). Limits remain
100 total To/Cc/Bcc recipients, 20 attachments, 10 MiB decoded attachment content and 2 MiB body
characters. Bcc is sent in the SMTP envelope and omitted from transmitted MIME headers.

Acceptance means the SMTP server accepted DATA, not that the inbox received it. There is no queue or
automatic retry. Cancellation/disconnection after DATA can leave delivery uncertain. A failed QUIT after
acceptance does not change a successful send to failure.

## Maintainer notes

Console tests cover the old contract/proxy, profile routing, SQL persistence and migration, credentials,
concurrency, and manager state. The SQL schema smoke harness is outside the repository at
`../.artefacts/em-system/scripts/smtp-schema-smoke/schema-smoke.ps1`. It checks multiple profiles,
default/name uniqueness, constraints, password-free view, default replacement and idempotent scripts.
Tests use fake transports or local SMTP listeners and do not send external mail.

Verification on 2026-10-09: backend, WPF and MAUI builds passed; all 148 backend
and 82 UI console tests passed with no skipped tests. This includes SQL migration,
long legacy revisions, default replacement, stale saves, named routing, both
client proxies, editor state and XAML resource loading without rendering. The
schema/view scripts passed the temporary SQL database smoke check. They were also
applied to the two local databases listed in the private migration target notes;
both had no saved SMTP configurations, and repeated migration plus the legacy
adapter passed. A subsequent missing-table report revealed that the active API
used a remote database. Its credentials were available in the local configuration;
the initial migration had omitted that target. The SQL scripts have now been
applied to all three `OSHA_CSM` targets requested by the user, recorded in the
private migration notes. Columns, indexes, check/default constraints and view
definitions match. Profile-list and legacy read actions plus idempotent migration
passed on each database. All three currently have no configured SMTP profiles.

Visual rendering, live mouse interaction, and delivery through an external SMTP provider remain
unverified. GUI verification is not run without explicit user confirmation under `claude.md`.
