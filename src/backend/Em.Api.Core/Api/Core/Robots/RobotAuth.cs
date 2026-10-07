using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Em.Api.Core
{
   /// <summary>
   /// Robot token and Basic authentication for the <c>/v2</c> path. The token has high entropy (256 random
   /// bits), so only a SHA-256 hash is stored: verification is a single index lookup, without a slow KDF on
   /// every request. The Docker username is matched against the robot name after the token is found.
   /// </summary>
   public static class RobotAuth
   {
      private const string TokenPrefix = "emc_";

      // How many leading characters of the token are stored in the clear to recognize a token on the
      // management screen.
      private const int DisplayPrefixLength = 10;

      // LastUsed is not written on every request: at most once per this interval per robot.
      private static readonly TimeSpan LastUsedInterval = TimeSpan.FromMinutes(5);

      private static readonly ConcurrentDictionary<string, DateTime> LastUsedWritten = new();

      /// <summary>Generates a new random robot token.</summary>
      public static string GenerateToken() =>
         TokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

      /// <summary>The leading part of a token that is stored in the clear, used to recognize it on screen.</summary>
      public static string DisplayPrefix(string token) => token[..Math.Min(DisplayPrefixLength, token.Length)];

      /// <summary>Computes the SHA-256 hash of a token, which is what is stored.</summary>
      public static string HashToken(string token) =>
         Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

      private static string Base64UrlEncode(byte[] bytes) =>
         Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

      /// <summary>
      /// The robot that proves itself through the Basic header, or <c>null</c> when the header is missing,
      /// wrong, the robot is inactive, or its token has expired. The caller answers 401 for <c>null</c>.
      /// </summary>
      public static async Task<ta_Robot?> AuthenticateAsync(HttpContext ctx, RobotContext db) {
         if (!TryReadBasic(ctx.Request.Headers.Authorization.ToString(), out var user, out var password)) {
            return null;
         }

         return await AuthenticateTokenCoreAsync(password, db, ctx.RequestAborted, user);
      }

      /// <summary>Finds the active robot that owns a token, or <c>null</c>.</summary>
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
