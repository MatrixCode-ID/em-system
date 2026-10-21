using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using Em.Ui.Wpf.Core.Release;
namespace Em.Ui.Wpf.Dialogs {
   /// <summary>One signing key installed on this machine, ready to be shown.</summary>
   public class ReleaseSigningKey
   {
      /// <summary>Thumbprint of its certificate.</summary>
      public required string Thumbprint { get; init; }

      /// <summary>Subject of its certificate.</summary>
      public required string Subject { get; init; }

      /// <summary>The <c>keyId</c> of its public key, as in <c>release.json.sig</c>.</summary>
      public required string KeyId { get; init; }

      /// <summary>Validity limit of its certificate.</summary>
      public required DateTime NotAfter { get; init; }

      /// <summary><c>true</c> when its private key may be exported from this machine.</summary>
      public required bool IsExportable { get; init; }

      /// <summary>Tulisan di combobox.</summary>
      public string Caption => $"{Subject.Replace("CN=", "")} · {KeyId}";

      /// <summary>Tulisan chip exportable.</summary>
      public string ExportableCaption => IsExportable ? "Exportable" : "Non-exportable";

      /// <summary>The validity limit in a human-friendly form.</summary>
      public string ExpiresCaption => NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

      /// <inheritdoc />
      /// <remarks>The combobox's selection box shows items through this value, not through DisplayMemberPath.</remarks>
      public override string ToString() => Caption;

      /// <summary>Every signing key installed in this user's certificate store.</summary>
      public static IReadOnlyList<ReleaseSigningKey> ListInstalled() {
         var keys = new List<ReleaseSigningKey>();
         foreach (var certificate in SigningCertificates.List()) {
            using (certificate) keys.Add(From(certificate));
         }

         return keys;
      }

      /// <summary>The signing key with thumbprint <paramref name="thumbprint"/>, or <c>null</c> when it is not installed.</summary>
      public static ReleaseSigningKey? Find(string? thumbprint) {
         using var certificate = SigningCertificates.Find(thumbprint);
         return certificate is null ? null : From(certificate);
      }

      /// <summary>The key source that is represented.</summary>
      public ReleaseSigningSource Source { get; init; }

      /// <summary>Model of the public key from the profile file.</summary>
      public static ReleaseSigningKey FromCertificateFile(X509Certificate2 certificate) => new() {
         Thumbprint = certificate.Thumbprint, Subject = certificate.Subject,
         KeyId = SigningCertificates.KeyIdOf(certificate), NotAfter = certificate.NotAfter,
         IsExportable = true, Source = ReleaseSigningSource.ProfileFile
      };

      private static ReleaseSigningKey From(X509Certificate2 certificate) => new() {
         Thumbprint = certificate.Thumbprint,
         Subject = certificate.Subject,
         KeyId = SigningCertificates.KeyIdOf(certificate),
         NotAfter = certificate.NotAfter,
         IsExportable = SigningCertificates.IsExportable(certificate)
      };
   }

}
