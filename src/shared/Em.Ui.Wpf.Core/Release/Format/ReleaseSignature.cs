using System.Security.Cryptography;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Hasil pemeriksaan tanda tangan <c>release.json</c>.</summary>
   public enum ReleaseSignatureStatus
   {
      /// <summary>Tanda tangan sah dan dibuat oleh salah satu public key yang dipercaya.</summary>
      Valid = 0,

      /// <summary><c>keyId</c> di file tanda tangan tidak cocok dengan public key mana pun yang dipercaya.</summary>
      UnknownKey = 1,

      /// <summary>Key-nya dikenal, tetapi tanda tangannya tidak cocok dengan byte manifest (atau rusak).</summary>
      Invalid = 2
   }

   /// <summary>
   /// Menandatangani dan memverifikasi byte <c>release.json</c> dengan ECDSA P-256 + SHA-256, format
   /// tanda tangan IEEE P1363 (64 byte), serta menghitung <c>keyId</c> dan bentuk PEM sebuah public key.
   /// Aturan lengkapnya ada di <c>doc/release-format.md</c> bagian 4 dan 5.
   /// </summary>
   public static class ReleaseSignature
   {
      /// <summary>OID kurva P-256 (secp256r1 / prime256v1).</summary>
      public const string P256Oid = "1.2.840.10045.3.1.7";

      private const int SignatureLength = 64;

      /// <summary>
      /// <c>keyId</c> sebuah public key: 16 karakter hex huruf kecil pertama SHA-256 dari DER
      /// SubjectPublicKeyInfo-nya.
      /// </summary>
      public static string KeyIdOf(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
         Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo))[..16];

      /// <summary>
      /// Menandatangani byte <c>release.json</c> apa adanya dengan <paramref name="privateKey"/>.
      /// </summary>
      /// <param name="manifestBytes">Byte <c>release.json</c> persis seperti yang akan ditulis.</param>
      /// <param name="privateKey">Private key ECDSA P-256.</param>
      /// <exception cref="CryptographicException">Key-nya bukan P-256.</exception>
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
      /// <c>true</c> kalau <paramref name="signature"/> adalah tanda tangan sah atas
      /// <paramref name="manifestBytes"/> oleh salah satu <paramref name="trustedPublicKeys"/>.
      /// </summary>
      /// <param name="manifestBytes">Byte <c>release.json</c> persis seperti yang diunduh.</param>
      /// <param name="signature">Isi <c>release.json.sig</c>.</param>
      /// <param name="trustedPublicKeys">DER SubjectPublicKeyInfo setiap public key yang dipercaya.</param>
      public static bool Verify(ReadOnlySpan<byte> manifestBytes, ReleaseSignatureFile signature,
         IEnumerable<ReadOnlyMemory<byte>> trustedPublicKeys) =>
         Check(manifestBytes, signature, trustedPublicKeys) == ReleaseSignatureStatus.Valid;

      /// <summary>
      /// Seperti <see cref="Verify"/>, tetapi menyebut kenapa tanda tangan ditolak: key-nya tidak dikenal,
      /// atau tanda tangannya tidak cocok.
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
      /// Bentuk PEM (<c>-----BEGIN PUBLIC KEY-----</c>) sebuah public key, seperti yang ditanam di
      /// launcher.
      /// </summary>
      public static string ToPem(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
         new string(PemEncoding.Write("PUBLIC KEY", subjectPublicKeyInfo)).ReplaceLineEndings("\n") + "\n";

      /// <summary><c>true</c> kalau <paramref name="key"/> berada di kurva P-256.</summary>
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
