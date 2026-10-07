using Isopoh.Cryptography.Argon2;

namespace Em.Shared
{
   /// <summary>
   /// <see cref="IStringHasher"/> implementation using the Argon2 algorithm (through the
   /// <c>Isopoh.Cryptography.Argon2</c> library). Used for secure password/credential hashing.
   /// </summary>
   public class Argon2Hashing : IStringHasher
   {
      /// <summary>
      /// Produces the Argon2 hash of <paramref name="input"/>. The result already includes the salt and
      /// Argon2 parameters in one string, so it can be stored in the database directly.
      /// </summary>
      /// <param name="input">Plain text (e.g. a password) to hash.</param>
      /// <returns>Argon2 hash string.</returns>
      public string HashValue(string input) {
         return Argon2.Hash(input);
      }

      /// <summary>
      /// Compares plain text with a stored Argon2 hash, for verification (e.g. at sign-in).
      /// </summary>
      /// <param name="input">Plain text to verify.</param>
      /// <param name="hashValue">Previously stored Argon2 hash (from <see cref="HashValue"/>).</param>
      /// <returns><c>true</c> when <paramref name="input"/> matches <paramref name="hashValue"/>.</returns>
      public bool CompareHashValue(string input, string hashValue) {
         return Argon2.Verify(hashValue, input);
      }
   }
}
