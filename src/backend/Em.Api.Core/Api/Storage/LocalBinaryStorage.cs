using Em.Shared;

namespace Em.Api.Core.Storage
{
   /// <summary>
   /// Penyimpanan isi berkas di folder pada mesin server, dinyalakan lewat
   /// <c>EmAppBuilder.AddLocalBinaryStorage</c>. Dipakai aplikasi yang belum perlu penyimpanan objek
   /// tersendiri; pemakainya tidak berubah saat kelak berpindah ke penyimpanan lain, karena yang
   /// dipegang pemakai adalah <see cref="IBinaryStorage"/>.
   /// </summary>
   /// <remarks>
   /// Folder ini bukan folder yang disajikan ke luar: tidak ada alamat publik yang menunjuk ke isinya.
   /// </remarks>
   public class LocalBinaryStorage : IBinaryStorage
   {
      /// <summary>
      /// Membuat penyimpanan berkas lokal pada sebuah folder.
      /// </summary>
      /// <param name="rootPath">
      /// Folder tempat isinya disimpan, sudah berupa path absolut. Foldernya dibuat sendiri kalau belum
      /// ada.
      /// </param>
      public LocalBinaryStorage(string rootPath) {
         ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
         RootPath = Path.GetFullPath(rootPath);
         Directory.CreateDirectory(RootPath);
      }

      /// <summary>Folder tempat isinya disimpan.</summary>
      public string RootPath { get; }

      /// <inheritdoc />
      public async Task<string> PutAsync(string key, Stream content, CancellationToken cancellationToken = default) {
         var target = ResolveInside(key, out var normalizedKey);
         ArgumentNullException.ThrowIfNull(content);
         cancellationToken.ThrowIfCancellationRequested();
         if (File.Exists(target) || Directory.Exists(target)) {
            throw new ActionException("The storage key is already in use.", 409);
         }

         Directory.CreateDirectory(Path.GetDirectoryName(target)!);
         var separator = normalizedKey.LastIndexOf(BinaryStorageKey.Separator);
         var parentKey = separator < 0 ? null : normalizedKey[..separator];
         var temporary = ResolveInside(
            BinaryStorageKey.Combine(parentKey, $".upload-{Guid.NewGuid():N}.tmp"), out _);
         var temporaryCreated = false;
         try {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                            FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
               temporaryCreated = true;
               await content.CopyToAsync(output, cancellationToken);
               await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try {
               File.Move(temporary, target);
               temporaryCreated = false;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target)) {
               throw new ActionException("The storage key is already in use.", 409);
            }
            return normalizedKey;
         }
         finally {
            if (temporaryCreated) File.Delete(temporary);
         }
      }

      /// <inheritdoc />
      public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) {
         var path = ResolveInside(key, out _);
         cancellationToken.ThrowIfCancellationRequested();
         try {
            Stream content = new FileStream(path, FileMode.Open, FileAccess.Read,
               FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(content);
         }
         catch (FileNotFoundException) {
            throw new ActionException("No content was found for the storage key.", 404);
         }
         catch (DirectoryNotFoundException) {
            throw new ActionException("No content was found for the storage key.", 404);
         }
      }

      /// <inheritdoc />
      public Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
         var path = ResolveInside(key, out _);
         cancellationToken.ThrowIfCancellationRequested();
         try {
            File.Delete(path);
         }
         catch (DirectoryNotFoundException) {
            // A missing parent means the content is already absent.
         }
         return Task.CompletedTask;
      }

      /// <inheritdoc />
      public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) {
         var path = ResolveInside(key, out _);
         cancellationToken.ThrowIfCancellationRequested();
         return Task.FromResult(File.Exists(path));
      }

      private string ResolveInside(string key, out string normalizedKey) {
         normalizedKey = BinaryStorageKey.Normalize(key);
         if (Path.IsPathRooted(key)) {
            throw new ArgumentException("A storage key must be a relative path.", nameof(key));
         }

         var fullPath = Path.GetFullPath(Path.Combine(RootPath,
            normalizedKey.Replace(BinaryStorageKey.Separator, Path.DirectorySeparatorChar)));
         var rootWithSeparator = Path.TrimEndingDirectorySeparator(RootPath) + Path.DirectorySeparatorChar;
         var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
         if (!fullPath.StartsWith(rootWithSeparator, comparison)) {
            throw new ArgumentException("The path points outside the storage folder.", nameof(key));
         }
         return fullPath;
      }
   }
}
