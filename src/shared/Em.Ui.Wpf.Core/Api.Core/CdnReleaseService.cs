using System.IO;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Api.Core;

/// <summary>Client proxy for Release Manager's owned CDN operations.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class CdnReleaseService(EmApp app) : ServiceWpfBase(app), ICdnReleaseServices {
   /// <inheritdoc />
   public Task<CdnFolderContent> GetMeta_CdnReleaseFolder(string? path) => GetAsync<CdnFolderContent>(nameof(GetMeta_CdnReleaseFolder), path!);
   /// <inheritdoc />
   public Task<CdnEntry[]> GetMeta_CdnReleaseTree(string path, string appId) => GetAsync<CdnEntry[]>(nameof(GetMeta_CdnReleaseTree), path, appId);
   /// <inheritdoc />
   public Task<CdnManagedFolder> PostGetMeta_CdnReleaseManage(string path, string appId) => PostAsync<CdnManagedFolder>(nameof(PostGetMeta_CdnReleaseManage), path, appId);
   /// <inheritdoc />
   public Task PostMeta_CdnReleaseUnmanage(string path, string appId) => PostAsync(nameof(PostMeta_CdnReleaseUnmanage), path, appId);
   /// <inheritdoc />
   public Task PostMeta_CdnReleasePreflight(CdnSyncPlan plan) => PostAsync(nameof(PostMeta_CdnReleasePreflight), plan);
   /// <inheritdoc />
   public Task<CdnEntry> PostGetMeta_CdnReleaseCreateFolder(string? path, string name, string appId) => PostAsync<CdnEntry>(nameof(PostGetMeta_CdnReleaseCreateFolder), path!, name, appId);
   /// <inheritdoc />
   public Task<CdnEntry> PostGetMeta_CdnReleaseUpload(CdnReleaseUploadRequest request, Stream content) => PostStreamAsync<CdnEntry>(nameof(PostGetMeta_CdnReleaseUpload), content, request);
   /// <inheritdoc />
   public Task PostMeta_CdnReleaseDelete(string path, string appId) => PostAsync(nameof(PostMeta_CdnReleaseDelete), path, appId);
   /// <inheritdoc />
   public Task PostMeta_CdnReleaseFolderSettings(CdnReleaseFolderSettingsRequest request) => PostAsync(nameof(PostMeta_CdnReleaseFolderSettings), request);
}
