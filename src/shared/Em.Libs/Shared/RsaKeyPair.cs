using System.Security.Cryptography;

namespace Em.Shared
{
   /// <summary>
   /// RSA public/private key pair, used by <c>ServicesBase.GetServerRsaKeyAsync</c>. Generic for two
   /// modes: a full pair (server, has a <see cref="PrivateKey"/>) or public key only (client/UI,
   /// <see cref="PrivateKey"/> is null because the private key is never sent to the client).
   /// </summary>
   public class RsaKeyPair
   {
      /// <summary>
      /// Standard padding for RSA encryption/decryption across the application. Uses OAEP SHA-256, not
      /// PKCS#1 v1.5, because PKCS#1 v1.5 is vulnerable to padding oracles (Bleichenbacher attack) when the
      /// decrypting party leaks the difference between "valid padding" and "invalid padding". Both sides
      /// (server and client) must use the same value.
      /// </summary>
      public static RSAEncryptionPadding DefaultPadding => RSAEncryptionPadding.OaepSHA256;

      /// <summary>
      /// Standard padding for RSA digital signatures across the application. Uses PSS, which is stronger
      /// than PKCS#1 v1.5. Both sides (signer and verifier) must use the same value.
      /// </summary>
      public static RSASignaturePadding DefaultSignaturePadding => RSASignaturePadding.Pss;

      /// <summary>
      /// Standard hash algorithm used together with <see cref="DefaultSignaturePadding"/> when signing and
      /// verifying. Both sides must use the same value.
      /// </summary>
      public static HashAlgorithmName DefaultHashAlgorithm => HashAlgorithmName.SHA256;

      /// <summary>
      /// Creates an instance carrying only the public key, for client-side use (encryption and signature
      /// verification only, without decrypting or signing).
      /// </summary>
      /// <param name="publicKey">Public key, as Base64 of DER PKCS#1 (<c>RSA.ExportRSAPublicKey</c>).</param>
      public static RsaKeyPair Create(string publicKey) => new RsaKeyPair(publicKey);

      /// <summary>
      /// Same as <see cref="Create(string)"/>, but takes the public key as raw DER PKCS#1 bytes so the
      /// caller need not Base64-encode it.
      /// </summary>
      /// <param name="publicKey">Public key as raw DER PKCS#1 bytes.</param>
      public static RsaKeyPair Create(byte[] publicKey) => Create(Convert.ToBase64String(publicKey));

      /// <summary>Creates a key pair from Base64 keys.</summary>
      /// <param name="publicKey">Public key, as Base64 of DER PKCS#1 (<c>RSA.ExportRSAPublicKey</c>).</param>
      /// <param name="privateKey">
      /// Private key, as Base64 of DER PKCS#1 (<c>RSA.ExportRSAPrivateKey</c>). Optional - null means this
      /// instance only carries the public key (e.g. used in the UI for encryption only, without decrypting).
      /// </param>
      public RsaKeyPair(string publicKey, string? privateKey = null) {
         PublicKey = publicKey;
         PrivateKey = privateKey;
      }

      /// <summary>
      /// Public key as Base64 of DER PKCS#1. Safe to send to the client or keep in configuration.
      /// </summary>
      public string PublicKey { get; init; }

      /// <summary>
      /// Private key as Base64 of DER PKCS#1, or <c>null</c> when this instance only carries the public key.
      /// This value is secret and must never leave the server.
      /// </summary>
      public string? PrivateKey { get; init; }

      /// <summary>
      /// True when this instance has a private key (full pair mode), false when it only has the public key.
      /// </summary>
      public bool HasPrivateKey => PrivateKey is not null;

      /// <summary>
      /// Returns <see cref="PublicKey"/> as raw DER PKCS#1 bytes, ready for <c>RSA.ImportRSAPublicKey</c>.
      /// </summary>
      public byte[] GetPublicBytes() => Convert.FromBase64String(PublicKey);

      /// <summary>
      /// Returns <see cref="PrivateKey"/> as raw DER PKCS#1 bytes.
      /// </summary>
      /// <exception cref="InvalidOperationException">Thrown when this instance only carries the public key.</exception>
      public byte[] GetPrivateBytes() {
         if (PrivateKey is null) {
            throw new InvalidOperationException("This RsaKeyPair only has a public key; no private key is set.");
         }
         return Convert.FromBase64String(PrivateKey);
      }

      /// <summary>
      /// Creates a new <see cref="RSA"/> instance loaded with this pair's key: the private key when present
      /// (so it can decrypt and sign), or only the public key otherwise. The caller is responsible for
      /// disposing the result.
      /// </summary>
      public RSA CreateRsa() {
         var rsa = RSA.Create();
         if (HasPrivateKey) {
            // PKCS#1 RSAPrivateKey already contains the public components, so no separate import is needed.
            rsa.ImportRSAPrivateKey(GetPrivateBytes(), out _);
         }
         else {
            rsa.ImportRSAPublicKey(GetPublicBytes(), out _);
         }
         return rsa;
      }

      /// <summary>
      /// Encrypts data with the public key using <see cref="DefaultPadding"/>. The data size is limited by
      /// the key size (for a 2048-bit key with OAEP SHA-256: at most 190 bytes).
      /// </summary>
      public byte[] EncryptValue(ReadOnlySpan<byte> data) {
         using var rsa = CreateRsa();
         return rsa.Encrypt(data, DefaultPadding);
      }

      /// <summary>
      /// Decrypts data using <see cref="DefaultPadding"/>. Requires the private key.
      /// </summary>
      /// <remarks>
      /// Be careful exposing this method through an endpoint anyone can reach: an endpoint that decrypts
      /// arbitrary ciphertext from outside and leaks the result (or even only success/failure) works as a
      /// decryption oracle. To prove key ownership use <see cref="SignData"/>/<see cref="VerifyData"/>, not
      /// decryption.
      /// </remarks>
      public byte[] DecryptValue(ReadOnlySpan<byte> data) {
         using var rsa = CreateRsa();
         return rsa.Decrypt(data, DefaultPadding);
      }

      /// <summary>
      /// Signs data with the private key using <see cref="DefaultHashAlgorithm"/> and
      /// <see cref="DefaultSignaturePadding"/>. There is no data size limit because what is signed is its
      /// hash.
      /// </summary>
      /// <exception cref="InvalidOperationException">Thrown when this instance has no private key.</exception>
      public byte[] SignData(ReadOnlySpan<byte> data) {
         if (!HasPrivateKey) {
            throw new InvalidOperationException("Signing requires a private key; this RsaKeyPair only has a public key.");
         }
         using var rsa = CreateRsa();
         return rsa.SignData(data, DefaultHashAlgorithm, DefaultSignaturePadding);
      }

      /// <summary>
      /// Verifies that <paramref name="signature"/> really is a signature over <paramref name="data"/> by
      /// the holder of the private key of <see cref="PublicKey"/>. The public key alone is enough, so it can
      /// be called on the client side.
      /// </summary>
      /// <returns><c>true</c> when the signature is valid, <c>false</c> otherwise.</returns>
      public bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) {
         using var rsa = CreateRsa();
         return rsa.VerifyData(data, signature, DefaultHashAlgorithm, DefaultSignaturePadding);
      }
   }
}
