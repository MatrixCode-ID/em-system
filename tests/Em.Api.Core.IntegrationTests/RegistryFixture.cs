using System.Text;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Registry;

namespace Em.Api.Core.IntegrationTests
{
   /// <summary>
   /// Registry di database kosong tambahan plus folder storage sementara, untuk test garbage collection dan
   /// penghapusan manifest. Skema dibuat EF tanpa foreign key (modelnya tidak punya relasi); itu cukup untuk
   /// perilaku yang diuji di sini. Folder dan database dihapus saat <see cref="DisposeAsync"/> / akhir run.
   /// </summary>
   internal sealed class RegistryFixture : IAsyncDisposable
   {
      private int counter;

      private RegistryFixture(CtnContext db, CtnBlobStore store, string folder) {
         Db = db;
         Store = store;
         Folder = folder;
      }

      public CtnContext Db { get; }
      public CtnBlobStore Store { get; }
      public string Folder { get; }

      public static async Task<RegistryFixture> CreateAsync(SqlServerDatabase database, CancellationToken ct) {
         var connectionString = await database.CreateExtraDatabaseAsync(ct);
         var db = new CtnContext(new DbContextOptionsBuilder<CtnContext>().UseSqlServer(connectionString).Options);
         await db.Database.EnsureCreatedAsync(ct);

         var folder = Path.Combine(Path.GetTempPath(), "em-ctn-gc-" + Guid.NewGuid().ToString("N"));
         Directory.CreateDirectory(folder);
         var store = CtnBlobStore.Create(folder, folder);
         Directory.CreateDirectory(Path.Combine(folder, "uploads"));
         return new RegistryFixture(db, store, folder);
      }

      public async ValueTask DisposeAsync() {
         await Db.DisposeAsync();
         try {
            Directory.Delete(Folder, true);
         } catch (IOException) {
         } catch (UnauthorizedAccessException) {
         }
      }

      /// <summary>Membuat root dan container; mengembalikan id container.</summary>
      public async Task<string> AddImageAsync(string root, string name, CancellationToken ct) {
         var now = DateTime.UtcNow;
         var rootId = await Db.Roots.Where(r => r.cCtnRootName == root).Select(r => r.cCtnRootId).SingleOrDefaultAsync(ct);
         if (rootId is null) {
            rootId = $"{Ulid.NewUlid()}";
            Db.Roots.Add(new ta_CtnRoot { cCtnRootId = rootId, cCtnRootName = root, cCtnRootState = CtnNames.StateActive, ustamp = now, datestamp = now });
         }

         var imageId = $"{Ulid.NewUlid()}";
         Db.Images.Add(new ta_CtnImage {
            cCtnImageId = imageId, cCtnRootId = rootId, cCtnImageName = name, cCtnImageState = CtnNames.StateActive, ustamp = now, datestamp = now
         });
         await Db.SaveChangesAsync(ct);
         return imageId;
      }

      /// <summary>Mencatat blob dengan isi unik; berkasnya ditulis (kecuali <paramref name="writeFile"/> false).</summary>
      public async Task<(string Id, string Digest, long Size)> AddBlobAsync(DateTime createdUtc, CancellationToken ct, bool writeFile = true, bool writeRow = true) {
         var bytes = Encoding.UTF8.GetBytes("blob-" + Interlocked.Increment(ref counter) + "-" + Guid.NewGuid());
         var digest = CtnBlobStore.ComputeDigest(bytes);
         var id = $"{Ulid.NewUlid()}";
         if (writeFile) {
            var path = Store.BlobPath(digest);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
            File.SetLastWriteTimeUtc(path, createdUtc);
         }

         if (writeRow) {
            Db.Blobs.Add(new ta_CtnBlob { cCtnBlobId = id, cCtnBlobDigest = digest, cCtnBlobSize = bytes.Length, ustamp = createdUtc, datestamp = createdUtc });
            await Db.SaveChangesAsync(ct);
         }

         return (id, digest, bytes.Length);
      }

      public async Task AddLinkAsync(string imageId, string blobId, DateTime createdUtc, CancellationToken ct) {
         Db.BlobLinks.Add(new ta_CtnBlobLink { cCtnImageId = imageId, cCtnBlobId = blobId, datestamp = createdUtc });
         await Db.SaveChangesAsync(ct);
      }

      /// <summary>Mencatat manifest berikut daftar blob dan tag-nya; mengembalikan id dan digest manifest.</summary>
      public async Task<(string Id, string Digest)> AddManifestAsync(
         string imageId, string mediaType, string content, string[] blobIds, string[] tags, DateTime createdUtc, CancellationToken ct) {
         var bytes = Encoding.UTF8.GetBytes(content);
         var id = $"{Ulid.NewUlid()}";
         var digest = CtnBlobStore.ComputeDigest(bytes);
         Db.Manifests.Add(new ta_CtnManifest {
            cCtnManifestId = id, cCtnImageId = imageId, cCtnManifestDigest = digest, cCtnManifestMediaType = mediaType,
            cCtnManifestSize = bytes.Length, cCtnManifestContent = bytes, ustamp = createdUtc, datestamp = createdUtc
         });
         for (var i = 0; i < blobIds.Length; i++) {
            Db.ManifestBlobs.Add(new ta_CtnManifestBlob {
               cCtnManifestId = id, cCtnManifestBlobOrder = i, cCtnBlobId = blobIds[i], cCtnManifestBlobRole = "layer"
            });
         }

         foreach (var tag in tags) {
            Db.Tags.Add(new ta_CtnTag { cCtnImageId = imageId, cCtnTagName = tag, cCtnManifestId = id, ustamp = createdUtc, datestamp = createdUtc });
         }

         await Db.SaveChangesAsync(ct);
         return (id, digest);
      }

      /// <summary>Mencatat upload; berkasnya ditulis dengan waktu tulis <paramref name="ustamp"/>.</summary>
      public async Task<string> AddUploadAsync(string imageId, DateTime ustamp, bool writeFile, CancellationToken ct) {
         var id = $"{Ulid.NewUlid()}";
         Db.Uploads.Add(new ta_CtnUpload {
            cCtnUploadId = id, cCtnImageId = imageId, cRobotId = "robot", cCtnUploadSize = 4, ustamp = ustamp, datestamp = ustamp
         });
         await Db.SaveChangesAsync(ct);
         if (writeFile) {
            var path = Store.UploadPath(id);
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], ct);
            File.SetLastWriteTimeUtc(path, ustamp);
         }

         return id;
      }

      /// <summary>Berkas upload tanpa baris metadata, dengan nama ULID baru dan waktu tulis tertentu.</summary>
      public async Task<string> AddLooseUploadFileAsync(DateTime writtenUtc, CancellationToken ct) {
         var id = $"{Ulid.NewUlid()}";
         var path = Store.UploadPath(id);
         await File.WriteAllBytesAsync(path, [1, 2, 3, 4], ct);
         File.SetLastWriteTimeUtc(path, writtenUtc);
         return path;
      }
   }
}
