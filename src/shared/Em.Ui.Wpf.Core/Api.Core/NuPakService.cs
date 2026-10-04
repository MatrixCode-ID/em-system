using Em;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Api.Core.Models;
namespace Em.Api.Core;
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class NuPakService(EmApp app) : ServiceWpfBase(app), INuPakServices {
 public Task<StorageFeatureStatus> GetMeta_NuPakStorageStatus() => GetAsync<StorageFeatureStatus>(nameof(GetMeta_NuPakStorageStatus));
 public Task<StorageSettingsDetail> GetMeta_NuPakSettings() => GetAsync<StorageSettingsDetail>(nameof(GetMeta_NuPakSettings));
 public Task<StorageDirectoryValidation> PostGetMeta_NuPakValidateDirectory(StorageFeatureSettings draft) => PostAsync<StorageDirectoryValidation>(nameof(PostGetMeta_NuPakValidateDirectory),draft);
 public Task<StorageSettingsDetail> PostGetMeta_NuPakSettingsSave(StorageSettingsSave request) => PostAsync<StorageSettingsDetail>(nameof(PostGetMeta_NuPakSettingsSave),request);
 public Task<NuPakStatus> GetMeta_NuPakStatus() => GetAsync<NuPakStatus>(nameof(GetMeta_NuPakStatus));
 public Task<NuPakStatus> PostGetMeta_NuPakSetEnabled(bool enabled) => PostAsync<NuPakStatus>(nameof(PostGetMeta_NuPakSetEnabled) ,enabled);
 public Task<NuPakFeedInfo[]> GetMeta_NuPakFeeds() => GetAsync<NuPakFeedInfo[]>(nameof(GetMeta_NuPakFeeds));
 public Task<NuPakFeedInfo> GetMeta_NuPakFeed(string feedId) => GetAsync<NuPakFeedInfo>(nameof(GetMeta_NuPakFeed) ,feedId);
 public Task<NuPakFeedInfo> PostGetMeta_NuPakFeedCreate(string slug, string name, string? description) => PostAsync<NuPakFeedInfo>(nameof(PostGetMeta_NuPakFeedCreate) ,slug, name, description!);
 public Task<NuPakFeedInfo> PostGetMeta_NuPakFeedUpdate(string feedId, string name, string? description, bool enabled, bool anonymousRead) => PostAsync<NuPakFeedInfo>(nameof(PostGetMeta_NuPakFeedUpdate) ,feedId, name, description!, enabled, anonymousRead);
 public Task PostMeta_NuPakFeedDelete(string feedId) => PostAsync(nameof(PostMeta_NuPakFeedDelete) ,feedId);
 public Task<NuPakStorageInfo> GetMeta_NuPakFeedStorageSize(string feedId) => GetAsync<NuPakStorageInfo>(nameof(GetMeta_NuPakFeedStorageSize) ,feedId);
 public Task<ta_NuPakAudit[]> GetMeta_NuPakServerAudit(NuPakAuditFilter filter, int skip, int take) => GetAsync<ta_NuPakAudit[]>(nameof(GetMeta_NuPakServerAudit) ,filter, skip, take);
 public Task<NuPakStorageInfo> GetMeta_NuPakStorageSize() => GetAsync<NuPakStorageInfo>(nameof(GetMeta_NuPakStorageSize));
 public Task<NuPakPrefixInfo[]> GetMeta_NuPakPrefixes(string feedId) => GetAsync<NuPakPrefixInfo[]>(nameof(GetMeta_NuPakPrefixes) ,feedId);
 public Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixCreate(string feedId, string name, string? description) => PostAsync<NuPakPrefixInfo>(nameof(PostGetMeta_NuPakPrefixCreate) ,feedId, name, description!);
 public Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixUpdate(string feedId, string prefixId, string name, string? description, bool active) => PostAsync<NuPakPrefixInfo>(nameof(PostGetMeta_NuPakPrefixUpdate) ,feedId, prefixId, name, description!, active);
 public Task PostMeta_NuPakPrefixDelete(string feedId, string prefixId) => PostAsync(nameof(PostMeta_NuPakPrefixDelete) ,feedId, prefixId);
 public Task<NuPakAccessInfo[]> GetMeta_NuPakPrefixAccess(string feedId, string prefixId) => GetAsync<NuPakAccessInfo[]>(nameof(GetMeta_NuPakPrefixAccess) ,feedId, prefixId);
 public Task<NuPakPackageInfo[]> GetMeta_NuPakPackages(string feedId, string prefixId, string? search, int skip, int take) => GetAsync<NuPakPackageInfo[]>(nameof(GetMeta_NuPakPackages) ,feedId, prefixId, search!, skip, take);
 public Task<NuPakVersionInfo[]> GetMeta_NuPakVersions(string feedId, string packageId) => GetAsync<NuPakVersionInfo[]>(nameof(GetMeta_NuPakVersions) ,feedId, packageId);
 public Task PostMeta_NuPakVersionRecycle(string feedId, string versionId) => PostAsync(nameof(PostMeta_NuPakVersionRecycle) ,feedId, versionId);
 public Task PostMeta_NuPakVersionRestore(string feedId, string versionId) => PostAsync(nameof(PostMeta_NuPakVersionRestore) ,feedId, versionId);
 public Task<NuPakVersionInfo[]> GetMeta_NuPakRecycleBin(string feedId, string? prefixId, int skip, int take) => GetAsync<NuPakVersionInfo[]>(nameof(GetMeta_NuPakRecycleBin) ,feedId, prefixId!, skip, take);
 public Task PostMeta_NuPakVersionPurge(string feedId, string versionId) => PostAsync(nameof(PostMeta_NuPakVersionPurge) ,feedId, versionId);
 public Task<NuPakEmptyResult> PostMeta_NuPakRecycleBinEmpty(string feedId, string? prefixId) => PostAsync<NuPakEmptyResult>(nameof(PostMeta_NuPakRecycleBinEmpty) ,feedId, prefixId!);
 public Task<ta_NuPakAudit[]> GetMeta_NuPakAudit(string feedId, NuPakAuditFilter filter, int skip, int take) => GetAsync<ta_NuPakAudit[]>(nameof(GetMeta_NuPakAudit) ,feedId, filter, skip, take);
}
