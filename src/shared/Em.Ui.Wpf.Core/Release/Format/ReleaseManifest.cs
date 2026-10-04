namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// Isi <c>release.json</c>: kapan rilis diterbitkan dan file apa saja yang menjadi bagiannya. Dibaca
   /// dan ditulis lewat <see cref="ReleaseManifestSerializer"/>; formatnya diatur
   /// <c>doc/release-format.md</c> bagian 2.
   /// </summary>
   public sealed class ReleaseManifest
   {
      /// <summary>Waktu rilis diterbitkan, dalam UTC. Hanya informasi.</summary>
      public required DateTime PublishedAtUtc { get; init; }

      /// <summary>Seluruh file rilis. Path-nya unik tanpa memandang huruf besar/kecil.</summary>
      public required IReadOnlyList<ReleaseFile> Files { get; init; }

      /// <summary>
      /// Mencari file berdasarkan path-nya, tanpa memandang huruf besar/kecil. <c>null</c> kalau tidak
      /// tercantum.
      /// </summary>
      /// <param name="path">Path relatif terhadap <c>binaries/</c>, dipisah <c>/</c>.</param>
      public ReleaseFile? Find(string path) {
         _index ??= Files.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
         return _index.GetValueOrDefault(path);
      }

      private Dictionary<string, ReleaseFile>? _index;
   }
}
