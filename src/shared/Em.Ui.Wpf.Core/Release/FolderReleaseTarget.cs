using System.IO;

namespace Em.Ui.Wpf.Core.Release
{
   /// <summary>
   /// A release target in an ordinary folder, local or on a network, that is written to directly. Every
   /// file is written to a temporary file starting with a dot in the same folder, then moved to its name,
   /// so a reader never sees a half-finished file.
   /// </summary>
   public sealed class FolderReleaseTarget : ReleaseTarget
   {
      private const int BufferSize = 81920;
      private readonly string _base;

      /// <summary>
      /// Creates a folder target: release folder <paramref name="releaseFolder"/> inside
      /// <paramref name="targetFolder"/>.
      /// </summary>
      public FolderReleaseTarget(string targetFolder, string releaseFolder) {
         _base = Path.GetFullPath(Path.Combine(targetFolder,
            NormalizeReleaseFolder(releaseFolder).Replace('/', Path.DirectorySeparatorChar)));
      }

      /// <inheritdoc />
      public override string Description => _base;

      /// <inheritdoc />
      public override async Task<byte[]?> ReadFileAsync(string path, CancellationToken token) {
         var file = Local(path);
         return File.Exists(file) ? await File.ReadAllBytesAsync(file, token).ConfigureAwait(false) : null;
      }

      /// <inheritdoc />
      public override async Task<(long Size, string Sha256)?> HashFileAsync(string path, IProgress<long>? progress,
         CancellationToken token) {
         var file = Local(path);
         if (!File.Exists(file)) return null;

         return await Task.Run(async () => await ReleaseHash.ComputeAsync(file, progress, token), token)
            .ConfigureAwait(false);
      }

      /// <inheritdoc />
      public override Task<IReadOnlyList<ReleaseTargetEntry>> ListBinariesAsync(CancellationToken token) =>
         Task.Run<IReadOnlyList<ReleaseTargetEntry>>(() => {
            var root = new DirectoryInfo(Local(ReleaseLayout.BinariesFolder));
            if (!root.Exists) return [];

            // Hidden and dot-prefixed entries are listed too: a temporary file left behind by an
            // interrupted Sync is not part of the release, so it shows up as something to remove.
            var options = new EnumerationOptions {
               RecurseSubdirectories = true,
               AttributesToSkip = 0,
               IgnoreInaccessible = false
            };
            return root.EnumerateFileSystemInfos("*", options)
               .Select(r => new ReleaseTargetEntry(
                  Path.GetRelativePath(root.FullName, r.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                  r is DirectoryInfo,
                  r is FileInfo file ? file.Length : 0))
               .ToArray();
         }, token);

      /// <inheritdoc />
      public override Task EnsureFolderAsync(string folder, CancellationToken token) {
         token.ThrowIfCancellationRequested();
         Directory.CreateDirectory(Local(folder));
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public override Task WriteFileAsync(string path, Stream content, IProgress<long>? progress,
         CancellationToken token) =>
         Task.Run(async () => {
            var target = Local(path);
            var temporary = Path.Combine(Path.GetDirectoryName(target)!,
               $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
            try {
               await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                               BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                  await using var input = new ProgressReadStream(content, progress, token);
                  await input.CopyToAsync(output, BufferSize, token);
               }

               File.Move(temporary, target, overwrite: true);
            }
            catch {
               TryDelete(temporary);
               throw;
            }
         }, token);

      /// <inheritdoc />
      public override Task DeleteAsync(string path, bool isFolder, CancellationToken token) =>
         Task.Run(() => {
            var local = Local(path);
            if (isFolder) {
               if (Directory.Exists(local)) Directory.Delete(local, recursive: true);
            }
            else if (File.Exists(local)) {
               File.SetAttributes(local, FileAttributes.Normal);
               File.Delete(local);
            }
         }, token);

      private string Local(string path) => Path.Combine(_base, path.Replace('/', Path.DirectorySeparatorChar));

      private static void TryDelete(string file) {
         try {
            File.Delete(file);
         }
         catch (Exception) {
            // Best effort: the original error is the one worth reporting.
         }
      }
   }
}
