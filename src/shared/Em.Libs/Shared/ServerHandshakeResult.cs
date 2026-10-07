namespace Em.Shared
{
   /// <summary>
   /// Result of action <c>Core/Handshake</c>: the server's public key plus proof that the server holds the
   /// matching private key, namely a signature over the nonce the client sent. Shared by the backend
   /// (building the response) and the UI (reading it), so the property names must stay the same on both
   /// sides because <c>Defaults.ResponseJsonOptions</c> is deliberately case-sensitive.
   /// </summary>
   public class ServerHandshakeResult
   {
      /// <summary>
      /// Server RSA public key, as Base64 of DER PKCS#1. Do not trust it before <see cref="Signature"/> has
      /// been verified against the nonce that was sent.
      /// </summary>
      public string PublicKey { get; set; } = string.Empty;

      /// <summary>
      /// Server signature (Base64Url) over the payload built by <c>ProbeProtocol.BuildSignaturePayload</c>,
      /// i.e. the domain prefix followed by the client's nonce.
      /// </summary>
      public string Signature { get; set; } = string.Empty;

      /// <summary>
      /// Size of the server RSA key in bits, for diagnostics on the client side.
      /// </summary>
      public int KeySize { get; set; }
   }
}
