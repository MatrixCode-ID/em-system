namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// The content of <c>release.json</c>: when the release was published and which files are part of it.
   /// Read and written through <see cref="ReleaseManifestSerializer"/>; its format is governed by
   /// <c>doc/release-format.md</c> section 2.
   /// </summary>
   public sealed class ReleaseManifest
   {
      /// <summary>The time the release was published, in UTC. Informational only.</summary>
      public required DateTime PublishedAtUtc { get; init; }

      /// <summary>All release files. Their paths are unique regardless of case.</summary>
      public required IReadOnlyList<ReleaseFile> Files { get; init; }

      /// <summary>
      /// Looks up a file by its path, regardless of case. <c>null</c> when it is not listed.
      /// </summary>
      /// <param name="path">The path relative to <c>binaries/</c>, separated by <c>/</c>.</param>
      public ReleaseFile? Find(string path) {
         _index ??= Files.ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
         return _index.GetValueOrDefault(path);
      }

      private Dictionary<string, ReleaseFile>? _index;
   }
}
