using Em.Api.Core.Models;

namespace Em.Api.Core.ObjectStorage;

internal sealed class SwitchedObjectStore(ObjectStore store, FeatureServiceRegistry registry) : IObjectStore {
   public async Task<StoreObjectInfo> PutAsync(string bucket, string key, Stream content, string? mime = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.PutAsync(bucket, key, content, mime, metadata, ct);
   }
   public async Task<StoreObjectRead> OpenAsync(string bucket, string key, int? version = null, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.OpenAsync(bucket, key, version, ct);
   }
   public async Task<StoreObjectInfo?> GetAsync(string bucket, string key, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.GetAsync(bucket, key, ct);
   }
   public async Task<StoreObjectInfo> DeleteAsync(string bucket, string key, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.DeleteAsync(bucket, key, ct);
   }
   public async Task<StoreObjectInfo> RestoreAsync(string bucket, string key, int version, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.RestoreAsync(bucket, key, version, ct);
   }
   public async Task PurgeVersionAsync(string bucket, string key, int version, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      await store.PurgeVersionAsync(bucket, key, version, ct);
   }
   public async Task<StoreObjectListing> ListAsync(string bucket, string? prefix = null, string? delimiter = null, string? continuation = null, int limit = 1000, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.ListAsync(bucket, prefix, delimiter, continuation, limit, ct);
   }
   public async Task<StoreObjectVersionInfo[]> VersionsAsync(string bucket, string key, CancellationToken ct = default) {
      await registry.Get("ObjectStorage").EnsureAvailableAsync();
      return await store.VersionsAsync(bucket, key, ct);
   }
}
