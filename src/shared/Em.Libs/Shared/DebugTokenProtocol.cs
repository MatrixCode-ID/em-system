using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Shared
{
   /// <summary>
   /// Rules for building the debug token - the developer entry that requires neither a password nor an
   /// access token. The developer keeps a private key on their machine, signs a small payload, and puts the
   /// result in header <see cref="Defaults.DebugTokenHeader"/>; the server only stores the public key and
   /// verifies that signature on every request. Both sides use this class - the client builds the token,
   /// the server reads it - so the signed bytes and the verified bytes are exactly the same.
   /// </summary>
   /// <remarks>
   /// Token shape: <c>&lt;payload&gt;.&lt;signature&gt;</c>, both Base64Url.
   /// <para>
   /// What is signed are the ASCII bytes of the payload segment itself, not the raw JSON. That removes any
   /// JSON canonicalization concern: the signature proves exactly the byte sequence that appears in the
   /// header, so even the smallest difference in the payload makes verification fail.
   /// </para>
   /// <para>
   /// The key name is inside the payload, not outside it, so it is covered by the signature - outside,
   /// anyone could swap it. The server parses the payload first to know which public key to use, then
   /// verifies the signature with that key.
   /// </para>
   /// </remarks>
   public static class DebugTokenProtocol
   {
      /// <summary>
      /// Maximum token length that will be processed. A valid token is about 400 characters (an RSA-2048
      /// signature is 256 bytes), so this limit is generous; it stops callers who have proven nothing from
      /// forcing the server to decode a large blob.
      /// </summary>
      public const int MaxTokenLength = 2048;

      /// <summary>
      /// Builds a signed debug token.
      /// </summary>
      /// <param name="name">
      /// Key name; must exactly match the name registered on the server. The server uses this name to find
      /// the matching public key.
      /// </param>
      /// <param name="key">The developer's key pair; must carry the private key because it is used to sign.</param>
      /// <param name="issuedAtUtc">Token issue time in UTC, used by the server to compute its validity.</param>
      /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is empty.</exception>
      /// <exception cref="InvalidOperationException">Thrown when <paramref name="key"/> has no private key.</exception>
      public static string Create(string name, RsaKeyPair key, DateTime issuedAtUtc) {
         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Debug token key name must not be empty.", nameof(name));
         }

         var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
            new TokenPayload(name, new DateTimeOffset(issuedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds()));
         var payloadSegment = Base64Url.EncodeToString(payloadJson);
         var signature = key.SignData(Encoding.ASCII.GetBytes(payloadSegment));

         return $"{payloadSegment}.{Base64Url.EncodeToString(signature)}";
      }

      /// <summary>
      /// Splits a token into its parts and reads its payload. This method deliberately does not verify the
      /// signature: only the server knows which public key belongs to the name, so verification happens
      /// there with the <paramref name="signedPayload"/> and <paramref name="signature"/> returned here.
      /// </summary>
      /// <param name="token">Debug token header value as is.</param>
      /// <param name="name">Key name the token states; empty when the token cannot be read.</param>
      /// <param name="issuedAtUtc">Token issue time in UTC.</param>
      /// <param name="signedPayload">Bytes that were actually signed, ready for <c>RsaKeyPair.VerifyData</c>.</param>
      /// <param name="signature">Signature over <paramref name="signedPayload"/>.</param>
      /// <returns><c>true</c> when the token can be split and its payload read completely.</returns>
      public static bool TryRead(string token, out string name, out DateTime issuedAtUtc,
         out byte[] signedPayload, out byte[] signature) {
         name = string.Empty;
         issuedAtUtc = default;
         signedPayload = [];
         signature = [];

         if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) {
            return false;
         }

         var separator = token.IndexOf('.');
         if (separator <= 0 || separator == token.Length - 1) {
            return false;
         }

         var payloadSegment = token[..separator];
         var signatureSegment = token[(separator + 1)..];

         // One dot and no more. A third segment means this is not this token shape, and accepting it
         // silently would make the verified bytes differ from the signed bytes.
         if (signatureSegment.Contains('.')) {
            return false;
         }

         TokenPayload? payload;
         try {
            payload = JsonSerializer.Deserialize<TokenPayload>(Base64Url.DecodeFromChars(payloadSegment));
            signature = Base64Url.DecodeFromChars(signatureSegment);
         }
         catch (Exception x) when (x is FormatException or JsonException) {
            signature = [];
            return false;
         }

         if (payload is null || string.IsNullOrWhiteSpace(payload.Name)) {
            signature = [];
            return false;
         }

         try {
            issuedAtUtc = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAt).UtcDateTime;
         }
         catch (ArgumentOutOfRangeException) {
            signature = [];
            return false;
         }

         name = payload.Name;
         signedPayload = Encoding.ASCII.GetBytes(payloadSegment);
         return true;
      }

      private sealed record TokenPayload(
         [property: JsonPropertyName("name")] string Name,
         [property: JsonPropertyName("iat")] long IssuedAt);
   }
}
