namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>Status satu file dibandingkan dengan rilis di tujuan.</summary>
   public enum ReleaseDiffStatus
   {
      /// <summary>Belum tercantum di <c>release.json</c> tujuan: akan diunggah.</summary>
      New = 0,

      /// <summary>Tercantum, tetapi isinya berbeda atau filenya tidak ada di tujuan: akan diunggah ulang.</summary>
      Changed = 1,

      /// <summary>Ada di <c>binaries/</c> tujuan, tetapi tidak ada di local publish folder: akan dihapus.</summary>
      Removed = 2,

      /// <summary>Ukuran dan SHA-256-nya sama: tidak disentuh.</summary>
      Same = 3
   }

   /// <summary>Satu baris hasil perbandingan.</summary>
   public sealed class ReleaseDiffItem
   {
      /// <summary>Path relatif terhadap <c>binaries/</c>, dipisah <c>/</c>.</summary>
      public required string Path { get; init; }

      /// <summary>Kelompok tampilan file ini.</summary>
      public required ReleaseGroup Group { get; init; }

      /// <summary>Apa yang akan terjadi pada file ini saat Sync.</summary>
      public required ReleaseDiffStatus Status { get; init; }

      /// <summary>Ukuran di local publish folder; <c>null</c> untuk file berstatus <see cref="ReleaseDiffStatus.Removed"/>.</summary>
      public long? LocalSize { get; init; }

      /// <summary>Ukuran di tujuan; <c>null</c> kalau file itu belum ada di sana.</summary>
      public long? RemoteSize { get; init; }
   }

   /// <summary>
   /// Hasil perbandingan local publish folder dengan rilis di tujuan: status setiap file, serta keadaan
   /// tujuan saat dibandingkan. Sync hanya berjalan di atas hasil ini, dan menolak berjalan kalau
   /// <c>release.json</c> tujuan sudah berubah sejak itu.
   /// </summary>
   public sealed class ReleaseComparison
   {
      /// <summary>Potret local publish folder yang dibandingkan.</summary>
      public required LocalSnapshot Local { get; init; }

      /// <summary>Byte <c>release.json</c> tujuan saat dibandingkan, atau <c>null</c> kalau belum ada.</summary>
      public required byte[]? RemoteManifestBytes { get; init; }

      /// <summary>Isi <c>release.json</c> tujuan, atau <c>null</c> kalau tidak ada atau tidak sah.</summary>
      public required ReleaseManifest? RemoteManifest { get; init; }

      /// <summary>Kenapa <c>release.json</c> tujuan tidak bisa dibaca, atau <c>null</c>.</summary>
      public string? RemoteManifestError { get; init; }

      /// <summary>Setiap file lokal dan setiap file tujuan yang akan dihapus, urut path.</summary>
      public required IReadOnlyList<ReleaseDiffItem> Items { get; init; }

      /// <summary>
      /// Folder di <c>binaries/</c> tujuan yang tidak lagi dipakai rilis baru, hanya yang paling atas
      /// (folder di dalamnya ikut terhapus bersamanya).
      /// </summary>
      public required IReadOnlyList<string> FoldersToDelete { get; init; }

      /// <summary>Jumlah file dengan status <paramref name="status"/>.</summary>
      public int CountOf(ReleaseDiffStatus status) => Items.Count(r => r.Status == status);

      /// <summary>Total byte yang akan diunggah Sync.</summary>
      public long UploadBytes => Items.Where(IsUpload).Sum(r => r.LocalSize ?? 0);

      /// <summary>
      /// <c>true</c> kalau Sync akan mengubah sesuatu di tujuan selain menandatangani ulang: ada file yang
      /// diunggah atau dihapus, atau <c>release.json</c> tujuan belum mencantumkan persis file lokal.
      /// </summary>
      public bool HasChanges =>
         Items.Any(r => r.Status != ReleaseDiffStatus.Same) || FoldersToDelete.Count > 0 ||
         RemoteManifest is null || RemoteManifest.Files.Count != Local.Files.Count;

      internal static bool IsUpload(ReleaseDiffItem item) =>
         item.Status is ReleaseDiffStatus.New or ReleaseDiffStatus.Changed;
   }

   /// <summary>
   /// Membandingkan local publish folder dengan rilis di tujuan. Status "sama/ganti" diambil dari ukuran
   /// dan SHA-256 di <c>release.json</c> tujuan; yang "hapus" diambil dari isi <c>binaries/</c> yang
   /// benar-benar ada di tujuan, supaya sisa Sync yang terputus ikut bersih.
   /// </summary>
   public static class ReleaseComparer
   {
      /// <summary>Membaca <c>release.json</c> dan isi <c>binaries/</c> di tujuan, lalu membandingkannya.</summary>
      public static async Task<ReleaseComparison> CompareAsync(ReleaseTarget target, LocalSnapshot local,
         CancellationToken token) {
         var manifest = await target.ReadFileAsync(ReleaseLayout.ManifestFileName, token).ConfigureAwait(false);
         var binaries = await target.ListBinariesAsync(token).ConfigureAwait(false);
         return Compare(local, manifest, binaries);
      }

      /// <summary>
      /// Membandingkan <paramref name="local"/> dengan keadaan tujuan yang sudah dibaca. Manifest tujuan
      /// yang tidak sah diperlakukan seperti tidak ada: semua file lokal berstatus baru.
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
