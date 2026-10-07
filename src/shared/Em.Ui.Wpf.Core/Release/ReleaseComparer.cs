namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Status of one file compared with the release at the target.</summary>
   public enum ReleaseDiffStatus
   {
      /// <summary>Not listed in the target's <c>release.json</c>: it will be uploaded.</summary>
      New = 0,

      /// <summary>Listed, but its content is different or the file is missing at the target: it will be uploaded again.</summary>
      Changed = 1,

      /// <summary>Exists in the target's <c>binaries/</c> but not in the local publish folder: it will be deleted.</summary>
      Removed = 2,

      /// <summary>Its size and SHA-256 are the same: it is not touched.</summary>
      Same = 3
   }

   /// <summary>One row of the comparison result.</summary>
   public sealed class ReleaseDiffItem
   {
      /// <summary>Path relative to <c>binaries/</c>, separated by <c>/</c>.</summary>
      public required string Path { get; init; }

      /// <summary>The display group of this file.</summary>
      public required ReleaseGroup Group { get; init; }

      /// <summary>What will happen to this file at Sync.</summary>
      public required ReleaseDiffStatus Status { get; init; }

      /// <summary>Size in the local publish folder; <c>null</c> for a file with status <see cref="ReleaseDiffStatus.Removed"/>.</summary>
      public long? LocalSize { get; init; }

      /// <summary>Size at the target; <c>null</c> when the file is not there yet.</summary>
      public long? RemoteSize { get; init; }
   }

   /// <summary>
   /// The result of comparing the local publish folder with the release at the target: the status of every
   /// file, and the state of the target at the moment of comparison. Sync only runs on top of this result,
   /// and refuses to run if the target's <c>release.json</c> has changed since.
   /// </summary>
   public sealed class ReleaseComparison
   {
      /// <summary>The snapshot of the local publish folder that was compared.</summary>
      public required LocalSnapshot Local { get; init; }

      /// <summary>The bytes of the target's <c>release.json</c> at the time of comparison, or <c>null</c> when it does not exist yet.</summary>
      public required byte[]? RemoteManifestBytes { get; init; }

      /// <summary>The content of the target's <c>release.json</c>, or <c>null</c> when it does not exist or is not valid.</summary>
      public required ReleaseManifest? RemoteManifest { get; init; }

      /// <summary>Why the target's <c>release.json</c> could not be read, or <c>null</c>.</summary>
      public string? RemoteManifestError { get; init; }

      /// <summary>Every local file and every target file that will be deleted, ordered by path.</summary>
      public required IReadOnlyList<ReleaseDiffItem> Items { get; init; }

      /// <summary>
      /// Folders in the target's <c>binaries/</c> that the new release no longer uses, only the topmost ones
      /// (folders inside them are deleted with them).
      /// </summary>
      public required IReadOnlyList<string> FoldersToDelete { get; init; }

      /// <summary>Number of files with status <paramref name="status"/>.</summary>
      public int CountOf(ReleaseDiffStatus status) => Items.Count(r => r.Status == status);

      /// <summary>Total bytes that Sync will upload.</summary>
      public long UploadBytes => Items.Where(IsUpload).Sum(r => r.LocalSize ?? 0);

      /// <summary>
      /// <c>true</c> when Sync will change something at the target besides re-signing: a file is uploaded or
      /// deleted, or the target's <c>release.json</c> does not yet list exactly the local files.
      /// </summary>
      public bool HasChanges =>
         Items.Any(r => r.Status != ReleaseDiffStatus.Same) || FoldersToDelete.Count > 0 ||
         RemoteManifest is null || RemoteManifest.Files.Count != Local.Files.Count;

      internal static bool IsUpload(ReleaseDiffItem item) =>
         item.Status is ReleaseDiffStatus.New or ReleaseDiffStatus.Changed;
   }

   /// <summary>
   /// Compares the local publish folder with the release at the target. The "same/replace" status is taken
   /// from the size and SHA-256 in the target's <c>release.json</c>; "delete" is taken from the content of
   /// <c>binaries/</c> that really exists at the target, so leftovers of an interrupted Sync are cleaned too.
   /// </summary>
   public static class ReleaseComparer
   {
      /// <summary>Reads <c>release.json</c> and the content of <c>binaries/</c> at the target, then compares them.</summary>
      public static async Task<ReleaseComparison> CompareAsync(ReleaseTarget target, LocalSnapshot local,
         CancellationToken token) {
         var manifest = await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token).ConfigureAwait(false);
         var binaries = await target.ListBinariesAsync(token).ConfigureAwait(false);
         return Compare(local, manifest, binaries);
      }

      /// <summary>
      /// Compares <paramref name="local"/> with the target state that has already been read. A target manifest
      /// that is not valid is treated as absent: all local files have the new status.
      /// </summary>
      public static ReleaseComparison Compare(LocalSnapshot local, byte[]? remoteManifestBytes,
         IReadOnlyList<ReleaseTargetEntry> remoteBinaries) {
         ReleaseManifest? manifest = null;
         string? manifestError = null;
         if (remoteManifestBytes is not null) {
            try {
               manifest = ReleaseManifestSerializer.Deserialize(remoteManifestBytes);
            }
            catch (ReleaseFormatException x) {
               manifestError = x.Message;
            }
         }

         var remoteFiles = remoteBinaries.Where(r => !r.IsFolder)
            .ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
         var localPaths = new HashSet<string>(local.Files.Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
         var items = new List<ReleaseDiffItem>();

         foreach (var file in local.Files) {
            var listed = manifest?.Find(file.Path);
            var present = remoteFiles.GetValueOrDefault(file.Path);
            var status = listed is null ? ReleaseDiffStatus.New
               : !listed.HasSameContent(file) ? ReleaseDiffStatus.Changed
               // Listed as the same, but not really there: an interrupted Sync or a hand edit.
               : present is null || present.Size != file.Size ? ReleaseDiffStatus.Changed
               : ReleaseDiffStatus.Same;
            items.Add(new ReleaseDiffItem {
               Path = file.Path,
               Group = local.Grouping.GroupOf(file.Path),
               Status = status,
               LocalSize = file.Size,
               RemoteSize = present?.Size
            });
         }

         foreach (var remote in remoteFiles.Values.Where(r => !localPaths.Contains(r.Path))) {
            items.Add(new ReleaseDiffItem {
               Path = remote.Path,
               Group = local.Grouping.GroupOf(remote.Path),
               Status = ReleaseDiffStatus.Removed,
               RemoteSize = remote.Size
            });
         }

         // Every folder a local file lives in must stay; any other folder at the target goes, and only
         // the outermost of those needs deleting.
         var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
         foreach (var path in localPaths) {
            for (var parent = ReleaseTarget.ParentOf(path); parent is not null; parent = ReleaseTarget.ParentOf(parent))
               needed.Add(parent);
         }

         var unused = remoteBinaries.Where(r => r.IsFolder && !needed.Contains(r.Path)).Select(r => r.Path).ToArray();
         var outermost = unused.Where(r => ReleaseTarget.ParentOf(r) is not { } parent || needed.Contains(parent))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

         return new ReleaseComparison {
            Local = local,
            RemoteManifestBytes = remoteManifestBytes,
            RemoteManifest = manifest,
            RemoteManifestError = manifestError,
            Items = items.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            FoldersToDelete = outermost
         };
      }
   }
}
