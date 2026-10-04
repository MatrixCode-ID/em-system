using System.Security.Cryptography;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Penyimpan blob (layer) di disk, beralamat sha256, plus berkas unggahan sementara. Hanya berkas:
   /// siapa yang boleh melihat blob mana dicatat di database (<c>ta_CtnBlobLink</c>). Seperti
   /// <c>CdnStore</c>, selalu didaftarkan sebagai singleton - saat registry mati ia versi yang mati -
   /// supaya <c>CtnServices</c> selalu bisa dibuat dan menjawab 404 sendiri.
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
      /// Membangun store untuk <paramref name="configuredPath"/> seperti yang ditulis di
      /// <c>Program.cs</c>: path absolut dipakai apa adanya, relatif dihitung dari
      /// <paramref name="contentRootPath"/>. Foldernya dibuat kalau belum ada.
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
      /// Path blob untuk <paramref name="digest"/> (<c>sha256:hex</c>, sudah divalidasi pemanggil).
      /// Disebar ke subfolder dua karakter pertama supaya satu folder tidak menampung jutaan berkas.
      /// </summary>
      public string BlobPath(string digest) {
         var hex = digest[7..];
         return Path.Combine(RootPath, "blobs", "sha256", hex[..2], hex);
      }

      // Id unggahan selalu ULID buatan server, tapi tetap divalidasi: nilainya datang dari URL.
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

      /// <summary>Menambahkan <paramref name="content"/> ke akhir berkas unggahan; mengembalikan ukuran barunya.</summary>
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
            // Berkas sementara; sisanya akan dibersihkan pembersihan unggahan basi (tahap 2).
         }
      }

      /// <summary>Hash sha256 berkas dalam bentuk <c>sha256:hex</c> (huruf kecil).</summary>
      public static async Task<string> ComputeDigestAsync(string path, CancellationToken ct) {
         await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
         var hash = await SHA256.HashDataAsync(file, ct);
         return "sha256:" + Convert.ToHexStringLower(hash);
      }

      public static string ComputeDigest(ReadOnlySpan<byte> content) =>
         "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));

      /// <summary>
      /// Memindahkan berkas unggahan yang digest-nya sudah terbukti menjadi blob. Kalau blob itu sudah ada
      /// (isinya pasti sama, alamatnya digest), berkas unggahan cukup dibuang.
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
   }
}
