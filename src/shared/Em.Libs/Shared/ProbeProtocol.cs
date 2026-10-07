using System.Buffers.Text;

namespace Em.Shared
{
   /// <summary>
   /// Rules for building the connection probe payload (action <c>Core/Handshake</c>): the client sends a
   /// random nonce, the server signs it, and the client verifies the signature with the public key the
   /// server returns. Both sides use this class so the signed bytes and the verified bytes are exactly the
   /// same.
   /// </summary>
   /// <remarks>
   /// The signed payload deliberately carries a domain prefix (<c>em.probe.v1:</c>) rather than the raw
   /// nonce. This limits what the signature means: the server only ever signs bytes starting with this
   /// prefix, so a probe signature cannot be reused as a signature over another protocol message. As a
   /// consequence, any other payload the server signs later must use a different domain prefix.
   /// </remarks>
   public static class ProbeProtocol
   {
      /// <summary>
      /// Minimum nonce length in bytes. A nonce that is too short makes the signature guessable/replayable.
      /// </summary>
      public const int MinNonceLength = 16;

      /// <summary>
      /// Maximum nonce length in bytes. Limited so unauthenticated callers cannot force the server to sign a
      /// large blob.
      /// </summary>
      public const int MaxNonceLength = 64;

      /// <summary>
      /// Nonce length used by the client when starting a probe.
      /// </summary>
      public const int DefaultNonceLength = 32;

      private static readonly byte[] Domain = "em.probe.v1:"u8.ToArray();

      /// <summary>
      /// Builds the bytes the server actually signs: the domain prefix followed by the nonce.
      /// </summary>
      /// <param name="nonce">Random nonce from the client.</param>
      public static byte[] BuildSignaturePayload(ReadOnlySpan<byte> nonce) => [..Domain, ..nonce];

      /// <summary>
      /// Encodes a nonce as a Base64Url string so it is safe as a query string value.
      /// </summary>
      public static string EncodeNonce(ReadOnlySpan<byte> nonce) => Base64Url.EncodeToString(nonce);

      /// <summary>
      /// Decodes a nonce from a Base64Url string and validates its length against
      /// <see cref="MinNonceLength"/> and <see cref="MaxNonceLength"/>.
      /// </summary>
      /// <param name="value">Base64Url-encoded nonce.</param>
      /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is not valid Base64Url.</exception>
      /// <exception cref="ArgumentOutOfRangeException">Thrown when the nonce length is outside the allowed range.</exception>
      public static byte[] DecodeNonce(string value) {
         byte[] nonce;
         try {
            nonce = Base64Url.DecodeFromChars(value);
         }
         catch (FormatException x) {
            throw new ArgumentException("Nonce is not valid Base64Url text.", nameof(value), x);
         }

         if (nonce.Length is < MinNonceLength or > MaxNonceLength) {
            throw new ArgumentOutOfRangeException(nameof(value), nonce.Length,
               $"Nonce must be between {MinNonceLength} and {MaxNonceLength} bytes.");
         }

         return nonce;
      }
   }
}
