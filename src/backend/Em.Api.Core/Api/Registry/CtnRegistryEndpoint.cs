using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Em.Api.Core.Registry
{
   /// <summary>
   /// Jalur publik <c>/v2</c> (OCI Distribution) yang cukup untuk <c>docker login</c>, <c>push</c>, dan
   /// <c>pull</c>: ping, blob (unggah bersegmen, mount lintas container, Range), manifest, dan daftar
   /// tag. Dipasang <c>EmApp.Run</c> di luar jalur action, jadi tidak terkena batas waktu dan jatah
   /// request action; autentikasinya robot (Basic), bukan sesi Em.
   /// </summary>
   /// <remarks>
   /// Push tidak pernah membuat root, folder, atau nama container: yang belum dibuat lewat
   /// <c>ICtnServices</c> dijawab <c>NAME_UNKNOWN</c>. Hak mengikuti root: robot tanpa baris hak di
   /// sebuah root tidak bisa melihat root itu (juga <c>NAME_UNKNOWN</c>); robot <c>R</c> yang mencoba
   /// menulis dijawab <c>DENIED</c>.
   /// </remarks>
   internal static class CtnRegistryEndpoint
   {
      private const int MaxManifestSize = 4 * 1024 * 1024;

      // Request yang sedang dilayani, dibawa ke setiap handler supaya parameternya tidak berderet.
      private sealed class Call
      {
         public required HttpContext Http { get; init; }
         public required CtnContext Db { get; init; }
         public required CtnBlobStore Store { get; init; }
         public required ta_Robot Robot { get; init; }
         public required ta_CtnRoot Root { get; init; }
         public required ta_CtnImage Image { get; init; }
         public required CtnRoute Route { get; init; }
         public required string FullName { get; init; }
         public CancellationToken Ct => Http.RequestAborted;
         public string BasePath => Http.Request.PathBase.Value ?? "/v2";
      }

      public static async Task HandleAsync(HttpContext http) {
         var store = http.RequestServices.GetRequiredService<CtnBlobStore>();
         if (!store.IsEnabled) {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
         }

         http.Response.Headers["Docker-Distribution-API-Version"] = "registry/2.0";
         try {
            await DispatchAsync(http, store);
         } catch (CtnRegistryException ex) {
            await WriteErrorAsync(http, ex);
         } catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) {
            // Klien pergi di tengah jalan; tidak ada yang menunggu jawaban.
         } catch (Exception ex) {
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Em.Registry")
               .LogError(ex, "Registry request {Method} {Path} failed", http.Request.Method, http.Request.Path);
            await WriteErrorAsync(http, new CtnRegistryException(500, "UNKNOWN", "Internal registry error."));
         }
      }

      private static async Task WriteErrorAsync(HttpContext http, CtnRegistryException ex) {
         if (http.Response.HasStarted) return;

         http.Response.Clear();
         http.Response.Headers["Docker-Distribution-API-Version"] = "registry/2.0";
         if (ex.StatusCode == 401) {
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"Em Container Registry\"";
         }

         http.Response.StatusCode = ex.StatusCode;
         await http.Response.WriteAsJsonAsync(new {
            errors = new[] { new { code = ex.Code, message = ex.Message, detail = ex.Detail } }
         });
      }

      private static async Task DispatchAsync(HttpContext http, CtnBlobStore store) {
         var route = CtnRoute.Parse(http.Request.Path.Value);
         var db = http.RequestServices.GetRequiredService<CtnContext>();

         var robot = await RobotAuth.AuthenticateAsync(http, http.RequestServices.GetRequiredService<RobotContext>()) ??
                     throw new CtnRegistryException(401, "UNAUTHORIZED", "Authentication required.");

         if (route.Kind == CtnRouteKind.Ping) {
            await http.Response.WriteAsJsonAsync(new { });
            return;
         }

         if (route.Kind == CtnRouteKind.Unknown) {
            throw new CtnRegistryException(404, "UNSUPPORTED", "The requested path is not supported.");
         }

         if (route.NameSegments.Length != 2 ||
             !CtnNames.IsValidName(route.NameSegments[0], CtnNames.MaxRootName) ||
             !CtnNames.IsValidName(route.NameSegments[1], CtnNames.MaxImageName)) {
            throw new CtnRegistryException(404, "NAME_INVALID",
               "Container names are exactly 'root/name': two segments, lowercase letters and digits separated by '.', '_' or '-'.");
         }

         var needsWrite = !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));
         var (root, image) = await ResolveNameAsync(db, robot, route.NameSegments, needsWrite, http.RequestAborted);

         var call = new Call {
            Http = http, Db = db, Store = store, Robot = robot, Root = root, Image = image, Route = route,
            FullName = $"{route.NameSegments[0]}/{route.NameSegments[1]}"
         };

         var method = http.Request.Method;
         switch (route.Kind) {
            case CtnRouteKind.Tags when HttpMethods.IsGet(method):
               await ListTagsAsync(call);
               break;
            case CtnRouteKind.Manifest when HttpMethods.IsGet(method) || HttpMethods.IsHead(method):
               await GetManifestAsync(call);
               break;
            case CtnRouteKind.Manifest when HttpMethods.IsPut(method):
               await PutManifestAsync(call);
               break;
            case CtnRouteKind.Manifest when HttpMethods.IsDelete(method):
               await DeleteManifestAsync(call);
               break;
            case CtnRouteKind.Blob when HttpMethods.IsGet(method) || HttpMethods.IsHead(method):
               await GetBlobAsync(call);
               break;
            case CtnRouteKind.UploadStart when HttpMethods.IsPost(method):
               await StartUploadAsync(call);
               break;
            case CtnRouteKind.Upload when HttpMethods.IsPatch(method):
               await PatchUploadAsync(call);
               break;
            case CtnRouteKind.Upload when HttpMethods.IsPut(method):
               await FinishUploadAsync(call);
               break;
            case CtnRouteKind.Upload when HttpMethods.IsGet(method):
               await UploadStatusAsync(call);
               break;
            case CtnRouteKind.Upload when HttpMethods.IsDelete(method):
               await CancelUploadAsync(call);
               break;
            default:
               throw new CtnRegistryException(405, "UNSUPPORTED", $"{method} is not supported on this path.");
         }
      }

      // Root dan nama harus sudah dibuat lewat layanan manajemen, dan robotnya harus punya hak di root
      // itu. Robot tanpa baris hak tidak boleh tahu root-nya ada, jadi jawabannya sama dengan root yang
      // tidak ada.
      private static async Task<(ta_CtnRoot, ta_CtnImage)> ResolveNameAsync(CtnContext db, ta_Robot robot,
         string[] name, bool needsWrite, CancellationToken ct) {
         var rootName = name[0];
         var root = await db.Roots.SingleOrDefaultAsync(r => r.cCtnRootName == rootName, ct);
         var access = root is null
            ? null
            : await db.RobotRoots
               .Where(r => r.cRobotId == robot.cRobotId && r.cCtnRootId == root.cCtnRootId)
               .Select(r => r.cCtnRootRobotAccess)
               .SingleOrDefaultAsync(ct);
         if (root is null || access is null) {
            throw new CtnRegistryException(404, "NAME_UNKNOWN", $"Repository name '{name[0]}/{name[1]}' is not known.");
         }

         if (needsWrite && access != CtnNames.AccessWrite) {
            throw new CtnRegistryException(403, "DENIED", $"This account may only pull from '{rootName}'.");
         }

         if (root.cCtnRootState != CtnNames.StateActive) {
            throw new CtnRegistryException(403, "DENIED", $"Root '{rootName}' is disabled.");
         }

         var imageName = name[1];
         var image = await db.Images.SingleOrDefaultAsync(r => r.cCtnRootId == root.cCtnRootId && r.cCtnImageName == imageName, ct);
         if (image is null) {
            throw new CtnRegistryException(404, "NAME_UNKNOWN",
               $"Repository name '{name[0]}/{name[1]}' is not known; create it in the Container Manager first.");
         }

         if (image.cCtnImageState != CtnNames.StateActive) {
            throw new CtnRegistryException(403, "DENIED", $"Container '{name[0]}/{name[1]}' is disabled.");
         }

         return (root, image);
      }

      #region Tags

      private static async Task ListTagsAsync(Call c) {
         var query = c.Http.Request.Query;
         var limit = int.TryParse(query["n"], out var n) && n > 0 ? Math.Min(n, 1000) : 0;
         var last = query["last"].ToString();

         var tags = c.Db.Tags.Where(t => t.cCtnImageId == c.Image.cCtnImageId);
         if (last.Length > 0) {
            tags = tags.Where(t => string.Compare(t.cCtnTagName, last) > 0);
         }

         var ordered = tags.OrderBy(t => t.cCtnTagName).Select(t => t.cCtnTagName);
         var names = limit > 0
            ? await ordered.Take(limit + 1).ToListAsync(c.Ct)
            : await ordered.ToListAsync(c.Ct);

         if (limit > 0 && names.Count > limit) {
            names.RemoveAt(names.Count - 1);
            c.Http.Response.Headers.Link =
               $"<{c.BasePath}/{c.FullName}/tags/list?last={Uri.EscapeDataString(names[^1])}&n={limit}>; rel=\"next\"";
         }

         await c.Http.Response.WriteAsJsonAsync(new { name = c.FullName, tags = names });
      }

      #endregion

      #region Manifests

      private static async Task<ta_CtnManifest?> FindManifestAsync(Call c, string reference) {
         if (CtnNames.IsValidDigest(reference)) {
            return await c.Db.Manifests.SingleOrDefaultAsync(
               m => m.cCtnImageId == c.Image.cCtnImageId && m.cCtnManifestDigest == reference, c.Ct);
         }

         return await c.Db.Tags
            .Where(t => t.cCtnImageId == c.Image.cCtnImageId && t.cCtnTagName == reference)
            .Join(c.Db.Manifests, t => t.cCtnManifestId, m => m.cCtnManifestId, (_, m) => m)
            .SingleOrDefaultAsync(c.Ct);
      }

      private static async Task GetManifestAsync(Call c) {
         var reference = c.Route.Reference!;
         var manifest = await FindManifestAsync(c, reference) ??
                        throw new CtnRegistryException(404, "MANIFEST_UNKNOWN", $"Manifest '{reference}' is unknown.");

         var response = c.Http.Response;
         response.ContentType = manifest.cCtnManifestMediaType;
         response.ContentLength = manifest.cCtnManifestContent.Length;
         response.Headers["Docker-Content-Digest"] = manifest.cCtnManifestDigest;
         response.Headers.ETag = $"\"{manifest.cCtnManifestDigest}\"";
         if (!HttpMethods.IsHead(c.Http.Request.Method)) {
            await response.Body.WriteAsync(manifest.cCtnManifestContent, c.Ct);
         }
      }

      private static async Task PutManifestAsync(Call c) {
         var reference = c.Route.Reference!;
         var isDigestReference = CtnNames.IsValidDigest(reference);
         if (!isDigestReference && !CtnNames.IsValidTag(reference)) {
            throw new CtnRegistryException(400, reference.StartsWith("sha256:", StringComparison.Ordinal) ? "DIGEST_INVALID" : "TAG_INVALID",
               $"'{reference}' is not a valid tag or sha256 digest.");
         }

         var content = await ReadLimitedAsync(c.Http, MaxManifestSize);
         var digest = CtnBlobStore.ComputeDigest(content);
         if (isDigestReference && reference != digest) {
            throw new CtnRegistryException(400, "DIGEST_INVALID", "The digest in the URL does not match the manifest content.");
         }

         var (mediaType, blobRefs, childRefs) = ParseManifest(content, c.Http.Request.ContentType);
         var imageId = c.Image.cCtnImageId;

         // Setiap blob yang disebut manifest harus sudah terhubung ke container ini; tautan itulah pagar
         // yang membuat digest layer milik container lain tidak bisa dipakai begitu saja.
         var blobIds = new Dictionary<string, string>();
         var wantedBlobs = blobRefs.Select(r => r.Digest).Distinct().ToList();
         if (wantedBlobs.Count > 0) {
            var found = await c.Db.BlobLinks.Where(l => l.cCtnImageId == imageId)
               .Join(c.Db.Blobs.Where(b => wantedBlobs.Contains(b.cCtnBlobDigest)),
                  l => l.cCtnBlobId, b => b.cCtnBlobId, (_, b) => new { b.cCtnBlobId, b.cCtnBlobDigest })
               .ToListAsync(c.Ct);
            blobIds = found.ToDictionary(r => r.cCtnBlobDigest, r => r.cCtnBlobId);
            var missing = wantedBlobs.Where(d => !blobIds.ContainsKey(d)).ToArray();
            if (missing.Length > 0) {
               throw new CtnRegistryException(400, "MANIFEST_BLOB_UNKNOWN", "The manifest refers to a blob that is not in this repository.",
                  missing.Select(d => new { digest = d }).ToArray());
            }
         }

         if (childRefs.Count > 0) {
            var wantedChildren = childRefs.Distinct().ToList();
            var foundChildren = await c.Db.Manifests
               .Where(m => m.cCtnImageId == imageId && wantedChildren.Contains(m.cCtnManifestDigest))
               .Select(m => m.cCtnManifestDigest).ToListAsync(c.Ct);
            var missing = wantedChildren.Except(foundChildren).ToArray();
            if (missing.Length > 0) {
               throw new CtnRegistryException(400, "MANIFEST_BLOB_UNKNOWN", "The manifest list refers to a manifest that is not in this repository.",
                  missing.Select(d => new { digest = d }).ToArray());
            }
         }

         var now = DateTime.UtcNow;
         await using var tx = await c.Db.Database.BeginTransactionAsync(c.Ct);

         var manifestId = await c.Db.Manifests
            .Where(m => m.cCtnImageId == imageId && m.cCtnManifestDigest == digest)
            .Select(m => m.cCtnManifestId).SingleOrDefaultAsync(c.Ct);
         if (manifestId is null) {
            manifestId = $"{Ulid.NewUlid()}";
            c.Db.Manifests.Add(new ta_CtnManifest {
               cCtnManifestId = manifestId,
               cCtnImageId = imageId,
               cCtnManifestDigest = digest,
               cCtnManifestMediaType = mediaType,
               cCtnManifestSize = content.Length,
               cCtnManifestContent = content,
               cCtnManifestPushedBy_cRobotId = c.Robot.cRobotId,
               ustamp = now,
               datestamp = now
            });
            var order = 0;
            foreach (var blob in blobRefs) {
               c.Db.ManifestBlobs.Add(new ta_CtnManifestBlob {
                  cCtnManifestId = manifestId,
                  cCtnManifestBlobOrder = order++,
                  cCtnBlobId = blobIds[blob.Digest],
                  cCtnManifestBlobRole = blob.Role
               });
            }

            await c.Db.SaveChangesAsync(c.Ct);
         }

         if (!isDigestReference) {
            var updated = await c.Db.Tags.Where(t => t.cCtnImageId == imageId && t.cCtnTagName == reference)
               .ExecuteUpdateAsync(s => s.SetProperty(t => t.cCtnManifestId, manifestId).SetProperty(t => t.ustamp, now), c.Ct);
            if (updated == 0) {
               c.Db.Tags.Add(new ta_CtnTag {
                  cCtnImageId = imageId, cCtnTagName = reference, cCtnManifestId = manifestId, ustamp = now, datestamp = now
               });
               await c.Db.SaveChangesAsync(c.Ct);
            }
         }

         await tx.CommitAsync(c.Ct);

         c.Http.Response.StatusCode = StatusCodes.Status201Created;
         c.Http.Response.Headers.Location = $"{c.BasePath}/{c.FullName}/manifests/{digest}";
         c.Http.Response.Headers["Docker-Content-Digest"] = digest;
         c.Http.Response.ContentLength = 0;
      }

      private static async Task DeleteManifestAsync(Call c) {
         var reference = c.Route.Reference!;
         var imageId = c.Image.cCtnImageId;
         if (CtnNames.IsValidDigest(reference)) {
            var manifest = await FindManifestAsync(c, reference) ??
                           throw new CtnRegistryException(404, "MANIFEST_UNKNOWN", $"Manifest '{reference}' is unknown.");
            if (await CtnManifestDeletion.FindReferencingIndexAsync(c.Db, imageId, manifest.cCtnManifestDigest, c.Ct) is { } index) {
               throw new CtnRegistryException(409, "DENIED", $"Manifest '{reference}' is referenced by index '{index}'; delete the index first.");
            }

            await CtnManifestDeletion.DeleteAsync(c.Db, manifest.cCtnManifestId, c.Ct);
         } else {
            var deleted = await c.Db.Tags.Where(t => t.cCtnImageId == imageId && t.cCtnTagName == reference).ExecuteDeleteAsync(c.Ct);
            if (deleted == 0) {
               throw new CtnRegistryException(404, "MANIFEST_UNKNOWN", $"Tag '{reference}' is unknown.");
            }
         }

         c.Http.Response.StatusCode = StatusCodes.Status202Accepted;
      }

      private record BlobRef(string Digest, string Role);

      // Membaca manifest image (config + layer) atau index/list (manifest anak). Yang lain - termasuk
      // schema 1 - ditolak: klien Docker modern tidak mengirimnya.
      private static (string MediaType, List<BlobRef> Blobs, List<string> Children) ParseManifest(byte[] content, string? contentType) {
         JsonDocument doc;
         try {
            doc = JsonDocument.Parse(content);
         } catch (JsonException) {
            throw new CtnRegistryException(400, "MANIFEST_INVALID", "The manifest is not valid JSON.");
         }

         using (doc) {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
               throw new CtnRegistryException(400, "MANIFEST_INVALID", "The manifest must be a JSON object.");
            }

            var headerType = contentType?.Split(';')[0].Trim();
            var bodyType = root.TryGetProperty("mediaType", out var mt) && mt.ValueKind == JsonValueKind.String ? mt.GetString() : null;
            if (!string.IsNullOrEmpty(headerType) && !string.IsNullOrEmpty(bodyType) && headerType != bodyType) {
               throw new CtnRegistryException(400, "MANIFEST_INVALID", "The Content-Type does not match the manifest's mediaType.");
            }

            var mediaType = !string.IsNullOrEmpty(headerType) ? headerType : bodyType;
            var blobs = new List<BlobRef>();
            var children = new List<string>();
            switch (mediaType) {
               case CtnNames.ManifestDockerV2 or CtnNames.ManifestOciImage:
                  if (!root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object) {
                     throw new CtnRegistryException(400, "MANIFEST_INVALID", "The manifest has no config.");
                  }

                  blobs.Add(new BlobRef(RequireDigest(config), "config"));
                  if (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array) {
                     foreach (var layer in layers.EnumerateArray()) {
                        // Layer asing (Windows) hidup di alamat luar; tidak ada blob yang perlu terhubung.
                        if (layer.TryGetProperty("urls", out var urls) && urls.ValueKind == JsonValueKind.Array && urls.GetArrayLength() > 0) {
                           continue;
                        }

                        blobs.Add(new BlobRef(RequireDigest(layer), "layer"));
                     }
                  }

                  break;
               case CtnNames.ManifestDockerList or CtnNames.ManifestOciIndex:
                  if (!root.TryGetProperty("manifests", out var manifests) || manifests.ValueKind != JsonValueKind.Array) {
                     throw new CtnRegistryException(400, "MANIFEST_INVALID", "The manifest list has no manifests.");
                  }

                  children.AddRange(manifests.EnumerateArray().Select(RequireDigest));
                  break;
               default:
                  throw new CtnRegistryException(400, "MANIFEST_INVALID", $"Unsupported manifest media type '{mediaType}'.");
            }

            return (mediaType, blobs, children);
         }
      }

      private static string RequireDigest(JsonElement element) {
         var digest = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
         return CtnNames.IsValidDigest(digest)
            ? digest!
            : throw new CtnRegistryException(400, "MANIFEST_INVALID", "The manifest refers to a missing or non-sha256 digest.");
      }

      private static async Task<byte[]> ReadLimitedAsync(HttpContext http, int limit) {
         using var buffer = new MemoryStream();
         var chunk = new byte[16 * 1024];
         int read;
         while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted)) > 0) {
            if (buffer.Length + read > limit) {
               throw new CtnRegistryException(400, "MANIFEST_INVALID", $"The manifest is larger than {limit / (1024 * 1024)} MiB.");
            }

            buffer.Write(chunk, 0, read);
         }

         return buffer.ToArray();
      }

      #endregion

      #region Blobs

      private static async Task GetBlobAsync(Call c) {
         var digest = c.Route.Reference!;
         if (!CtnNames.IsValidDigest(digest)) {
            throw new CtnRegistryException(400, "DIGEST_INVALID", $"'{digest}' is not a valid sha256 digest.");
         }

         var linked = await c.Db.BlobLinks.Where(l => l.cCtnImageId == c.Image.cCtnImageId)
            .AnyAsync(l => c.Db.Blobs.Any(b => b.cCtnBlobId == l.cCtnBlobId && b.cCtnBlobDigest == digest), c.Ct);
         var path = c.Store.BlobPath(digest);
         if (!linked || !File.Exists(path)) {
            throw new CtnRegistryException(404, "BLOB_UNKNOWN", $"Blob '{digest}' is unknown to this repository.");
         }

         c.Http.Response.Headers["Docker-Content-Digest"] = digest;
         // Range, If-Range, HEAD, 206 dan 416 ditangani oleh hasil file-nya.
         await Results.File(path, "application/octet-stream", enableRangeProcessing: true).ExecuteAsync(c.Http);
      }

      #endregion

      #region Uploads

      private static void DisableBodyLimit(HttpContext http) {
         var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
         if (feature is { IsReadOnly: false }) {
            feature.MaxRequestBodySize = null;
         }
      }

      private static void WriteUploadAccepted(Call c, string uploadId, long size) {
         var response = c.Http.Response;
         response.StatusCode = StatusCodes.Status202Accepted;
         response.Headers.Location = $"{c.BasePath}/{c.FullName}/blobs/uploads/{uploadId}";
         response.Headers["Docker-Upload-UUID"] = uploadId;
         response.Headers.Range = $"0-{Math.Max(size - 1, 0)}";
         response.ContentLength = 0;
      }

      private static async Task<ta_CtnUpload> FindUploadAsync(Call c) {
         var id = c.Route.UploadId!;
         var upload = id.Length == 26
            ? await c.Db.Uploads.SingleOrDefaultAsync(u =>
               u.cCtnUploadId == id && u.cCtnImageId == c.Image.cCtnImageId && u.cRobotId == c.Robot.cRobotId, c.Ct)
            : null;
         return upload is not null && File.Exists(c.Store.UploadPath(id))
            ? upload
            : throw new CtnRegistryException(404, "BLOB_UPLOAD_UNKNOWN", $"Upload '{id}' is unknown.");
      }

      private static async Task StartUploadAsync(Call c) {
         DisableBodyLimit(c.Http);
         var query = c.Http.Request.Query;

         if (await TryMountAsync(c, query["mount"].ToString(), query["from"].ToString())) return;

         var uploadId = $"{Ulid.NewUlid()}";
         c.Store.CreateUploadFile(uploadId);
         var now = DateTime.UtcNow;
         c.Db.Uploads.Add(new ta_CtnUpload {
            cCtnUploadId = uploadId, cCtnImageId = c.Image.cCtnImageId, cRobotId = c.Robot.cRobotId,
            cCtnUploadSize = 0, ustamp = now, datestamp = now
         });
         await c.Db.SaveChangesAsync(c.Ct);

         // Unggahan monolitik: POST ...?digest=... membawa seluruh isi sekaligus.
         if (query.ContainsKey("digest")) {
            await CompleteUploadAsync(c, uploadId, query["digest"].ToString());
            return;
         }

         WriteUploadAccepted(c, uploadId, 0);
      }

      // Mount lintas container: layer yang sudah ada di container sumber cukup ditautkan, tanpa unggah
      // ulang. Butuh hak pull di root sumber; kalau syaratnya tidak terpenuhi (sumber tidak ada, tidak
      // ada hak, blob tidak terhubung) jatuh ke unggahan biasa, sesuai spesifikasi, dan tidak membocorkan
      // apa pun tentang root yang tidak boleh dilihat.
      private static async Task<bool> TryMountAsync(Call c, string digest, string from) {
         if (!CtnNames.IsValidDigest(digest) || string.IsNullOrEmpty(from)) return false;

         var source = from.Split('/');
         if (source.Length != 2 ||
             !CtnNames.IsValidName(source[0], CtnNames.MaxRootName) ||
             !CtnNames.IsValidName(source[1], CtnNames.MaxImageName)) {
            return false;
         }

         var sourceImageId = await (
            from root in c.Db.Roots
            join right in c.Db.RobotRoots on root.cCtnRootId equals right.cCtnRootId
            join image in c.Db.Images on root.cCtnRootId equals image.cCtnRootId
            where root.cCtnRootName == source[0] && root.cCtnRootState == CtnNames.StateActive &&
                  right.cRobotId == c.Robot.cRobotId &&
                  image.cCtnImageName == source[1] && image.cCtnImageState == CtnNames.StateActive
            select image.cCtnImageId).SingleOrDefaultAsync(c.Ct);
         if (sourceImageId is null) return false;

         // Kunci menahan garbage collection selama blob sumber dicek dan ditautkan ke container ini.
         using (await CtnBlobGate.EnterAsync(c.Ct)) {
            var blobId = await c.Db.BlobLinks.Where(l => l.cCtnImageId == sourceImageId)
               .Join(c.Db.Blobs.Where(b => b.cCtnBlobDigest == digest), l => l.cCtnBlobId, b => b.cCtnBlobId, (_, b) => b.cCtnBlobId)
               .SingleOrDefaultAsync(c.Ct);
            if (blobId is null || !c.Store.BlobExists(digest)) return false;

            await LinkBlobAsync(c, blobId);
         }

         c.Http.Response.StatusCode = StatusCodes.Status201Created;
         c.Http.Response.Headers.Location = $"{c.BasePath}/{c.FullName}/blobs/{digest}";
         c.Http.Response.Headers["Docker-Content-Digest"] = digest;
         c.Http.Response.ContentLength = 0;
         return true;
      }

      private static async Task LinkBlobAsync(Call c, string blobId) {
         var imageId = c.Image.cCtnImageId;
         if (await c.Db.BlobLinks.AnyAsync(l => l.cCtnImageId == imageId && l.cCtnBlobId == blobId, c.Ct)) return;

         c.Db.BlobLinks.Add(new ta_CtnBlobLink { cCtnImageId = imageId, cCtnBlobId = blobId, datestamp = DateTime.UtcNow });
         try {
            await c.Db.SaveChangesAsync(c.Ct);
         } catch (DbUpdateException) {
            // Request lain menautkan blob yang sama lebih dulu; hasil akhirnya sama. Selain itu, gagal.
            c.Db.ChangeTracker.Clear();
            if (!await c.Db.BlobLinks.AnyAsync(l => l.cCtnImageId == imageId && l.cCtnBlobId == blobId, c.Ct)) throw;
         }
      }

      private static async Task PatchUploadAsync(Call c) {
         DisableBodyLimit(c.Http);
         var upload = await FindUploadAsync(c);
         var current = c.Store.UploadSize(upload.cCtnUploadId);

         // Content-Range ("start-end", kadang diawali "bytes ") bersifat opsional, tetapi kalau ada harus
         // menyambung persis di ujung yang sudah diterima.
         var range = c.Http.Request.Headers.ContentRange.ToString();
         if (range.Length > 0) {
            var text = range.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase) ? range[6..] : range;
            if (!long.TryParse(text.Split('-')[0], out var start) || start != current) {
               c.Http.Response.Headers.Range = $"0-{Math.Max(current - 1, 0)}";
               throw new CtnRegistryException(416, "BLOB_UPLOAD_INVALID", "The chunk does not continue at the end of the data received so far.");
            }
         }

         var size = await c.Store.AppendUploadAsync(upload.cCtnUploadId, c.Http.Request.Body, c.Ct);
         await c.Db.Uploads.Where(u => u.cCtnUploadId == upload.cCtnUploadId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.cCtnUploadSize, size).SetProperty(u => u.ustamp, DateTime.UtcNow), c.Ct);
         WriteUploadAccepted(c, upload.cCtnUploadId, size);
      }

      private static async Task FinishUploadAsync(Call c) {
         DisableBodyLimit(c.Http);
         var upload = await FindUploadAsync(c);
         await CompleteUploadAsync(c, upload.cCtnUploadId, c.Http.Request.Query["digest"].ToString());
      }

      // Menutup unggahan: isi sisa body (kalau ada), memastikan hash-nya sama dengan digest yang
      // dijanjikan klien, lalu memindahkan berkas jadi blob dan menautkannya ke container ini.
      private static async Task CompleteUploadAsync(Call c, string uploadId, string digest) {
         if (!CtnNames.IsValidDigest(digest)) {
            throw new CtnRegistryException(400, "DIGEST_INVALID", "A valid sha256 'digest' query parameter is required.");
         }

         if (c.Http.Request.ContentLength is null or > 0) {
            await c.Store.AppendUploadAsync(uploadId, c.Http.Request.Body, c.Ct);
         }

         var path = c.Store.UploadPath(uploadId);
         var actual = await CtnBlobStore.ComputeDigestAsync(path, c.Ct);
         if (actual != digest) {
            c.Store.DeleteUploadFile(uploadId);
            await c.Db.Uploads.Where(u => u.cCtnUploadId == uploadId).ExecuteDeleteAsync(CancellationToken.None);
            throw new CtnRegistryException(400, "DIGEST_INVALID", "The digest does not match the uploaded content.");
         }

         var size = new FileInfo(path).Length;
         // Kunci dipegang dari pemindahan berkas sampai tautan tersimpan, supaya garbage collection tidak menyapu blob ini di tengah jalan.
         using (await CtnBlobGate.EnterAsync(c.Ct)) {
            c.Store.CommitUpload(uploadId, digest);

            var blobId = await c.Db.Blobs.Where(b => b.cCtnBlobDigest == digest).Select(b => b.cCtnBlobId).SingleOrDefaultAsync(c.Ct);
            if (blobId is null) {
               var now = DateTime.UtcNow;
               blobId = $"{Ulid.NewUlid()}";
               c.Db.Blobs.Add(new ta_CtnBlob { cCtnBlobId = blobId, cCtnBlobDigest = digest, cCtnBlobSize = size, ustamp = now, datestamp = now });
               try {
                  await c.Db.SaveChangesAsync(c.Ct);
               } catch (DbUpdateException) {
                  // Kemungkinan besar blob yang sama dicatat request lain di saat yang sama; ambil miliknya.
                  c.Db.ChangeTracker.Clear();
                  blobId = await c.Db.Blobs.Where(b => b.cCtnBlobDigest == digest).Select(b => b.cCtnBlobId).SingleOrDefaultAsync(c.Ct) ?? throw new CtnRegistryException(500, "UNKNOWN", "The blob could not be recorded.");
               }
            }

            await LinkBlobAsync(c, blobId);
         }

         await c.Db.Uploads.Where(u => u.cCtnUploadId == uploadId).ExecuteDeleteAsync(c.Ct);

         c.Http.Response.StatusCode = StatusCodes.Status201Created;
         c.Http.Response.Headers.Location = $"{c.BasePath}/{c.FullName}/blobs/{digest}";
         c.Http.Response.Headers["Docker-Content-Digest"] = digest;
         c.Http.Response.ContentLength = 0;
      }

      private static async Task UploadStatusAsync(Call c) {
         var upload = await FindUploadAsync(c);
         var size = c.Store.UploadSize(upload.cCtnUploadId);
         c.Http.Response.StatusCode = StatusCodes.Status204NoContent;
         c.Http.Response.Headers["Docker-Upload-UUID"] = upload.cCtnUploadId;
         c.Http.Response.Headers.Range = $"0-{Math.Max(size - 1, 0)}";
      }

      private static async Task CancelUploadAsync(Call c) {
         var upload = await FindUploadAsync(c);
         c.Store.DeleteUploadFile(upload.cCtnUploadId);
         await c.Db.Uploads.Where(u => u.cCtnUploadId == upload.cCtnUploadId).ExecuteDeleteAsync(c.Ct);
         c.Http.Response.StatusCode = StatusCodes.Status204NoContent;
      }

      #endregion
   }
}
