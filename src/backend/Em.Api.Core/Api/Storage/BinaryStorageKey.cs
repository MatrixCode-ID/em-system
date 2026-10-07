using System.Text;

namespace Em.Api.Core.Storage
{
   /// <summary>
   /// Shape rules for <see cref="IBinaryStorage"/> keys, together with how to compose and normalize them.
   /// Used by every storage implementation, so a key that is valid in one implementation is also valid in
   /// another - including one that stores its content off this machine.
   /// </summary>
   /// <remarks>
   /// A key consists of several parts separated by slashes, like a path. What is forbidden is everything
   /// that would let a key point to a place other than what it reads: empty parts, parts that are only a
   /// dot, backslashes, and unreadable characters. This restriction is stricter than what a file system
   /// requires, because the same key must remain valid when the storage later moves to a network.
   /// </remarks>
   public static class BinaryStorageKey
   {
      /// <summary>Maximum length of a key, counted in characters.</summary>
      public const int MaxLength = 1024;

      /// <summary>Maximum length of one key part.</summary>
      public const int MaxSegmentLength = 255;

      /// <summary>Separator between key parts.</summary>
      public const char Separator = '/';

      /// <summary>
      /// Normalizes a key: double slashes are collapsed, leading and trailing slashes are removed, then the
      /// result is checked against the key shape rules.
      /// </summary>
      /// <param name="key">The key being normalized.</param>
      /// <returns>The key in its canonical form.</returns>
      /// <exception cref="ArgumentException">
      /// Thrown when the key is empty, too long, contains an invalid part, or contains a disallowed
      /// character. Its message names which part refused it.
      /// </exception>
      public static string Normalize(string key) {
         if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A storage key must not be empty.", nameof(key));
         }

         if (key.Contains('\\')) {
            throw new ArgumentException(
               $"Storage key '{key}' contains a backslash; the only separator allowed is '{Separator}'.", nameof(key));
         }

         var segments = key.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
         if (segments.Length == 0) {
            throw new ArgumentException($"Storage key '{key}' has no usable part.", nameof(key));
         }

         foreach (var segment in segments) {
            EnsureValidSegment(key, segment);
         }

         var normalized = string.Join(Separator, segments);
         if (normalized.Length > MaxLength) {
            throw new ArgumentException(
               $"Storage key '{normalized}' is {normalized.Length} characters, which exceeds the maximum of {MaxLength}.",
               nameof(key));
         }

         return normalized;
      }

      /// <summary>
      /// Composes a key from several parts, then normalizes it. Empty parts are skipped, so callers do not
      /// need to deal with the slashes at the joints.
      /// </summary>
      /// <param name="parts">The parts of the key, in order.</param>
      /// <returns>The key in its canonical form.</returns>
      /// <exception cref="ArgumentException">Thrown for the same reasons as <see cref="Normalize"/>.</exception>
      public static string Combine(params string?[] parts) {
         ArgumentNullException.ThrowIfNull(parts);
         var builder = new StringBuilder();

         foreach (var part in parts) {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (builder.Length > 0) builder.Append(Separator);
            builder.Append(part);
         }

         return Normalize(builder.ToString());
      }

      /// <summary>Whether a key is valid according to the key shape rules.</summary>
      /// <param name="key">The key being checked.</param>
      public static bool IsValid(string? key) {
         if (key is null) return false;

         try {
            Normalize(key);
            return true;
         }
         catch (ArgumentException) {
            return false;
         }
      }

      private static void EnsureValidSegment(string key, string segment) {
         if (segment.Length > MaxSegmentLength) {
            throw new ArgumentException(
               $"Storage key '{key}' has a part of {segment.Length} characters, which exceeds the maximum of {MaxSegmentLength}.",
               nameof(key));
         }

         // A part made of dots only is what lets a key point outside the place it reads as,
         // so it is refused here rather than resolved away later.
         if (segment.All(r => r == '.')) {
            throw new ArgumentException(
               $"Storage key '{key}' has a part made of dots only ('{segment}'), which is not a name.", nameof(key));
         }

         foreach (var c in segment) {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') continue;

            throw new ArgumentException(
               $"Storage key '{key}' contains the character '{c}', which is not allowed. " +
               "A key part may only contain ASCII letters, digits, '-', '_' and '.'.", nameof(key));
         }
      }
   }
}
