using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;

namespace Em.Api.Core.Registry.Deploy
{
   /// <summary>
   /// Encrypts deploy credentials (SSH key/password, passphrase, Portainer token, registry login) with
   /// AES-256-GCM. The 32-byte key is created on first use and stored Base64 in <c>ta_Meta</c> under
   /// <see cref="MetaKey"/>, so anyone who can read the database can also read the credentials; this was
   /// accepted as the trade-off for not needing a key file next to every API instance.
   /// </summary>
   /// <remarks>Stored format: <c>version(1)=1 | nonce(12) | tag(16) | ciphertext</c>.</remarks>
   internal sealed class CtnDeploySecrets
   {
      internal const string MetaKey = "Ctn.Deploy.Key";
      private const byte Version = 1;
      private const int NonceSize = 12;
      private const int TagSize = 16;

      private readonly Func<CancellationToken, Task<byte[]>> _loadKey;
      private byte[]? _key;

      public CtnDeploySecrets(ApiCoreContext db) : this(ct => LoadOrCreateKeyAsync(db, ct)) {
      }

      /// <summary>Uses a fixed key; for tests.</summary>
      internal CtnDeploySecrets(byte[] key) : this(_ => Task.FromResult(key)) {
      }

      private CtnDeploySecrets(Func<CancellationToken, Task<byte[]>> loadKey) {
         _loadKey = loadKey;
      }

      /// <summary>Encrypts <paramref name="plain"/>; <c>null</c> or empty gives <c>null</c>.</summary>
      public async Task<byte[]?> ProtectAsync(string? plain, CancellationToken ct = default) =>
         string.IsNullOrEmpty(plain) ? null : Encrypt(await KeyAsync(ct), plain);

      /// <summary>Decrypts a stored value; <c>null</c> gives <c>null</c>.</summary>
      /// <exception cref="CryptographicException">The value was changed or encrypted with another key.</exception>
      public async Task<string?> UnprotectAsync(byte[]? data, CancellationToken ct = default) =>
         data is null || data.Length == 0 ? null : Decrypt(await KeyAsync(ct), data);

      private async Task<byte[]> KeyAsync(CancellationToken ct) => _key ??= await _loadKey(ct);

      internal static byte[] Encrypt(byte[] key, string plain) {
         var data = Encoding.UTF8.GetBytes(plain);
         var result = new byte[1 + NonceSize + TagSize + data.Length];
         result[0] = Version;
         var nonce = result.AsSpan(1, NonceSize);
         RandomNumberGenerator.Fill(nonce);
         using var aes = new AesGcm(key, TagSize);
         aes.Encrypt(nonce, data, result.AsSpan(1 + NonceSize + TagSize), result.AsSpan(1 + NonceSize, TagSize));
         return result;
      }

      internal static string Decrypt(byte[] key, byte[] stored) {
         if (stored.Length < 1 + NonceSize + TagSize || stored[0] != Version) {
            throw new CryptographicException("Unsupported deploy secret format.");
         }

         var cipher = stored.AsSpan(1 + NonceSize + TagSize);
         var plain = new byte[cipher.Length];
         using var aes = new AesGcm(key, TagSize);
         aes.Decrypt(stored.AsSpan(1, NonceSize), cipher, stored.AsSpan(1 + NonceSize, TagSize), plain);
         return Encoding.UTF8.GetString(plain);
      }

      private static async Task<byte[]> LoadOrCreateKeyAsync(ApiCoreContext db, CancellationToken ct) {
         if (await ReadKeyAsync(db, ct) is { } existing) return existing;

         var key = RandomNumberGenerator.GetBytes(32);
         db.ta_Metas.Add(new ta_Meta {
            cMetaKey = MetaKey, cMetaValue = Convert.ToBase64String(key),
            cMetaDescription = "Key that encrypts container deploy credentials. Do not change: stored credentials become unreadable.",
            ustamp = DateTime.UtcNow
         });
         try {
            await db.SaveChangesAsync(ct);
            return key;
         }
         catch (DbUpdateException) {
            // Another request created the key first; use that one.
            db.ChangeTracker.Clear();
            return await ReadKeyAsync(db, ct) ?? throw new InvalidOperationException("The deploy key could not be created.");
         }
      }

      private static async Task<byte[]?> ReadKeyAsync(ApiCoreContext db, CancellationToken ct) {
         var value = await db.ta_Metas.Where(m => m.cMetaKey == MetaKey).Select(m => m.cMetaValue).SingleOrDefaultAsync(ct);
         if (string.IsNullOrWhiteSpace(value)) return null;

         var key = Convert.FromBase64String(value);
         return key.Length == 32 ? key : throw new InvalidOperationException($"ta_Meta '{MetaKey}' does not hold a 32-byte key.");
      }
   }
}
