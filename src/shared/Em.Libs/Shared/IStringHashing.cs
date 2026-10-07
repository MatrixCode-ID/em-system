namespace Em.Shared
{
   /// <summary>
   /// Contract of a string hashing component (e.g. for passwords), so the hashing algorithm (Argon2, etc.)
   /// can be replaced without changing the calling code. See <see cref="Argon2Hashing"/> for the default
   /// implementation.
   /// </summary>
   public interface IStringHasher
   {
      /// <summary>
      /// Produces the hash of plain text <paramref name="input"/>.
      /// </summary>
      /// <param name="input">Plain text to hash.</param>
      /// <returns>Hash string that can be stored and later compared through <see cref="CompareHashValue"/>.</returns>
      string HashValue(string input);

      /// <summary>
      /// Compares plain text with a stored hash value.
      /// </summary>
      /// <param name="input">Plain text to verify.</param>
      /// <param name="hashValue">Stored hash value, produced by <see cref="HashValue"/>.</param>
      /// <returns><c>true</c> when <paramref name="input"/> matches <paramref name="hashValue"/>.</returns>
      bool CompareHashValue(string input, string hashValue);
   }
}
