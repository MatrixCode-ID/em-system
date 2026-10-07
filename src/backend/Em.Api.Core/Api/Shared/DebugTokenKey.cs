using Em.Shared;

namespace Em.Api.Shared
{
   /// <summary>
   /// One developer public key registered through
   /// <see cref="EmAppBuilder.AddDebugToken(string,string,int)"/>, together with the name and validity
   /// period of the tokens it signs. The key was already read and validated at registration, so this row
   /// is only used to verify - no re-parsing per request.
   /// </summary>
   /// <param name="Name">Name of the key, which the token names and which appears in the log.</param>
   /// <param name="Key">Its public key; never carries a private key.</param>
   /// <param name="Days">Validity period of the token in days since it was issued, or <c>-1</c> for no limit.</param>
   internal sealed record DebugTokenKey(string Name, RsaKeyPair Key, int Days)
   {
      /// <summary>
      /// <c>false</c> when this key was registered with no time limit, so the token's issue time need not be
      /// checked at all.
      /// </summary>
      public bool HasExpiry => Days >= 0;

      /// <summary>
      /// When a token issued at <paramref name="issuedAtUtc"/> stops being accepted.
      /// </summary>
      public DateTime ExpiresAtUtc(DateTime issuedAtUtc) => issuedAtUtc.AddDays(Days);
   }
}
