using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core.Storage;

internal interface IStorageSettingsPersistence
{
   string Description { get; }
   string? Read();
   void Write(long expectedRevision, string json);
}

/// <summary>Atomic document in ta_Meta. Serializable transaction plus expected revision protects cross-process writers.</summary>
internal sealed class MetaStorageSettingsPersistence(string key, Func<ApiCoreContext> createContext) : IStorageSettingsPersistence
{
   public string Description => "ta_Meta:" + key;
   internal static string CreateHostKey(string hostId, string machine, string contentRoot) {
      var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));
      if (OperatingSystem.IsWindows()) root = root.ToUpperInvariant();
      return "Em.StorageSettings:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(machine + "\n" + root + "\n" + hostId)));
   }
   public string? Read() {
      using var db = createContext();
      return db.ta_Metas.AsNoTracking().SingleOrDefault(m => m.cMetaKey == key)?.cMetaValue;
   }
   public void Write(long expectedRevision, string json) {
      try { WriteCore(expectedRevision, json); }
      catch (Exception ex) when (IsWriteConflict(ex)) {
         throw new ActionException("Concurrent storage settings update. Reload before saving.", 409);
      }
   }
   private static bool IsWriteConflict(Exception exception) {
      for (Exception? ex = exception; ex is not null; ex = ex.InnerException) {
         if (ex is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 1205 or 2601 or 2627) return true;
         if (ex is Npgsql.PostgresException pg && pg.SqlState is "40001" or "40P01" or "23505") return true;
         if (ex is MySqlConnector.MySqlException mysql && mysql.Number is 1213 or 1062) return true;
      }
      return false;
   }
   private void WriteCore(long expectedRevision, string json) {
      using var db = createContext();
      using var transaction = db.Database.BeginTransaction(IsolationLevel.Serializable);
      var row = db.ta_Metas.AsTracking().SingleOrDefault(m => m.cMetaKey == key);
      var current = row is null ? 0 : JsonSerializer.Deserialize<StorageSettingsDocument>(row.cMetaValue)?.Revision
         ?? throw new InvalidDataException("Invalid persisted storage settings.");
      if (current != expectedRevision) throw new ActionException("Storage settings changed in ta_Meta. Reload before saving.", 409);
      if (row is null) {
         row = new ta_Meta { cMetaKey = key, cMetaDescription = "Per-host CDN/registry settings; applied on API startup." };
         db.ta_Metas.Add(row);
      }
      row.cMetaValue = json;
      row.ustamp = DateTime.UtcNow;
      db.SaveChanges();
      transaction.Commit();
   }
}
