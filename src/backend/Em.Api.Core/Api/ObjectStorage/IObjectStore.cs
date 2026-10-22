using Em.Api.Core.Models;

namespace Em.Api.Core.ObjectStorage;

/// <summary>
/// The object storage engine for server-side code: the single path through which versions, policies and
/// quotas are applied. Modules take it from dependency injection (it is scoped); the management actions,
/// the public <c>/store</c> endpoint and a future S3 adapter all use it as well.
/// </summary>
/// <remarks>
/// Buckets are addressed by name and must already exist (they are created by an administrator in the Object
/// Storage Manager or through <see cref="IObjectStorageServices.PostGetMeta_StoreBucketSave"/>). Failures
/// are <see cref="Em.Shared.ActionException"/> with an HTTP status: 400 for invalid names, 404 for a missing
/// bucket, object or version, 409 for quota or read-only conflicts, 413 for content above the policy's
/// maximum object size and 503 when not enough volumes are available or the service is disabled.
/// The injected module-facing service throws <see cref="Em.Shared.ServiceDisabledException"/> while disabled;
/// management actions and maintenance retain access to the underlying engine.
/// </remarks>
public interface IObjectStore
{
   /// <summary>Stores <paramref name="content"/> as a new current version of <paramref name="key"/>.</summary>
   /// <param name="bucket">Bucket name.</param>
   /// <param name="key">Object key.</param>
   /// <param name="content">Content, read from its current position to the end.</param>
   /// <param name="mime">MIME type; <c>null</c> stores <c>application/octet-stream</c>.</param>
   /// <param name="metadata">User metadata of the new version.</param>
   /// <param name="ct">Cancellation.</param>
   Task<StoreObjectInfo> PutAsync(string bucket, string key, Stream content, string? mime = null,
      IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default);

   /// <summary>Opens the content of the current version, or of <paramref name="version"/> when given. Dispose the result.</summary>
   Task<StoreObjectRead> OpenAsync(string bucket, string key, int? version = null, CancellationToken ct = default);

   /// <summary>The object with its current version, also when that is a delete marker; <c>null</c> when the key has no history.</summary>
   Task<StoreObjectInfo?> GetAsync(string bucket, string key, CancellationToken ct = default);

   /// <summary>Adds a delete marker as the current version. Deleting an already deleted object changes nothing.</summary>
   Task<StoreObjectInfo> DeleteAsync(string bucket, string key, CancellationToken ct = default);

   /// <summary>Adds a new current version that points to the content of <paramref name="version"/>.</summary>
   Task<StoreObjectInfo> RestoreAsync(string bucket, string key, int version, CancellationToken ct = default);

   /// <summary>
   /// Permanently deletes one version. When it was current, the newest remaining version that is not a delete
   /// marker becomes current; when no version remains, the object is removed.
   /// </summary>
   Task PurgeVersionAsync(string bucket, string key, int version, CancellationToken ct = default);

   /// <summary>Lists objects that are not deleted, ordered by key; see <see cref="IObjectStorageServices.GetMeta_StoreObjects"/>.</summary>
   Task<StoreObjectListing> ListAsync(string bucket, string? prefix = null, string? delimiter = null, string? continuation = null,
      int limit = 1000, CancellationToken ct = default);

   /// <summary>Every version of an object, newest first (404 when the key has no history).</summary>
   Task<StoreObjectVersionInfo[]> VersionsAsync(string bucket, string key, CancellationToken ct = default);
}

/// <summary>Content of one object version opened for reading.</summary>
/// <param name="Info">The object and the version being read.</param>
/// <param name="Content">Seekable content stream; owned by the caller.</param>
/// <param name="Metadata">User metadata of the version.</param>
public sealed record StoreObjectRead(StoreObjectInfo Info, Stream Content, Dictionary<string, string>? Metadata) : IAsyncDisposable
{
   /// <inheritdoc />
   public ValueTask DisposeAsync() => Content.DisposeAsync();
}
