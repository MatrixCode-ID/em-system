namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Satu file rilis di dalam manifest: path-nya relatif terhadap folder <c>binaries/</c>, ukuran, dan
   /// SHA-256 isinya. Aturan setiap nilai ada di <c>doc/release-format.md</c> bagian 2.
   /// </summary>
   public sealed class ReleaseFile
   {
      /// <summary>
      /// Path relatif terhadap <c>binaries/</c>, dipisah <c>/</c>, sekaligus membawa nama filenya
      /// (mis. <c>runtimes/win-x64/native/x.dll</c>). Dibandingkan tanpa memandang huruf besar/kecil.
      /// </summary>
      public required string Path { get; init; }

      /// <summary>Ukuran file dalam byte.</summary>
      public required long Size { get; init; }

      /// <summary>SHA-256 isi file, 64 karakter hex huruf kecil.</summary>
      public required string Sha256 { get; init; }

      /// <summary>
      /// <c>true</c> kalau <paramref name="other"/> berisi file yang sama: ukuran dan SHA-256-nya sama.
      /// Path tidak ikut dibandingkan.
      /// </summary>
      public bool HasSameContent(ReleaseFile other) =>
         Size == other.Size && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);
   }
}
