namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// One release file inside the manifest: its path relative to the <c>binaries/</c> folder, its size, and
   /// the SHA-256 of its content. The rules for each value are in <c>doc/release-format.md</c> section 2.
   /// </summary>
   public sealed class ReleaseFile
   {
      /// <summary>
      /// The path relative to <c>binaries/</c>, separated by <c>/</c>, which also carries the file name
      /// (e.g. <c>runtimes/win-x64/native/x.dll</c>). Compared without regard to case.
      /// </summary>
      public required string Path { get; init; }

      /// <summary>The file size in bytes.</summary>
      public required long Size { get; init; }

      /// <summary>The SHA-256 of the file content, 64 lowercase hex characters.</summary>
      public required string Sha256 { get; init; }

      /// <summary>
      /// <c>true</c> when <paramref name="other"/> holds the same file: its size and SHA-256 are the same.
      /// The path is not compared.
      /// </summary>
      public bool HasSameContent(ReleaseFile other) =>
         Size == other.Size && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);
   }
}
