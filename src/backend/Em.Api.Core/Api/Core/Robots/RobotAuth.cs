using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core
{
   /// <summary>
   /// Token robot dan autentikasi Basic untuk jalur <c>/v2</c>. Token berentropi tinggi (256 bit acak),
   /// jadi yang disimpan cukup hash SHA-256: verifikasinya satu pencarian indeks, tanpa KDF lambat per
   /// request. Username Docker dicocokkan dengan nama robot sesudah token ditemukan.
   /// </summary>
   public static class RobotAuth
   {
      private const string TokenPrefix = "emc_";

      // Berapa karakter awal token yang disimpan terang-terangan untuk mengenali token di layar manajemen.
      private const int DisplayPrefixLength = 10;

      // LastUsed tidak ditulis di setiap request: paling sering sekali per jeda ini per robot.
      private static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(5);

      private static readonly ConcurrentDictionary<string, DateTime> LastUsedWritten = new();

      public static string GenerateToken() =>
         TokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

      public static string DisplayPrefix(string token) => token[..Math.Min(DisplayPrefixLength, token.Length)];

      public static string HashToken(string token) =>
         Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

      private static string Base64UrlEncode(byte[] bytes) =>
         Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

      /// <summary>
      /// Robot yang membuktikan dirinya lewat header Basic, atau <c>null</c> kalau header tidak ada,
      /// salah, robotnya nonaktif, atau tokennya kedaluwarsa. Pemanggil menjawab 401 untuk <c>null</c>.
      /// </summary>
      public static async Task<ta_Robot?> AuthenticateAsync(HttpContext ctx, RobotContext db) {
         if (!TryReadBasic(ctx.Request.Headers.Authorization.ToString(), out var user, out var password)) {
            return null;
         }

         return await AuthenticateTokenCoreAsync(password, db, ctx.RequestAborted, user);
      }

      public static Task<ta_Robot?> AuthenticateAsync(string token, RobotContext db, CancellationToken ct) =>
         AuthenticateTokenCoreAsync(token, db, ct, null);

      private static async Task<ta_Robot?> AuthenticateTokenCoreAsync(string token, RobotContext db, CancellationToken ct, string? user) {
         if (token.Length > 256 || !token.StartsWith(TokenPrefix, StringComparison.Ordinal)) return null;
         var hash = HashToken(token);
         var robot = await db.Robots.SingleOrDefaultAsync(r => r.cRobotTokenHash == hash, ct);
         if (robot is null ||
             robot.cRobotState != 1 ||
             (user is not null && !string.Equals(robot.cRobotName, user, StringComparison.Ordinal))) {
            return null;
         }

         var now = DateTime.UtcNow;
         if (robot.cRobotTokenExpiry is { } expiry && expiry <= now) {
            return null;
         }

         if (!LastUsedWritten.TryGetValue(robot.cRobotId, out var written) || now - written >= LastUsedInterval) {
            LastUsedWritten[robot.cRobotId] = now;
            await db.Robots.Where(r => r.cRobotId == robot.cRobotId)
               .ExecuteUpdateAsync(s => s.SetProperty(r => r.cRobotTokenLastUsed, now), ct);
         }

         return robot;
      }

      private static bool TryReadBasic(string header, out string user, out string password) {
         user = password = string.Empty;
         const string scheme = "Basic ";
         if (header.Length > 1024 || !header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return false;

         string decoded;
         try {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[scheme.Length..].Trim()));
         } catch (FormatException) {
            return false;
         }

         var colon = decoded.IndexOf(':');
         if (colon <= 0 || colon == decoded.Length - 1) return false;

         user = decoded[..colon];
         password = decoded[(colon + 1)..];
         return true;
      }
   }
}
