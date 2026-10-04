namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Isi <c>release.json.sig</c>: penunjuk public key penanda tangan dan tanda tangannya. Formatnya
   /// diatur <c>doc/release-format.md</c> bagian 3.
   /// </summary>
   public sealed class ReleaseSignatureFile
   {
      /// <summary>
      /// 16 karakter hex huruf kecil pertama SHA-256 dari public key penanda tangan, untuk memilih public
      /// key yang dipakai memverifikasi (lihat <see cref="ReleaseSignature.KeyIdOf"/>).
      /// </summary>
      public required string KeyId { get; init; }

      /// <summary>Tanda tangan ECDSA P-256/SHA-256 format IEEE P1363 (64 byte), di-encode base64.</summary>
      public required string Signature { get; init; }
   }
}
