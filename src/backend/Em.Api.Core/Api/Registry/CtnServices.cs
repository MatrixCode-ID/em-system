using System.Data;
using Microsoft.EntityFrameworkCore;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Sisi pengelolaan container registry: root, folder dan container bernama, semuanya di balik satu claim <see cref="ICtnServices.CtnClaim"/>. Push dan pull tidak lewat
   /// sini - itu jalur <c>/v2</c> di <see cref="CtnRegistryEndpoint"/>. Berkas blob di disk tidak
   /// disentuh kecuali berkas unggahan sementara; pembersihan blob yatim lewat <see cref="PostGetMeta_CtnGcRun"/>.
   /// </summary>
   [Module(Defaults.AdministrativeToolsModuleName)]
   public partial class CtnServices : ServicesBase, ICtnServices
   {
      private Em.Api.Core.Storage.ManagedStorageSettings Settings => GetService<Em.Api.Core.Storage.ManagedStorageSettings>()!;
      [GetAction(claim: ICtnServices.CtnClaim)]
      public Task<StorageFeatureStatus> GetMeta_CtnStatus() => Task.FromResult(Settings.Status(false));
      [GetAction(claim: ICtnServices.SettingsClaim)]
      public Task<StorageSettingsDetail> GetMeta_CtnSettings() => Task.FromResult(Settings.Detail(false));
      [PostAction(claim: ICtnServices.SettingsClaim)]
      public Task<StorageDirectoryValidation> PostGetMeta_CtnValidateDirectory(StorageFeatureSettings settings) => Task.FromResult(Settings.Validate(false, settings, target => RegistryStorageIntegrity.Verify(GetService<CtnContext>()!, target)));
      [PostAction(claim: ICtnServices.SettingsClaim)]
      public Task<StorageSettingsDetail> PostGetMeta_CtnSettingsSave(StorageSettingsSave request) => Task.FromResult(Settings.Save(false, request, target => RegistryStorageIntegrity.Verify(GetService<CtnContext>()!, target)));

      private CtnBlobStore Store {
         get {
            var store = GetService<CtnBlobStore>()!;
            store.EnsureEnabled();
            return store;
         }
      }

      private CtnContext Db {
         get {
            _ = Store;
            return GetService<CtnContext>()!;
         }
      }

      #region Root

      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnStorageInfo> GetMeta_CtnStorageSize() {
         var db = Db;
         // Blob disimpan sekali secara global, meskipun dipakai banyak image/tag.
         // Jangan join link/tag: join akan menggandakan ukuran shared layer.
         return new CtnStorageInfo {
            BlobBytes = await db.Blobs.SumAsync(b => (long?)b.cCtnBlobSize, AbortToken) ?? 0,
            ManifestBytes = await db.Manifests.SumAsync(m => (long?)m.cCtnManifestSize, AbortToken) ?? 0
         };
      }

      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnRootInfo[]> GetMeta_CtnRoots() {
         var db = Db;
         var roots = await db.Roots.OrderBy(r => r.cCtnRootName).ToListAsync(AbortToken);
         var folders = await db.Folders.GroupBy(f => f.cCtnRootId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(r => r.Id, r => r.Count, AbortToken);
         var images = await db.Images.GroupBy(f => f.cCtnRootId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(r => r.Id, r => r.Count, AbortToken);
         return roots.Select(r => ToInfo(r, folders.GetValueOrDefault(r.cCtnRootId), images.GetValueOrDefault(r.cCtnRootId))).ToArray();
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnRootInfo> PostGetMeta_CtnRootCreate(string name, string? description) {
         var db = Db;
         name = CtnNames.ValidateRootName(name);
         var now = DateTime.UtcNow;
         var row = new ta_CtnRoot {
            cCtnRootId = $"{Ulid.NewUlid()}", cCtnRootName = name, cCtnRootState = CtnNames.StateActive,
            cCtnRootDescription = CtnNames.ValidateDescription(description), ustamp = now, datestamp = now
         };

         if (await db.Roots.AnyAsync(r => r.cCtnRootName == name)) {
            throw new ActionException($"Root '{name}' already exists.", 409);
         }

         db.Roots.Add(row);
         await SaveOrConflictAsync(db, $"Root '{name}' already exists.");
         return ToInfo(row, 0, 0);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnRootUpdate(string rootId, string? description, bool isActive) {
         var db = Db;
         var text = CtnNames.ValidateDescription(description);
         var state = isActive ? CtnNames.StateActive : CtnNames.StateDisabled;
         var now = DateTime.UtcNow;
         var rows = await db.Roots.Where(r => r.cCtnRootId == rootId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.cCtnRootDescription, text)
            .SetProperty(r => r.cCtnRootState, state)
            .SetProperty(r => r.ustamp, now));
         if (rows == 0) throw NotFound("Root");
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnRootDelete(string rootId) {
         var db = Db;
         var root = await RequireRootAsync(db, rootId);
         if (await db.Folders.AnyAsync(f => f.cCtnRootId == rootId) || await db.Images.AnyAsync(i => i.cCtnRootId == rootId)) {
            throw new ActionException($"Root '{root.cCtnRootName}' still has folders or containers; remove them first.", 409);
         }

         if (await db.RobotRoots.AnyAsync(r => r.cCtnRootId == rootId)) {
            throw new ActionException($"Root '{root.cCtnRootName}' still has robot access; revoke it first.", 409);
         }

         await db.Roots.Where(r => r.cCtnRootId == rootId).ExecuteDeleteAsync();
      }

      #endregion

      #region Folder dan container

      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnTree> GetMeta_CtnTree(string rootId) {
         var db = Db;
         var root = await RequireRootAsync(db, rootId);
         var folders = await db.Folders.Where(f => f.cCtnRootId == rootId)
            .OrderBy(f => f.cCtnFolderOrder).ThenBy(f => f.cCtnFolderName).ToListAsync(AbortToken);
         var images = await db.Images.Where(i => i.cCtnRootId == rootId).OrderBy(i => i.cCtnImageName).ToListAsync(AbortToken);
         var imageIds = images.Select(i => i.cCtnImageId).ToList();
         var tags = await db.Tags.Where(t => imageIds.Contains(t.cCtnImageId)).GroupBy(t => t.cCtnImageId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(r => r.Id, r => r.Count, AbortToken);
         var manifests = await db.Manifests.Where(m => imageIds.Contains(m.cCtnImageId)).GroupBy(m => m.cCtnImageId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(r => r.Id, r => r.Count, AbortToken);

         return new CtnTree {
            Folders = folders.Select(ToInfo).ToArray(),
            Images = images.Select(i => ToInfo(i, root.cCtnRootName, tags.GetValueOrDefault(i.cCtnImageId), manifests.GetValueOrDefault(i.cCtnImageId))).ToArray()
         };
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnFolderInfo> PostGetMeta_CtnFolderCreate(string rootId, string? parentFolderId, string name) {
         var db = Db;
         await RequireRootAsync(db, rootId);
         name = CtnNames.ValidateFolderName(name);
         parentFolderId = Normalize(parentFolderId);

         // Keunikan nama saudara dengan parent kosong tidak bisa dijaga indeks unik di semua database
         // (SQL Server menganggap dua NULL sama, MySQL dan PostgreSQL tidak), jadi dijaga di sini dalam
         // satu transaksi.
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
         var all = await db.Folders.Where(f => f.cCtnRootId == rootId).ToListAsync();
         var depth = parentFolderId is null ? 0 : DepthOf(all, parentFolderId);
         if (depth + 1 > CtnNames.MaxFolderDepth) {
            throw new ActionException($"Folders may be nested at most {CtnNames.MaxFolderDepth} levels deep.", 400);
         }

         EnsureNameFree(all, parentFolderId, name, exceptId: null);
         var now = DateTime.UtcNow;
         var row = new ta_CtnFolder {
            cCtnFolderId = $"{Ulid.NewUlid()}", cCtnRootId = rootId, cCtnFolderParent_cCtnFolderId = parentFolderId,
            cCtnFolderName = name, ustamp = now, datestamp = now
         };
         db.Folders.Add(row);
         await db.SaveChangesAsync();
         await tx.CommitAsync();
         return ToInfo(row);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnFolderInfo> PostGetMeta_CtnFolderRename(string folderId, string name) {
         var db = Db;
         name = CtnNames.ValidateFolderName(name);
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
         var folder = await RequireFolderAsync(db, folderId);
         var all = await db.Folders.Where(f => f.cCtnRootId == folder.cCtnRootId).ToListAsync();
         EnsureNameFree(all, folder.cCtnFolderParent_cCtnFolderId, name, exceptId: folderId);

         folder.cCtnFolderName = name;
         folder.ustamp = DateTime.UtcNow;
         db.UpdateRow(folder);
         await db.SaveChangesAsync();
         await tx.CommitAsync();
         return ToInfo(folder);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnFolderInfo> PostGetMeta_CtnFolderMove(string folderId, string? targetParentFolderId) {
         var db = Db;
         targetParentFolderId = Normalize(targetParentFolderId);
         await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
         var folder = await RequireFolderAsync(db, folderId);
         var all = await db.Folders.Where(f => f.cCtnRootId == folder.cCtnRootId).ToListAsync();

         if (targetParentFolderId is not null) {
            if (all.All(f => f.cCtnFolderId != targetParentFolderId)) {
               throw new ActionException("The target folder is not in the same root.", 400);
            }

            // Sebuah folder tidak boleh dipindah ke dirinya sendiri atau ke keturunannya.
            for (var cursor = targetParentFolderId; cursor is not null;
                 cursor = all.First(f => f.cCtnFolderId == cursor).cCtnFolderParent_cCtnFolderId) {
               if (cursor == folderId) throw new ActionException("A folder cannot be moved into itself.", 400);
            }
         }

         var depth = targetParentFolderId is null ? 0 : DepthOf(all, targetParentFolderId);
         if (depth + HeightOf(all, folderId) > CtnNames.MaxFolderDepth) {
            throw new ActionException($"Folders may be nested at most {CtnNames.MaxFolderDepth} levels deep.", 400);
         }

         EnsureNameFree(all, targetParentFolderId, folder.cCtnFolderName, exceptId: folderId);
         folder.cCtnFolderParent_cCtnFolderId = targetParentFolderId;
         folder.ustamp = DateTime.UtcNow;
         db.UpdateRow(folder);
         await db.SaveChangesAsync();
         await tx.CommitAsync();
         return ToInfo(folder);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnFolderDelete(string folderId) {
         var db = Db;
         var folder = await RequireFolderAsync(db, folderId);
         if (await db.Folders.AnyAsync(f => f.cCtnFolderParent_cCtnFolderId == folderId) ||
             await db.Images.AnyAsync(i => i.cCtnFolderId == folderId)) {
            throw new ActionException($"Folder '{folder.cCtnFolderName}' is not empty; move or delete its contents first.", 409);
         }

         await db.Folders.Where(f => f.cCtnFolderId == folderId).ExecuteDeleteAsync();
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnImageInfo> PostGetMeta_CtnImageCreate(string rootId, string? folderId, string name, string? description) {
         var db = Db;
         var root = await RequireRootAsync(db, rootId);
         name = CtnNames.ValidateImageName(root.cCtnRootName, name);
         folderId = Normalize(folderId);
         if (folderId is not null && !await db.Folders.AnyAsync(f => f.cCtnFolderId == folderId && f.cCtnRootId == rootId)) {
            throw new ActionException("The target folder is not in this root.", 400);
         }

         if (await db.Images.AnyAsync(i => i.cCtnRootId == rootId && i.cCtnImageName == name)) {
            throw new ActionException($"'{root.cCtnRootName}/{name}' already exists.", 409);
         }

         var now = DateTime.UtcNow;
         var row = new ta_CtnImage {
            cCtnImageId = $"{Ulid.NewUlid()}", cCtnRootId = rootId, cCtnFolderId = folderId, cCtnImageName = name,
            cCtnImageState = CtnNames.StateActive, cCtnImageDescription = CtnNames.ValidateDescription(description),
            ustamp = now, datestamp = now
         };
         db.Images.Add(row);
         await SaveOrConflictAsync(db, $"'{root.cCtnRootName}/{name}' already exists.");
         return ToInfo(row, root.cCtnRootName, 0, 0);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnImageInfo> PostGetMeta_CtnImageMove(string imageId, string? targetFolderId) {
         var db = Db;
         var image = await RequireImageAsync(db, imageId);
         targetFolderId = Normalize(targetFolderId);
         if (targetFolderId is not null &&
             !await db.Folders.AnyAsync(f => f.cCtnFolderId == targetFolderId && f.cCtnRootId == image.cCtnRootId)) {
            throw new ActionException("The target folder is not in the same root.", 400);
         }

         // Hanya metadata: blob tidak disalin dan nama pull (root/nama) tidak berubah.
         image.cCtnFolderId = targetFolderId;
         image.ustamp = DateTime.UtcNow;
         db.UpdateRow(image);
         await db.SaveChangesAsync();
         return await DescribeImageAsync(db, image);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnImageUpdate(string imageId, string? description, bool isActive) {
         var db = Db;
         var text = CtnNames.ValidateDescription(description);
         var state = isActive ? CtnNames.StateActive : CtnNames.StateDisabled;
         var now = DateTime.UtcNow;
         var rows = await db.Images.Where(i => i.cCtnImageId == imageId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.cCtnImageDescription, text)
            .SetProperty(i => i.cCtnImageState, state)
            .SetProperty(i => i.ustamp, now));
         if (rows == 0) throw NotFound("Container");
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnImageDelete(string imageId) {
         var db = Db;
         await RequireImageAsync(db, imageId);
         var uploadIds = await db.Uploads.Where(u => u.cCtnImageId == imageId).Select(u => u.cCtnUploadId).ToListAsync();

         // In order and explicit, not relying on CASCADE: tags point at manifests, so tags go first; the deploy
         // target and its history go before the container.
         await using var tx = await db.Database.BeginTransactionAsync();
         await db.Tags.Where(t => t.cCtnImageId == imageId).ExecuteDeleteAsync();
         await db.ManifestBlobs.Where(b => db.Manifests.Any(m => m.cCtnManifestId == b.cCtnManifestId && m.cCtnImageId == imageId)).ExecuteDeleteAsync();
         await db.Manifests.Where(m => m.cCtnImageId == imageId).ExecuteDeleteAsync();
         await db.BlobLinks.Where(l => l.cCtnImageId == imageId).ExecuteDeleteAsync();
         await db.Uploads.Where(u => u.cCtnImageId == imageId).ExecuteDeleteAsync();
         await Deploy.CtnDeployStore.DeleteForImageAsync(db, imageId);
         await db.Images.Where(i => i.cCtnImageId == imageId).ExecuteDeleteAsync();
         await tx.CommitAsync();

         foreach (var id in uploadIds) Store.DeleteUploadFile(id);
      }

      [GetAction(claim: ICtnServices.CtnClaim)]
      public async Task<CtnManifestInfo[]> GetMeta_CtnImageManifests(string imageId) {
         var db = Db;
         await RequireImageAsync(db, imageId);

         // Kolom isi manifest sengaja tidak ikut dibaca: daftar ini tidak membutuhkannya.
         var manifests = await db.Manifests.Where(m => m.cCtnImageId == imageId).OrderByDescending(m => m.datestamp)
            .Select(m => new {
               m.cCtnManifestId, m.cCtnManifestDigest, m.cCtnManifestMediaType, m.cCtnManifestSize, m.datestamp,
               m.cCtnManifestPushedBy_cRobotId
            }).ToListAsync(AbortToken);
         var tags = await db.Tags.Where(t => t.cCtnImageId == imageId).OrderBy(t => t.cCtnTagName).ToListAsync(AbortToken);
         var robotIds = manifests.Select(m => m.cCtnManifestPushedBy_cRobotId).Where(r => r != null).Distinct().ToList();
         var robots = await db.Robots.Where(r => robotIds.Contains(r.cRobotId))
            .ToDictionaryAsync(r => r.cRobotId, r => r.cRobotName, AbortToken);

         // Blob tiap manifest diperiksa ke storage (ada dan ukurannya sama, tanpa hash): database bisa dipakai
         // bersama folder storage lain, sehingga metadata tidak selalu punya berkasnya.
         var manifestIds = manifests.Select(m => m.cCtnManifestId).ToList();
         var links = await db.ManifestBlobs.Where(l => manifestIds.Contains(l.cCtnManifestId))
            .Join(db.Blobs, l => l.cCtnBlobId, b => b.cCtnBlobId,
               (l, b) => new { l.cCtnManifestId, b.cCtnBlobDigest, b.cCtnBlobSize })
            .ToListAsync(AbortToken);
         var intact = new Dictionary<string, bool>(StringComparer.Ordinal);
         foreach (var blob in links.DistinctBy(l => l.cCtnBlobDigest)) {
            intact[blob.cCtnBlobDigest] = blob.cCtnBlobDigest.Length == 71 && Store.BlobIntact(blob.cCtnBlobDigest, blob.cCtnBlobSize);
         }

         var blobsByManifest = links.ToLookup(l => l.cCtnManifestId, l => l.cCtnBlobDigest);

         return manifests.Select(m => new CtnManifestInfo {
            Id = m.cCtnManifestId,
            Digest = m.cCtnManifestDigest,
            MediaType = m.cCtnManifestMediaType,
            Size = m.cCtnManifestSize,
            PushedAt = m.datestamp,
            PushedBy = m.cCtnManifestPushedBy_cRobotId is { } id ? robots.GetValueOrDefault(id) : null,
            Tags = tags.Where(t => t.cCtnManifestId == m.cCtnManifestId).Select(t => t.cCtnTagName).ToArray(),
            BlobCount = blobsByManifest[m.cCtnManifestId].Distinct().Count(),
            MissingBlobCount = blobsByManifest[m.cCtnManifestId].Distinct().Count(d => !intact[d])
         }).ToArray();
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnTagDelete(string imageId, string tag) {
         var db = Db;
         await RequireImageAsync(db, imageId);
         var rows = await db.Tags.Where(t => t.cCtnImageId == imageId && t.cCtnTagName == tag).ExecuteDeleteAsync(AbortToken);
         if (rows == 0) throw new ActionException($"Tag '{tag}' was not found.", 404);
      }

      [PostAction(claim: ICtnServices.CtnClaim)]
      public async Task PostMeta_CtnManifestDelete(string imageId, string manifestId) {
         var db = Db;
         await RequireImageAsync(db, imageId);
         var digest = await db.Manifests.Where(m => m.cCtnManifestId == manifestId && m.cCtnImageId == imageId)
            .Select(m => m.cCtnManifestDigest).SingleOrDefaultAsync(AbortToken) ?? throw NotFound("Manifest");
         if (await CtnManifestDeletion.FindReferencingIndexAsync(db, imageId, digest, AbortToken) is { } index) {
            throw new ActionException($"Manifest is referenced by index '{index}'; delete the index first.", 409);
         }

         await CtnManifestDeletion.DeleteAsync(db, manifestId, AbortToken);
      }

      #endregion

      #region Garbage collection

      [GetAction(claim: ICtnServices.CtnClaim)]
      public Task<CtnGcReport> GetMeta_CtnGcReview(int graceHours) =>
         new CtnGarbageCollector(Db, Store).RunAsync(graceHours, dryRun: true, AbortToken);

      [PostAction(claim: ICtnServices.CtnClaim)]
      public Task<CtnGcReport> PostGetMeta_CtnGcRun(int graceHours) =>
         new CtnGarbageCollector(Db, Store).RunAsync(graceHours, dryRun: false, AbortToken);

      #endregion

      #region Helpers

      private static ActionException NotFound(string what) => new($"{what} was not found.", 404);

      // Id kosong dari UI berarti "tidak ada"; dibuat null supaya satu bentuk saja yang sampai ke query.
      private static string? Normalize(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

      private static async Task<ta_CtnRoot> RequireRootAsync(CtnContext db, string rootId) =>
         await db.Roots.SingleOrDefaultAsync(r => r.cCtnRootId == rootId) ?? throw NotFound("Root");

      private static async Task<ta_CtnFolder> RequireFolderAsync(CtnContext db, string folderId) =>
         await db.Folders.SingleOrDefaultAsync(f => f.cCtnFolderId == folderId) ?? throw NotFound("Folder");

      private static async Task<ta_CtnImage> RequireImageAsync(CtnContext db, string imageId) =>
         await db.Images.SingleOrDefaultAsync(i => i.cCtnImageId == imageId) ?? throw NotFound("Container");

      // Insert yang bentrok dengan indeks unik (dua permintaan membuat nama yang sama bersamaan) dijawab
      // 409 seperti pemeriksaan di depannya, bukan 500.
      private static async Task SaveOrConflictAsync(CtnContext db, string conflictMessage) {
         try {
            await db.SaveChangesAsync();
         } catch (DbUpdateException) {
            throw new ActionException(conflictMessage, 409);
         }
      }

      // Tingkat folder: folder langsung di root = 1.
      private static int DepthOf(List<ta_CtnFolder> all, string folderId) {
         var depth = 0;
         for (var cursor = folderId; cursor is not null;
              cursor = all.First(f => f.cCtnFolderId == cursor).cCtnFolderParent_cCtnFolderId) {
            depth++;
         }

         return depth;
      }

      // Tinggi sub-tree: folder itu sendiri = 1, ditambah tingkat anak terdalam.
      private static int HeightOf(List<ta_CtnFolder> all, string folderId) {
         var children = all.Where(f => f.cCtnFolderParent_cCtnFolderId == folderId).ToList();
         return 1 + (children.Count == 0 ? 0 : children.Max(c => HeightOf(all, c.cCtnFolderId)));
      }

      private static void EnsureNameFree(List<ta_CtnFolder> all, string? parentId, string name, string? exceptId) {
         if (all.Any(f => f.cCtnFolderParent_cCtnFolderId == parentId && f.cCtnFolderId != exceptId &&
                          string.Equals(f.cCtnFolderName, name, StringComparison.OrdinalIgnoreCase))) {
            throw new ActionException($"A folder named '{name}' already exists here.", 409);
         }
      }

      private static async Task<CtnImageInfo> DescribeImageAsync(CtnContext db, ta_CtnImage image) {
         var rootName = await db.Roots.Where(r => r.cCtnRootId == image.cCtnRootId).Select(r => r.cCtnRootName).SingleAsync();
         var tags = await db.Tags.CountAsync(t => t.cCtnImageId == image.cCtnImageId);
         var manifests = await db.Manifests.CountAsync(m => m.cCtnImageId == image.cCtnImageId);
         return ToInfo(image, rootName, tags, manifests);
      }

      private static CtnRootInfo ToInfo(ta_CtnRoot r, int folders, int images) => new() {
         Id = r.cCtnRootId, Name = r.cCtnRootName, Description = r.cCtnRootDescription,
         IsActive = r.cCtnRootState == CtnNames.StateActive, FolderCount = folders, ImageCount = images, CreatedAt = r.datestamp
      };

      private static CtnFolderInfo ToInfo(ta_CtnFolder f) => new() {
         Id = f.cCtnFolderId, RootId = f.cCtnRootId, ParentId = f.cCtnFolderParent_cCtnFolderId, Name = f.cCtnFolderName
      };

      private static CtnImageInfo ToInfo(ta_CtnImage i, string rootName, int tags, int manifests) => new() {
         Id = i.cCtnImageId, RootId = i.cCtnRootId, RootName = rootName, FolderId = i.cCtnFolderId, Name = i.cCtnImageName,
         FullName = $"{rootName}/{i.cCtnImageName}", Description = i.cCtnImageDescription,
         IsActive = i.cCtnImageState == CtnNames.StateActive, TagCount = tags, ManifestCount = manifests, CreatedAt = i.datestamp
      };

      #endregion
   }
}
