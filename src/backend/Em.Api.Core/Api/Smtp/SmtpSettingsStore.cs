using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Smtp;

internal sealed class SmtpSettingsDocument
{
   public long Revision { get; set; }
   public SmtpSettings Settings { get; set; } = new();
   public byte[]? Password { get; set; }
}

internal interface ISmtpSettingsStore
{
   Task<SmtpSettingsDetail> ReadAsync(CancellationToken ct);
   Task<SmtpSettingsDetail> SaveAsync(SmtpSettingsSave request, CancellationToken ct);
   Task<(SmtpSettings Settings, string? Password)> CredentialsAsync(CancellationToken ct);
}

internal interface ISmtpProfileStore
{
   Task<SmtpProfileList> ProfilesAsync(CancellationToken ct);
   Task<SmtpProfileDetail> SaveProfileAsync(SmtpProfileSave request, CancellationToken ct);
   Task<SmtpProfileList> DeleteAsync(string id, long revision, CancellationToken ct);
   Task<SmtpProfileList> DefaultAsync(string? id, long revision, CancellationToken ct);
   Task<(SmtpSettings Settings, string? Password)> SelectedCredentialsAsync(string? selector, bool byId, CancellationToken ct);
}

/// <summary>Transactional profile storage, migration and the stable default-settings adapter.</summary>
internal sealed class SmtpSettingsStore(IDbContextFactory<ApiCoreContext> factory) : ISmtpSettingsStore, ISmtpProfileStore
{
   internal const string SettingsKey = "Em.Smtp.Settings";
   internal const string SecretKey = "Em.Smtp.Key";
   internal const string RevisionKey = "Em.Smtp.Profiles.Revision";

   private async Task<T> TransactionAsync<T>(Func<ApiCoreContext, ta_Meta, Task<T>> operation, CancellationToken ct) {
      await using var db = await factory.CreateDbContextAsync(ct);
      try {
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
         // Serialize mutations and first-use migration on one metadata row, including legacy saves.
         var query = db.Database.IsSqlServer()
            ? db.ta_Metas.FromSqlInterpolated($"SELECT * FROM [dbo].[ta_Meta] WITH (UPDLOCK, HOLDLOCK) WHERE [cMetaKey] = {RevisionKey}")
            : db.ta_Metas.Where(x => x.cMetaKey == RevisionKey);
         var marker = await query.AsTracking().SingleOrDefaultAsync(ct);
         if (marker is null) {
            var legacy = await db.ta_Metas.AsNoTracking().SingleOrDefaultAsync(x => x.cMetaKey == SettingsKey, ct);
            long revision = 0;
            if (legacy is not null) {
               if (await db.ta_Smtps.AnyAsync(ct)) throw new ActionException("SMTP migration conflicts with existing profiles. Resolve the configuration before retrying.", 409);
               var old = JsonSerializer.Deserialize<SmtpSettingsDocument>(legacy.cMetaValue)
                  ?? throw new ActionException("Invalid legacy SMTP configuration. Restore it before migrating.", 409);
               if (old.Revision < 0) throw new ActionException("Invalid legacy SMTP revision.", 409);
               revision = old.Revision;
               if (old.Password is { Length: > 0 }) {
                  var key = await db.ta_Metas.AsNoTracking().SingleOrDefaultAsync(x => x.cMetaKey == SecretKey, ct)
                     ?? throw new ActionException("SMTP encryption key is missing. Restore it before migrating.", 409);
                  _ = SmtpSecrets.Decrypt(Convert.FromBase64String(key.cMetaValue), old.Password);
               }
               var row = NewRow("default"); WriteSettings(row, old.Settings);
               row.cSmtpDefault = true; row.cSmtpPassword = old.Password; db.ta_Smtps.Add(row);
            }
            marker = new() { cMetaKey = RevisionKey, cMetaValue = revision.ToString(CultureInfo.InvariantCulture),
               cMetaDescription = "SMTP migration marker and global concurrency token. Do not reset.", ustamp = DateTime.UtcNow };
            db.ta_Metas.Add(marker);
            // Keep the old document as an archive; marker presence prevents resurrection after deletion.
            await db.SaveChangesAsync(ct);
         }
         var result = await operation(db, marker);
         await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
      }
      catch (Exception ex) when (IsConflict(ex)) {
         throw new ActionException("SMTP settings changed or the name is already in use. Reload before saving.", 409);
      }
      catch (Exception ex) when (IsMissingSchema(ex)) {
         throw new ActionException("SMTP schema is missing. Apply tables/050-smtp.sql before using SMTP.", 503);
      }
   }

   private static long Revision(ta_Meta marker) => long.Parse(marker.cMetaValue, CultureInfo.InvariantCulture);
   private static void Expect(ta_Meta marker, long expected) {
      if (Revision(marker) != expected) throw new ActionException("SMTP settings changed. Reload before saving.", 409);
   }
   private static long Bump(ta_Meta marker) {
      var next = checked(Revision(marker) + 1);
      marker.cMetaValue = next.ToString(CultureInfo.InvariantCulture); marker.ustamp = DateTime.UtcNow; return next;
   }
   private static ta_Smtp NewRow(string name) => new() { cSmtpId = Ulid.NewUlid().ToString(), cSmtpName = name,
      datestamp = DateTime.UtcNow, ustamp = DateTime.UtcNow, cSmtpRevision = 1 };
   private static SmtpSettings Settings(ta_Smtp row) => new() { Enabled = row.cSmtpState == 1, Host = row.cSmtpHost,
      Port = row.cSmtpPort, Security = (SmtpSecurity)row.cSmtpSecurity, Authenticate = row.cSmtpAuthenticate,
      Username = row.cSmtpUsername, FromAddress = row.cSmtpFromAddress, FromName = row.cSmtpFromName,
      TimeoutSeconds = row.cSmtpTimeoutSeconds };
   private static void WriteSettings(ta_Smtp row, SmtpSettings settings) {
      row.cSmtpState = settings.Enabled ? 1 : 0; row.cSmtpHost = settings.Host; row.cSmtpPort = settings.Port;
      row.cSmtpSecurity = (int)settings.Security; row.cSmtpAuthenticate = settings.Authenticate;
      row.cSmtpUsername = settings.Username; row.cSmtpFromAddress = settings.FromAddress;
      row.cSmtpFromName = settings.FromName; row.cSmtpTimeoutSeconds = settings.TimeoutSeconds; row.ustamp = DateTime.UtcNow;
   }
   private static SmtpProfileDetail Detail(ta_Smtp row, long revision) => new() { Id = row.cSmtpId, Name = row.cSmtpName,
      Note = row.cSmtpNote, IsDefault = row.cSmtpDefault, Revision = revision, Settings = Settings(row),
      HasPassword = row.cSmtpPassword is { Length: > 0 } };
   private static async Task<SmtpProfileList> ListAsync(ApiCoreContext db, ta_Meta marker, CancellationToken ct) => new() {
      Revision = Revision(marker), Profiles = (await db.ta_Smtps.AsNoTracking().OrderBy(x => x.cSmtpName).ToArrayAsync(ct))
         .Select(x => Detail(x, Revision(marker))).ToArray()
   };
   private static async Task<ta_Smtp> FindAsync(ApiCoreContext db, string id, CancellationToken ct) =>
      await db.ta_Smtps.AsTracking().SingleOrDefaultAsync(x => x.cSmtpId == id, ct)
         ?? throw new ActionException("SMTP profile was not found.", 404);

   public Task<SmtpSettingsDetail> ReadAsync(CancellationToken ct) => TransactionAsync(async (db, marker) => {
      var row = await db.ta_Smtps.AsNoTracking().SingleOrDefaultAsync(x => x.cSmtpDefault, ct);
      return new SmtpSettingsDetail { Settings = row is null ? new() : Settings(row), Revision = Revision(marker),
         HasPassword = row?.cSmtpPassword is { Length: > 0 } };
   }, ct);

   public Task<SmtpSettingsDetail> SaveAsync(SmtpSettingsSave request, CancellationToken ct) {
      if (request is null) throw SmtpValidation.Bad("SMTP settings update is required.");
      ValidateSecret(request.Password, request.ClearPassword);
      return TransactionAsync(async (db, marker) => {
         Expect(marker, request.ExpectedRevision);
         var row = await db.ta_Smtps.AsTracking().SingleOrDefaultAsync(x => x.cSmtpDefault, ct);
         if (row is null) {
            row = NewRow("default");
            if ((await db.ta_Smtps.Select(x => x.cSmtpName).ToArrayAsync(ct)).Any(x => string.Equals(x, "default", StringComparison.OrdinalIgnoreCase)))
               row.cSmtpName += "-" + row.cSmtpId;
            row.cSmtpDefault = true; db.ta_Smtps.Add(row);
         } else row.cSmtpRevision = checked(row.cSmtpRevision + 1);
         await UpdateAsync(db, row, request.Settings, request.Password, request.ClearPassword, ct);
         return new SmtpSettingsDetail { Settings = Settings(row), Revision = Bump(marker),
            HasPassword = row.cSmtpPassword is { Length: > 0 } };
      }, ct);
   }

   public Task<SmtpProfileList> ProfilesAsync(CancellationToken ct) => TransactionAsync((db, marker) => ListAsync(db, marker, ct), ct);
   public Task<SmtpProfileDetail> SaveProfileAsync(SmtpProfileSave request, CancellationToken ct) {
      if (request is null) throw SmtpValidation.Bad("SMTP profile update is required.");
      var name = request.Name?.Trim();
      if (string.IsNullOrEmpty(name) || name.Length > 255 || name.Any(c => c < 32 || c > 126))
         throw SmtpValidation.Bad("SMTP name must contain 1-255 printable ASCII characters.");
      if (request.Note is { Length: > 500 } || request.Note?.Any(char.IsControl) == true)
         throw SmtpValidation.Bad("SMTP note is invalid or too long.");
      ValidateSecret(request.Password, request.ClearPassword);
      return TransactionAsync(async (db, marker) => {
         Expect(marker, request.ExpectedRevision);
         var rows = await db.ta_Smtps.AsNoTracking().ToArrayAsync(ct);
         if (rows.Any(x => x.cSmtpId != request.Id && string.Equals(x.cSmtpName, name, StringComparison.OrdinalIgnoreCase)))
            throw new ActionException("SMTP name is already in use.", 409);
         ta_Smtp row;
         if (request.Id is null) { row = NewRow(name); db.ta_Smtps.Add(row); }
         else { row = await FindAsync(db, request.Id, ct); row.cSmtpRevision = checked(row.cSmtpRevision + 1); }
         row.cSmtpName = name; row.cSmtpNote = request.Note;
         await UpdateAsync(db, row, request.Settings, request.Password, request.ClearPassword, ct);
         return Detail(row, Bump(marker));
      }, ct);
   }
   public Task<SmtpProfileList> DeleteAsync(string id, long revision, CancellationToken ct) => TransactionAsync(async (db, marker) => {
      Expect(marker, revision); db.ta_Smtps.Remove(await FindAsync(db, id, ct)); Bump(marker);
      await db.SaveChangesAsync(ct); return await ListAsync(db, marker, ct);
   }, ct);
   public Task<SmtpProfileList> DefaultAsync(string? id, long revision, CancellationToken ct) => TransactionAsync(async (db, marker) => {
      Expect(marker, revision);
      var selected = id is null ? null : await FindAsync(db, id, ct);
      var current = await db.ta_Smtps.AsTracking().SingleOrDefaultAsync(x => x.cSmtpDefault, ct);
      if (current?.cSmtpId == selected?.cSmtpId) return await ListAsync(db, marker, ct);
      if (current is not null) { current.cSmtpDefault = false; current.cSmtpRevision = checked(current.cSmtpRevision + 1); current.ustamp = DateTime.UtcNow; }
      // Flush the old default before setting the new one to satisfy the filtered unique index.
      await db.SaveChangesAsync(ct);
      if (selected is not null) { selected.cSmtpDefault = true; selected.cSmtpRevision = checked(selected.cSmtpRevision + 1); selected.ustamp = DateTime.UtcNow; }
      Bump(marker); await db.SaveChangesAsync(ct); return await ListAsync(db, marker, ct);
   }, ct);

   private static void ValidateSecret(string? password, bool clear) {
      if (clear && password is not null) throw SmtpValidation.Bad("Cannot replace and clear the SMTP password together.");
      if (password is { Length: 0 or > 4096 }) throw SmtpValidation.Bad("SMTP password must contain between 1 and 4096 characters.");
   }
   private static async Task UpdateAsync(ApiCoreContext db, ta_Smtp row, SmtpSettings settings, string? password, bool clear, CancellationToken ct) {
      if (clear) row.cSmtpPassword = null;
      SmtpValidation.Settings(settings, password is not null || row.cSmtpPassword is { Length: > 0 });
      if (password is not null) {
         var key = await db.ta_Metas.AsTracking().SingleOrDefaultAsync(x => x.cMetaKey == SecretKey, ct);
         if (key is null) {
            key = new() { cMetaKey = SecretKey, cMetaValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
               cMetaDescription = "SMTP AES-GCM key. Back up with profiles; do not change.", ustamp = DateTime.UtcNow };
            db.ta_Metas.Add(key);
         }
         row.cSmtpPassword = SmtpSecrets.Encrypt(Convert.FromBase64String(key.cMetaValue), password);
      }
      WriteSettings(row, settings);
   }
   public Task<(SmtpSettings Settings, string? Password)> CredentialsAsync(CancellationToken ct) => SelectedCredentialsAsync(null, false, ct);
   public Task<(SmtpSettings Settings, string? Password)> SelectedCredentialsAsync(string? selector, bool byId, CancellationToken ct) =>
      TransactionAsync(async (db, marker) => {
         ta_Smtp? row;
         if (byId) row = await db.ta_Smtps.AsNoTracking().SingleOrDefaultAsync(x => x.cSmtpId == selector, ct);
         else if (string.IsNullOrWhiteSpace(selector)) row = await db.ta_Smtps.AsNoTracking().SingleOrDefaultAsync(x => x.cSmtpDefault, ct);
         else row = (await db.ta_Smtps.AsNoTracking().ToArrayAsync(ct))
            .SingleOrDefault(x => string.Equals(x.cSmtpName, selector.Trim(), StringComparison.OrdinalIgnoreCase));
         if (row is null) throw new ActionException(!byId && string.IsNullOrWhiteSpace(selector)
            ? "No default SMTP is configured." : "SMTP profile was not found.", !byId && string.IsNullOrWhiteSpace(selector) ? 409 : 404);
         string? password = null;
         if (row.cSmtpPassword is { Length: > 0 }) {
            var key = await db.ta_Metas.AsNoTracking().SingleAsync(x => x.cMetaKey == SecretKey, ct);
            password = SmtpSecrets.Decrypt(Convert.FromBase64String(key.cMetaValue), row.cSmtpPassword);
         }
         return (Settings(row), password);
      }, ct);

   private static bool IsConflict(Exception ex) {
      for (Exception? error = ex; error is not null; error = error.InnerException) {
         if (error is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 1205 or 2601 or 2627) return true;
         if (error is Npgsql.PostgresException pg && pg.SqlState is "40001" or "40P01" or "23505") return true;
         if (error is MySqlConnector.MySqlException mysql && mysql.Number is 1213 or 1062) return true;
      }
      return false;
   }
   private static bool IsMissingSchema(Exception ex) {
      for (Exception? error = ex; error is not null; error = error.InnerException)
         if (error is Microsoft.Data.SqlClient.SqlException { Number: 208 }) return true;
      return false;
   }
}

internal static class SmtpSecrets
{
   // Version(1), nonce(12), tag(16), ciphertext. The key is stored separately in ta_Meta.
   internal static byte[] Encrypt(byte[] key, string password) {
      var plain = Encoding.UTF8.GetBytes(password);
      try {
         var data = new byte[29 + plain.Length];
         data[0] = 1;
         RandomNumberGenerator.Fill(data.AsSpan(1, 12));
         using var aes = new AesGcm(key, 16);
         aes.Encrypt(data.AsSpan(1, 12), plain, data.AsSpan(29), data.AsSpan(13, 16));
         return data;
      }
      finally { CryptographicOperations.ZeroMemory(plain); }
   }
   internal static string Decrypt(byte[] key, byte[] data) {
      if (data.Length < 29 || data[0] != 1) throw new CryptographicException("Invalid SMTP secret format.");
      var plain = new byte[data.Length - 29];
      try {
         using var aes = new AesGcm(key, 16);
         aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(29), data.AsSpan(13, 16), plain);
         return Encoding.UTF8.GetString(plain);
      }
      finally { CryptographicOperations.ZeroMemory(plain); }
   }
}
