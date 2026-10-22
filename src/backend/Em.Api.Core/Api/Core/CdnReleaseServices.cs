using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core;

/// <summary>Claim-gated access to product-owned release folders.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class CdnReleaseServices : ServicesBase, ICdnReleaseServices {
   private CdnStore Store {
      get { var store = GetService<CdnStore>()!; store.EnsureEnabled(); return store; }
   }
   /// <inheritdoc />
   [GetAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task<CdnFolderContent> GetMeta_CdnReleaseFolder(string? path) => Task.FromResult(Store.List(path));
   /// <inheritdoc />
   [GetAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task<CdnEntry[]> GetMeta_CdnReleaseTree(string path, string appId) {
      Store.EnsureWritable(path, appId);
      return Task.FromResult(Store.Tree(path));
   }
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task<CdnManagedFolder> PostGetMeta_CdnReleaseManage(string path, string appId) => Task.FromResult(Store.Manage(path, appId));
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task PostMeta_CdnReleaseUnmanage(string path, string appId) { Store.Unmanage(path, appId); return Task.CompletedTask; }
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task PostMeta_CdnReleasePreflight(CdnSyncPlan plan) { Store.Preflight(plan); return Task.CompletedTask; }
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task<CdnEntry> PostGetMeta_CdnReleaseCreateFolder(string? path, string name, string appId) => Task.FromResult(Store.CreateFolder(path, name, appId));
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task<CdnEntry> PostGetMeta_CdnReleaseUpload(CdnReleaseUploadRequest request, Stream content) =>
      Store.UploadAsync(request.Upload.Path, request.Upload.FileName, content, request.Upload.Overwrite, AbortToken, request.AppId);
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task PostMeta_CdnReleaseDelete(string path, string appId) { Store.Delete(path, appId); return Task.CompletedTask; }
   /// <inheritdoc />
   [PostAction(claim: ICdnReleaseServices.ReleaseClaim)]
   public Task PostMeta_CdnReleaseFolderSettings(CdnReleaseFolderSettingsRequest request) {
      ArgumentNullException.ThrowIfNull(request);
      Store.SaveFolderSettings(request.Path, request.Settings, request.AppId);
      return Task.CompletedTask;
   }
}
