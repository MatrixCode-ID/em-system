using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Signing key rilis: sertifikat self-signed ECDSA P-256 di certificate store Windows
   /// (<c>CurrentUser\My</c>). Siapa pun yang memegang key-nya boleh menerbitkan rilis, jadi key dibagikan
   /// sebagai file <c>.pfx</c> berpassword lalu dipasang di mesin lain lewat <see cref="Import"/>.
   /// Password tidak disimpan oleh class ini; penyimpanan opsional diatur profile dan ReleaseSigningSecrets.
   /// </summary>
   public static class SigningCertificates
   {
      /// <summary>Subject sertifikat yang dibuat <see cref="Create"/>.</summary>
      public const string SubjectName = "CN=Em Release Signing";

      private const string FriendlyName = "Em Release Signing";

      /// <summary>
      /// Membuat signing key baru (berlaku 10 tahun), menyimpannya sebagai <c>.pfx</c> berpassword di
      /// <paramref name="pfxPath"/>, lalu langsung memasangnya di mesin ini <b>tanpa</b> bisa diekspor
      /// ulang. File <c>.pfx</c> itulah salinan induknya.
      /// </summary>
      /// <returns>Sertifikat yang sudah terpasang di store.</returns>
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
      /// Memasang signing key dari file <c>.pfx</c> ke <c>CurrentUser\My</c>. Kalau sertifikat yang sama
      /// sudah terpasang, yang lama diganti, supaya pilihan <paramref name="exportable"/> yang baru berlaku.
      /// </summary>
      /// <param name="pfxPath">File <c>.pfx</c>.</param>
      /// <param name="password">Password file itu.</param>
      /// <param name="exportable">
      /// <c>true</c> kalau key ini boleh diekspor lagi dari mesin ini (<see cref="Export"/>). Hanya berlaku
      /// untuk mesin ini: pemegang file dan password-nya tetap bisa mengimpornya lagi dengan pilihan lain.
      /// </param>
      /// <returns>Sertifikat yang sudah terpasang di store.</returns>
      /// <exception cref="CryptographicException">Password salah, file rusak, tanpa private key, atau bukan ECDSA P-256.</exception>
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
      /// Menulis ulang signing key <paramref name="certificate"/> ke file <c>.pfx</c> dengan password baru.
      /// Hanya bisa untuk key yang diimpor dengan pilihan exportable (<see cref="IsExportable"/>).
      /// </summary>
      /// <exception cref="CryptographicException">Key ini tidak boleh diekspor.</exception>
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
      /// Sertifikat di <c>CurrentUser\My</c> yang bisa menjadi signing key: ber-private key dan ECDSA P-256.
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

      /// <summary>Sertifikat signing key dengan thumbprint <paramref name="thumbprint"/>, atau <c>null</c>.</summary>
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

      /// <summary><c>true</c> kalau private key <paramref name="certificate"/> boleh diekspor dari mesin ini.</summary>
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

      /// <summary><c>keyId</c> public key <paramref name="certificate"/>, seperti yang tertulis di <c>release.json.sig</c>.</summary>
      public static string KeyIdOf(X509Certificate2 certificate) => ReleaseSignature.KeyIdOf(PublicKeyOf(certificate));

      /// <summary>
      /// Public key <paramref name="certificate"/> dalam bentuk PEM untuk ditanam di launcher, didahului satu
      /// baris <c>keyId: ...</c> (teks di luar blok PEM diabaikan pembaca PEM).
      /// </summary>
      public static string PublicKeyPem(X509Certificate2 certificate) =>
         $"keyId: {KeyIdOf(certificate)}\n{ReleaseSignature.ToPem(PublicKeyOf(certificate))}";

      /// <summary>
      /// Mencabut signing key dengan thumbprint <paramref name="thumbprint"/> dari mesin ini: sertifikatnya
      /// dari store, private key-nya dari disk.
      /// </summary>
      public static void Remove(string thumbprint) {
         using var store = OpenStore(OpenFlags.ReadWrite);
         foreach (var certificate in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false)) {
            DeleteKey(certificate);
            store.Remove(certificate);
            certificate.Dispose();
         }
      }

      /// <summary>Membuat key berkas P-256 dan sertifikat publik tanpa memasang ke store.</summary>
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

      /// <summary>Memvalidasi password dan private key; hasilnya hanya sertifikat publik.</summary>
      public static X509Certificate2 ValidatePfx(string pfxPath, string password) {
         using var certificate = LoadPfxForSigning(pfxPath, password);
         return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
      }

      /// <summary>Membuka private key sementara; pemanggil wajib Dispose.</summary>
      public static X509Certificate2 LoadPfxForSigning(string pfxPath, string password) {
         var certificate = Load(pfxPath, password, X509KeyStorageFlags.EphemeralKeySet);
         if (IsSigningCandidate(certificate)) return certificate;
         certificate.Dispose();
         throw new CryptographicException("The .pfx file must contain an ECDSA P-256 private key.");
      }

      /// <summary>Membaca sertifikat publik; tidak ada atau rusak menghasilkan null.</summary>
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

      /// <summary>Menulis sertifikat DER tanpa private key.</summary>
      public static void WriteCertificateFile(X509Certificate2 certificate, string certificatePath) =>
         File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Cert));

      /// <summary>Mengekspor ulang berkas key dengan password baru tanpa memasang ke store.</summary>
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
