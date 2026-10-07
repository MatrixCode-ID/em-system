using System.Security.Cryptography;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Blob (layer) store on disk, addressed by sha256, plus temporary upload files. Files only: who may
   /// see which blob is recorded in the database (<c>ta_CtnBlobLink</c>). Like <c>CdnStore</c>, it is
   /// always registered as a singleton - when the registry is off it is the disabled version - so
   /// <c>CtnServices</c> can always be created and answer 404 itself.
   /// </summary>
   internal sealed class CtnBlobStore
   {
      internal const string PublicRequestPath = "/v2";

      private const int CopyBufferSize = 81920;

      public static CtnBlobStore Disabled { get; } = new(null);

      private CtnBlobStore(string? rootPath) {
         IsEnabled = rootPath is not null;
         RootPath = rootPath ?? string.Empty;
      }

      /// <summary>
      /// Builds the store for <paramref name="configuredPath"/> as written in <c>Program.cs</c>: an absolute
      /// path is used as-is, a relative one is resolved from <paramref name="contentRootPath"/>. The folder
      /// is created when missing.
      /// </summary>
      public static CtnBlobStore Create(string configuredPath, string contentRootPath) {
         var fullPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRootPath, configuredPath));
         Directory.CreateDirectory(Path.Combine(fullPath, "blobs"));
         Directory.CreateDirectory(Path.Combine(fullPath, "uploads"));
         return new CtnBlobStore(fullPath);
      }

      public bool IsEnabled { get; }

      public string RootPath { get; }

      public void EnsureEnabled() {
         if (!IsEnabled) {
            throw new ActionException("Container registry is not enabled on this server.", 404);
         }
      }

      /// <summary>
      /// Path of the blob for <paramref name="digest"/> (<c>sha256:hex</c>, already validated by the caller).
      /// Spread into a subfolder of the first two characters so one folder does not hold millions of files.
      /// </summary>
      public string BlobPath(string digest) {
         var hex = digest[7..];
         return Path.Combine(RootPath, "blobs", "sha256", hex[..2], hex);
      }

      // Upload ids are always server-made ULIDs, but still validated: the value comes from the URL.
      public string UploadPath(string uploadId) {
         if (uploadId.Length != 26 || !uploadId.All(char.IsAsciiLetterOrDigit)) {
            throw new ArgumentException("Invalid upload id.", nameof(uploadId));
         }

         return Path.Combine(RootPath, "uploads", uploadId);
      }

      public void CreateUploadFile(string uploadId) {
         using var _ = new FileStream(UploadPath(uploadId), FileMode.CreateNew, FileAccess.Write, FileShare.None);
      }

      public long UploadSize(string uploadId) {
         var info = new FileInfo(UploadPath(uploadId));
         return info.Exists ? info.Length : 0;
      }

      /// <summary>Appends <paramref name="content"/> to the end of the upload file; returns its new size.</summary>
      public async Task<long> AppendUploadAsync(string uploadId, Stream content, CancellationToken ct) {
         await using var file = new FileStream(UploadPath(uploadId), FileMode.Append, FileAccess.Write, FileShare.None,
            CopyBufferSize, FileOptions.Asynchronous);
         await content.CopyToAsync(file, CopyBufferSize, ct);
         await file.FlushAsync(ct);
         return file.Length;
      }

      public void DeleteUploadFile(string uploadId) {
         try {
            File.Delete(UploadPath(uploadId));
         } catch (IOException) {
            // A temporary file; garbage collection cleans up the rest.
         }
      }

      /// <summary>The sha256 hash of the file as <c>sha256:hex</c> (lowercase).</summary>
      public static async Task<string> ComputeDigestAsync(string path, CancellationToken ct) {
         await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
         var hash = await SHA256.HashDataAsync(file, ct);
         return "sha256:" + Convert.ToHexStringLower(hash);
      }

      public static string ComputeDigest(ReadOnlySpan<byte> content) =>
         "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));

      /// <summary>
      /// Moves an upload file whose digest has been proven to become the blob. When that blob already exists
      /// (its content is certainly the same, the address being the digest), the upload file is simply
      /// discarded.
      /// </summary>
      public void CommitUpload(string uploadId, string digest) {
         var source = UploadPath(uploadId);
         var target = BlobPath(digest);
         Directory.CreateDirectory(Path.GetDirectoryName(target)!);
         if (File.Exists(target)) {
            File.Delete(source);
            return;
         }

         File.Move(source, target);
      }

      public bool BlobExists(string digest) => File.Exists(BlobPath(digest));

      /// <summary>
      /// The blob file exists and its size equals the recorded one. The content hash is not computed: this
      /// check is used to flag missing blobs in the list, not to prove the content is intact.
      /// </summary>
      public bool BlobIntact(string digest, long size) {
         var info = new FileInfo(BlobPath(digest));
         return info.Exists && info.Length == size;
      }
   }
}
