using System.Data;
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
   public SmtpSettingsDetail Detail() => new() { Revision = Revision, Settings = Settings, HasPassword = Password is { Length: > 0 } };
}

internal interface ISmtpSettingsStore
{
   Task<SmtpSettingsDetail> ReadAsync(CancellationToken ct);
   Task<SmtpSettingsDetail> SaveAsync(SmtpSettingsSave request, CancellationToken ct);
   Task<(SmtpSettings Settings, string? Password)> CredentialsAsync(CancellationToken ct);
}

/// <summary>Database-wide document and AES-GCM key, committed atomically with revision checking.</summary>
internal sealed class SmtpSettingsStore(IDbContextFactory<ApiCoreContext> factory) : ISmtpSettingsStore
{
   internal const string SettingsKey = "Em.Smtp.Settings";
   internal const string SecretKey = "Em.Smtp.Key";

   private async Task<SmtpSettingsDocument> DocumentAsync(CancellationToken ct) {
      await using var db = await factory.CreateDbContextAsync(ct);
      var json = await db.ta_Metas.AsNoTracking().Where(m => m.cMetaKey == SettingsKey).Select(m => m.cMetaValue).SingleOrDefaultAsync(ct);
      return json is null ? new() : JsonSerializer.Deserialize<SmtpSettingsDocument>(json)
         ?? throw new InvalidDataException("Invalid SMTP settings document.");
   }
   public async Task<SmtpSettingsDetail> ReadAsync(CancellationToken ct) => (await DocumentAsync(ct)).Detail();

   public async Task<(SmtpSettings Settings, string? Password)> CredentialsAsync(CancellationToken ct) {
      var doc = await DocumentAsync(ct);
      if (doc.Password is not { Length: > 0 }) return (doc.Settings, null);
      await using var db = await factory.CreateDbContextAsync(ct);
      var value = await db.ta_Metas.AsNoTracking().Where(m => m.cMetaKey == SecretKey).Select(m => m.cMetaValue).SingleAsync(ct);
      return (doc.Settings, SmtpSecrets.Decrypt(Convert.FromBase64String(value), doc.Password));
   }

   public async Task<SmtpSettingsDetail> SaveAsync(SmtpSettingsSave request, CancellationToken ct) {
      if (request is null) throw SmtpValidation.Bad("SMTP settings update is required.");
      if (request.ClearPassword && request.Password is not null) throw SmtpValidation.Bad("Cannot replace and clear the SMTP password together.");
      if (request.Password is { Length: 0 or > 4096 }) throw SmtpValidation.Bad("SMTP password must contain between 1 and 4096 characters.");
      await using var db = await factory.CreateDbContextAsync(ct);
      try {
         await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
         var row = await db.ta_Metas.AsTracking().SingleOrDefaultAsync(m => m.cMetaKey == SettingsKey, ct);
         var doc = row is null ? new SmtpSettingsDocument() : JsonSerializer.Deserialize<SmtpSettingsDocument>(row.cMetaValue)
            ?? throw new InvalidDataException("Invalid SMTP settings document.");
         if (doc.Revision != request.ExpectedRevision) throw new ActionException("SMTP settings changed. Reload before saving.", 409);
         if (request.ClearPassword) doc.Password = null;
         SmtpValidation.Settings(request.Settings, request.Password is not null || doc.Password is { Length: > 0 });
         if (request.Password is not null) {
            var keyRow = await db.ta_Metas.AsTracking().SingleOrDefaultAsync(m => m.cMetaKey == SecretKey, ct);
            if (keyRow is null) {
               keyRow = new ta_Meta { cMetaKey = SecretKey, cMetaValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                  cMetaDescription = "SMTP AES-GCM key. Back up with settings; do not change.", ustamp = DateTime.UtcNow };
               db.ta_Metas.Add(keyRow);
            }
            doc.Password = SmtpSecrets.Encrypt(Convert.FromBase64String(keyRow.cMetaValue), request.Password);
         }
         doc.Settings = request.Settings;
         doc.Revision = checked(doc.Revision + 1);
         if (row is null) {
            row = new ta_Meta { cMetaKey = SettingsKey, cMetaDescription = "Database-wide SMTP settings; changes apply immediately." };
            db.ta_Metas.Add(row);
         }
         row.cMetaValue = JsonSerializer.Serialize(doc);
         row.ustamp = DateTime.UtcNow;
         await db.SaveChangesAsync(ct);
         await transaction.CommitAsync(ct);
         return doc.Detail();
      }
      catch (Exception ex) when (IsConflict(ex)) {
         throw new ActionException("Concurrent SMTP settings update. Reload before saving.", 409);
      }
   }

   private static bool IsConflict(Exception ex) {
      for (Exception? error = ex; error is not null; error = error.InnerException) {
         if (error is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 1205 or 2601 or 2627) return true;
         if (error is Npgsql.PostgresException pg && pg.SqlState is "40001" or "40P01" or "23505") return true;
         if (error is MySqlConnector.MySqlException mysql && mysql.Number is 1213 or 1062) return true;
      }
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
