using System.Security.Cryptography;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>The result of checking the signature of <c>release.json</c>.</summary>
   public enum ReleaseSignatureStatus
   {
      /// <summary>The signature is valid and was made by one of the trusted public keys.</summary>
      Valid = 0,

      /// <summary>The <c>keyId</c> in the signature file does not match any trusted public key.</summary>
      UnknownKey = 1,

      /// <summary>The key is known, but the signature does not match the manifest bytes (or is corrupt).</summary>
      Invalid = 2
   }

   /// <summary>
   /// Signs and verifies the <c>release.json</c> bytes with ECDSA P-256 + SHA-256, in the IEEE P1363
   /// signature format (64 bytes), and computes the <c>keyId</c> and PEM form of a public key. The full
   /// rules are in <c>doc/release-format.md</c> sections 4 and 5.
   /// </summary>
   public static class ReleaseSignature
   {
      /// <summary>OID kurva P-256 (secp256r1 / prime256v1).</summary>
      public const string P256Oid = "1.2.840.10045.3.1.7";

      private const int SignatureLength = 64;

      /// <summary>
      /// The <c>keyId</c> of a public key: the first 16 lowercase hex characters of the SHA-256 of its DER
      /// SubjectPublicKeyInfo.
      /// </summary>
      public static string KeyIdOf(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
         Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo))[..16];

      /// <summary>
      /// Signs the <c>release.json</c> bytes as-is with <paramref name="privateKey"/>.
      /// </summary>
      /// <param name="manifestBytes">The <c>release.json</c> bytes exactly as they will be written.</param>
      /// <param name="privateKey">The ECDSA P-256 private key.</param>
      /// <exception cref="CryptographicException">The key is not P-256.</exception>
      public static ReleaseSignatureFile Sign(ReadOnlySpan<byte> manifestBytes, ECDsa privateKey) {
         RequireP256(privateKey);
         var signature = privateKey.SignData(manifestBytes, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
         return new ReleaseSignatureFile {
            KeyId = KeyIdOf(privateKey.ExportSubjectPublicKeyInfo()),
            Signature = Convert.ToBase64String(signature)
         };
      }

      /// <summary>
      /// <c>true</c> when <paramref name="signature"/> is a valid signature over
      /// <paramref name="manifestBytes"/> by one of <paramref name="trustedPublicKeys"/>.
      /// </summary>
      /// <param name="manifestBytes">The <c>release.json</c> bytes exactly as downloaded.</param>
      /// <param name="signature">The content of <c>release.json.sig</c>.</param>
      /// <param name="trustedPublicKeys">The DER SubjectPublicKeyInfo of every trusted public key.</param>
      public static bool Verify(ReadOnlySpan<byte> manifestBytes, ReleaseSignatureFile signature,
         IEnumerable<ReadOnlyMemory<byte>> trustedPublicKeys) =>
         Check(manifestBytes, signature, trustedPublicKeys) == ReleaseSignatureStatus.Valid;

      /// <summary>
      /// Like <see cref="Verify"/>, but states why the signature was refused: the key is unknown, or the
      /// signature does not match.
      /// </summary>
      public static ReleaseSignatureStatus Check(ReadOnlySpan<byte> manifestBytes, ReleaseSignatureFile signature,
         IEnumerable<ReadOnlyMemory<byte>> trustedPublicKeys) {
         var key = trustedPublicKeys.FirstOrDefault(r =>
            string.Equals(KeyIdOf(r.Span), signature.KeyId, StringComparison.Ordinal));
         if (key.IsEmpty) return ReleaseSignatureStatus.UnknownKey;

         var bytes = new byte[SignatureLength];
         if (!Convert.TryFromBase64String(signature.Signature, bytes, out var written) || written != SignatureLength)
            return ReleaseSignatureStatus.Invalid;

         try {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key.Span, out _);
            RequireP256(ecdsa);
            return ecdsa.VerifyData(manifestBytes, bytes, HashAlgorithmName.SHA256,
               DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
               ? ReleaseSignatureStatus.Valid
               : ReleaseSignatureStatus.Invalid;
         }
         catch (CryptographicException) {
            return ReleaseSignatureStatus.Invalid;
         }
      }

      /// <summary>
      /// The PEM form (<c>-----BEGIN PUBLIC KEY-----</c>) of a public key, as embedded in the launcher.
      /// </summary>
      public static string ToPem(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
         new string(PemEncoding.Write("PUBLIC KEY", subjectPublicKeyInfo)).ReplaceLineEndings("\n") + "\n";

      /// <summary><c>true</c> when <paramref name="key"/> is on the P-256 curve.</summary>
      public static bool IsP256(ECDsa key) {
         if (key.KeySize != 256) return false;
         try {
            // A CNG key may report its curve by friendly name alone, with no OID value.
            var oid = key.ExportParameters(false).Curve.Oid;
            return oid?.Value == P256Oid ||
                   oid?.FriendlyName is "nistP256" or "ECDSA_P256" or "secP256r1" or "prime256v1";
         }
         catch (CryptographicException) {
            return false;
         }
      }

      private static void RequireP256(ECDsa key) {
         if (!IsP256(key)) throw new CryptographicException("The key is not an ECDSA P-256 key.");
      }
   }
}
