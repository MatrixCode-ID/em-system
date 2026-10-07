using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// The release signing key: a self-signed ECDSA P-256 certificate in the Windows certificate store
   /// (<c>CurrentUser\My</c>). Whoever holds the key may publish releases, so the key is shared as a
   /// password-protected <c>.pfx</c> file and installed on another machine through <see cref="Import"/>.
   /// The password is not stored by this class; optional storage is handled by the profile and
   /// ReleaseSigningSecrets.
   /// </summary>
   public static class SigningCertificates
   {
      /// <summary>The subject of the certificate created by <see cref="Create"/>.</summary>
      public const string SubjectName = "CN=Em Release Signing";

      private const string FriendlyName = "Em Release Signing";

      /// <summary>
      /// Creates a new signing key (valid for 10 years), stores it as a password-protected <c>.pfx</c> at
      /// <paramref name="pfxPath"/>, then immediately installs it on this machine <b>without</b> the ability
      /// to export it again. That <c>.pfx</c> file is its master copy.
      /// </summary>
      /// <returns>The certificate that is now installed in the store.</returns>
      public static X509Certificate2 Create(string pfxPath, string password) {
         using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
         var request = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256);
         request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
         request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
         using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

         File.WriteAllBytes(pfxPath, ExportPfx(certificate, password));
         return Import(pfxPath, password, exportable: false);
      }

      /// <summary>
      /// Installs a signing key from a <c>.pfx</c> file into <c>CurrentUser\My</c>. If the same certificate is
      /// already installed, the old one is replaced, so the new <paramref name="exportable"/> choice applies.
      /// </summary>
      /// <param name="pfxPath">The <c>.pfx</c> file.</param>
      /// <param name="password">The password of that file.</param>
      /// <param name="exportable">
      /// <c>true</c> when this key may be exported again from this machine (<see cref="Export"/>). It only
      /// applies to this machine: whoever holds the file and its password can still import it again with a
      /// different choice.
      /// </param>
      /// <returns>The certificate that is now installed in the store.</returns>
      /// <exception cref="CryptographicException">The password is wrong, the file is corrupt, it has no private key, or it is not ECDSA P-256.</exception>
      public static X509Certificate2 Import(string pfxPath, string password, bool exportable) {
         // Checked on a throw-away copy first, so a file that is refused leaves no key behind on disk.
         using (var probe = Load(pfxPath, password, X509KeyStorageFlags.EphemeralKeySet)) {
            if (!probe.HasPrivateKey) throw new CryptographicException("The .pfx file does not contain a private key.");

            using var publicKey = probe.GetECDsaPublicKey();
            if (publicKey is null || !ReleaseSignature.IsP256(publicKey))
               throw new CryptographicException("The key in the .pfx file is not an ECDSA P-256 key.");
         }

         var flags = X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet;
         if (exportable) flags |= X509KeyStorageFlags.Exportable;
         using var certificate = Load(pfxPath, password, flags);
         certificate.FriendlyName = FriendlyName;

         using (var store = OpenStore(OpenFlags.ReadWrite)) {
            var newKeyName = KeyNameOf(certificate);
            foreach (var existing in store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false)) {
               // The old entry's key goes with it, unless Windows handed the new import the same container.
               if (KeyNameOf(existing) is { } oldKeyName && !string.Equals(oldKeyName, newKeyName, StringComparison.Ordinal))
                  DeleteKey(existing);
               store.Remove(existing);
               existing.Dispose();
            }

            store.Add(certificate);
         }

         return Find(certificate.Thumbprint)
                ?? throw new CryptographicException("The certificate was not found in the store after it was imported.");
      }

      /// <summary>
      /// Rewrites signing key <paramref name="certificate"/> to a <c>.pfx</c> file with a new password. Only
      /// possible for a key that was imported with the exportable choice (<see cref="IsExportable"/>).
      /// </summary>
      /// <exception cref="CryptographicException">This key may not be exported.</exception>
      public static void Export(X509Certificate2 certificate, string pfxPath, string password) {
         byte[] pfx;
         try {
            pfx = ExportPfx(certificate, password);
         }
         catch (CryptographicException x) {
            throw new CryptographicException("This key was imported as non-exportable and cannot be exported.", x);
         }

         File.WriteAllBytes(pfxPath, pfx);
      }

      /// <summary>
      /// The certificates in <c>CurrentUser\My</c> that can be a signing key: those with a private key and
      /// ECDSA P-256.
      /// </summary>
      public static IReadOnlyList<X509Certificate2> List() {
         using var store = OpenStore(OpenFlags.ReadOnly);
         var result = new List<X509Certificate2>();
         foreach (var certificate in store.Certificates) {
            if (IsSigningCandidate(certificate)) result.Add(certificate);
            else certificate.Dispose();
         }

         return result.OrderBy(r => r.Subject, StringComparer.OrdinalIgnoreCase).ThenByDescending(r => r.NotAfter).ToArray();
      }

      /// <summary>The signing key certificate with thumbprint <paramref name="thumbprint"/>, or <c>null</c>.</summary>
      public static X509Certificate2? Find(string? thumbprint) {
         if (string.IsNullOrWhiteSpace(thumbprint)) return null;

         using var store = OpenStore(OpenFlags.ReadOnly);
         X509Certificate2? found = null;
         foreach (var certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false)) {
            if (found is null && IsSigningCandidate(certificate)) found = certificate;
            else certificate.Dispose();
         }

         return found;
      }

      /// <summary><c>true</c> when the private key of <paramref name="certificate"/> may be exported from this machine.</summary>
      public static bool IsExportable(X509Certificate2 certificate) {
         try {
            using var key = certificate.GetECDsaPrivateKey();
            return key is ECDsaCng cng &&
                   (cng.Key.ExportPolicy & (CngExportPolicies.AllowExport | CngExportPolicies.AllowPlaintextExport)) != 0;
         }
         catch (CryptographicException) {
            return false;
         }
      }

      /// <summary>DER SubjectPublicKeyInfo public key <paramref name="certificate"/>.</summary>
      public static byte[] PublicKeyOf(X509Certificate2 certificate) => certificate.PublicKey.ExportSubjectPublicKeyInfo();

      /// <summary>The <c>keyId</c> of the public key of <paramref name="certificate"/>, as written in <c>release.json.sig</c>.</summary>
      public static string KeyIdOf(X509Certificate2 certificate) => ReleaseSignature.KeyIdOf(PublicKeyOf(certificate));

      /// <summary>
      /// The public key of <paramref name="certificate"/> in PEM form to be embedded in the launcher,
      /// preceded by one <c>keyId: ...</c> line (text outside the PEM block is ignored by PEM readers).
      /// </summary>
      public static string PublicKeyPem(X509Certificate2 certificate) =>
         $"keyId: {KeyIdOf(certificate)}\n{ReleaseSignature.ToPem(PublicKeyOf(certificate))}";

      /// <summary>
      /// Removes the signing key with thumbprint <paramref name="thumbprint"/> from this machine: the
      /// certificate from the store, the private key from disk.
      /// </summary>
      public static void Remove(string thumbprint) {
         using var store = OpenStore(OpenFlags.ReadWrite);
         foreach (var certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false)) {
            DeleteKey(certificate);
            store.Remove(certificate);
            certificate.Dispose();
         }
      }

      /// <summary>Creates a P-256 file key and a public certificate without installing into the store.</summary>
      public static X509Certificate2 CreatePfx(string pfxPath, string certificatePath, string password) {
         using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
         var request = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256);
         request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
         request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
         using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
         File.WriteAllBytes(pfxPath, ExportPfx(certificate, password));
         WriteCertificateFile(certificate, certificatePath);
         return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
      }

      /// <summary>Validates the password and the private key; the result is only the public certificate.</summary>
      public static X509Certificate2 ValidatePfx(string pfxPath, string password) {
         using var certificate = LoadPfxForSigning(pfxPath, password);
         return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
      }

      /// <summary>Opens a temporary private key; the caller must dispose it.</summary>
      public static X509Certificate2 LoadPfxForSigning(string pfxPath, string password) {
         var certificate = Load(pfxPath, password, X509KeyStorageFlags.EphemeralKeySet);
         if (IsSigningCandidate(certificate)) return certificate;
         certificate.Dispose();
         throw new CryptographicException("The .pfx file must contain an ECDSA P-256 private key.");
      }

      /// <summary>Reads a public certificate; absent or corrupt yields null.</summary>
      public static X509Certificate2? LoadCertificateFile(string certificatePath) {
         X509Certificate2? certificate = null;
         try {
            if (!File.Exists(certificatePath)) return null;
            certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
            using var key = certificate.GetECDsaPublicKey();
            if (key is not null && ReleaseSignature.IsP256(key)) return certificate;
            certificate.Dispose();
            return null;
         }
         catch (Exception x) when (x is IOException or CryptographicException or UnauthorizedAccessException) {
            certificate?.Dispose();
            return null;
         }
      }

      /// <summary>Writes a DER certificate without the private key.</summary>
      public static void WriteCertificateFile(X509Certificate2 certificate, string certificatePath) =>
         File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Cert));

      /// <summary>Exports the key file again with a new password without installing into the store.</summary>
      public static void ExportPfxFile(string sourcePfx, string sourcePassword, string targetPfx, string newPassword) {
         using var certificate = Load(sourcePfx, sourcePassword, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
         if (!IsSigningCandidate(certificate)) throw new CryptographicException("The .pfx file must contain an ECDSA P-256 private key.");
         File.WriteAllBytes(targetPfx, ExportPfx(certificate, newPassword));
      }

      private static bool IsSigningCandidate(X509Certificate2 certificate) {
         if (!certificate.HasPrivateKey) return false;
         try {
            using var key = certificate.GetECDsaPublicKey();
            return key is not null && ReleaseSignature.IsP256(key);
         }
         catch (CryptographicException) {
            return false;
         }
      }

      private static X509Certificate2 Load(string pfxPath, string password, X509KeyStorageFlags flags) {
         try {
            return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password, flags);
         }
         catch (CryptographicException x) {
            throw new CryptographicException("The .pfx file could not be opened: the password is wrong or the file is not a valid .pfx.", x);
         }
      }

      private static byte[] ExportPfx(X509Certificate2 certificate, string password) =>
         certificate.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, password);

      private static X509Store OpenStore(OpenFlags flags) {
         var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
         store.Open(flags);
         return store;
      }

      private static string? KeyNameOf(X509Certificate2 certificate) {
         try {
            using var key = certificate.GetECDsaPrivateKey();
            return (key as ECDsaCng)?.Key.KeyName;
         }
         catch (CryptographicException) {
            return null;
         }
      }

      private static void DeleteKey(X509Certificate2 certificate) {
         try {
            using var key = certificate.GetECDsaPrivateKey();
            (key as ECDsaCng)?.Key.Delete();
         }
         catch (CryptographicException) {
            // Best effort: a key that cannot be reached cannot be deleted either.
         }
      }
   }
}
