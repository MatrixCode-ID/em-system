namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// The content of <c>release.json.sig</c>: the pointer to the signer's public key and the signature. Its
   /// format is governed by <c>doc/release-format.md</c> section 3.
   /// </summary>
   public sealed class ReleaseSignatureFile
   {
      /// <summary>
      /// The first 16 lowercase hex characters of the SHA-256 of the signer's public key, to choose the public
      /// key used to verify (see <see cref="ReleaseSignature.KeyIdOf"/>).
      /// </summary>
      public required string KeyId { get; init; }

      /// <summary>An ECDSA P-256/SHA-256 signature in the IEEE P1363 format (64 bytes), base64-encoded.</summary>
      public required string Signature { get; init; }
   }
}
