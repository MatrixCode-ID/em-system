using Em;
using Em.Shared;
using Em.Ui.Wpf.Core;
using Em.Api.Core.Models;
namespace Em.Api.Core;
/// <summary>Client-side implementation of the NuPak management actions.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed class NuPakService(EmApp app) : ServiceWpfBase(app), INuPakServices {
 /// <inheritdoc />
 public Task<StorageFeatureStatus> GetMeta_NuPakStorageStatus() => GetAsync<StorageFeatureStatus>(nameof(GetMeta_NuPakStorageStatus));
 /// <inheritdoc />
 public Task<StorageSettingsDetail> GetMeta_NuPakSettings() => GetAsync<StorageSettingsDetail>(nameof(GetMeta_NuPakSettings));
 /// <inheritdoc />
 public Task<StorageDirectoryValidation> PostGetMeta_NuPakValidateDirectory(StorageFeatureSettings draft) => PostAsync<StorageDirectoryValidation>(nameof(PostGetMeta_NuPakValidateDirectory),draft);
 /// <inheritdoc />
 public Task<StorageSettingsDetail> PostGetMeta_NuPakSettingsSave(StorageSettingsSave request) => PostAsync<StorageSettingsDetail>(nameof(PostGetMeta_NuPakSettingsSave),request);
 /// <inheritdoc />
 public Task<NuPakStatus> GetMeta_NuPakStatus() => GetAsync<NuPakStatus>(nameof(GetMeta_NuPakStatus));
 /// <inheritdoc />
 public Task<NuPakStatus> PostGetMeta_NuPakSetEnabled(bool enabled) => PostAsync<NuPakStatus>(nameof(PostGetMeta_NuPakSetEnabled) ,enabled);
 /// <inheritdoc />
 public Task<NuPakFeedInfo[]> GetMeta_NuPakFeeds() => GetAsync<NuPakFeedInfo[]>(nameof(GetMeta_NuPakFeeds));
 /// <inheritdoc />
 public Task<NuPakFeedInfo> GetMeta_NuPakFeed(string feedId) => GetAsync<NuPakFeedInfo>(nameof(GetMeta_NuPakFeed) ,feedId);
 /// <inheritdoc />
 public Task<NuPakFeedInfo> PostGetMeta_NuPakFeedCreate(string slug, string name, string? description) => PostAsync<NuPakFeedInfo>(nameof(PostGetMeta_NuPakFeedCreate) ,slug, name, description!);
 /// <inheritdoc />
 public Task<NuPakFeedInfo> PostGetMeta_NuPakFeedUpdate(string feedId, string name, string? description, bool enabled, bool anonymousRead) => PostAsync<NuPakFeedInfo>(nameof(PostGetMeta_NuPakFeedUpdate) ,feedId, name, description!, enabled, anonymousRead);
 /// <inheritdoc />
 public Task PostMeta_NuPakFeedDelete(string feedId) => PostAsync(nameof(PostMeta_NuPakFeedDelete) ,feedId);
 /// <inheritdoc />
 public Task<NuPakStorageInfo> GetMeta_NuPakFeedStorageSize(string feedId) => GetAsync<NuPakStorageInfo>(nameof(GetMeta_NuPakFeedStorageSize) ,feedId);
 /// <inheritdoc />
 public Task<ta_NuPakAudit[]> GetMeta_NuPakServerAudit(NuPakAuditFilter filter, int skip, int take) => GetAsync<ta_NuPakAudit[]>(nameof(GetMeta_NuPakServerAudit) ,filter, skip, take);
 /// <inheritdoc />
 public Task<NuPakStorageInfo> GetMeta_NuPakStorageSize() => GetAsync<NuPakStorageInfo>(nameof(GetMeta_NuPakStorageSize));
 /// <inheritdoc />
 public Task<NuPakPrefixInfo[]> GetMeta_NuPakPrefixes(string feedId) => GetAsync<NuPakPrefixInfo[]>(nameof(GetMeta_NuPakPrefixes) ,feedId);
 /// <inheritdoc />
 public Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixCreate(string feedId, string name, string? description) => PostAsync<NuPakPrefixInfo>(nameof(PostGetMeta_NuPakPrefixCreate) ,feedId, name, description!);
 /// <inheritdoc />
 public Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixUpdate(string feedId, string prefixId, string name, string? description, bool active) => PostAsync<NuPakPrefixInfo>(nameof(PostGetMeta_NuPakPrefixUpdate) ,feedId, prefixId, name, description!, active);
 /// <inheritdoc />
 public Task PostMeta_NuPakPrefixDelete(string feedId, string prefixId) => PostAsync(nameof(PostMeta_NuPakPrefixDelete) ,feedId, prefixId);
 /// <inheritdoc />
 public Task<NuPakAccessInfo[]> GetMeta_NuPakPrefixAccess(string feedId, string prefixId) => GetAsync<NuPakAccessInfo[]>(nameof(GetMeta_NuPakPrefixAccess) ,feedId, prefixId);
 /// <inheritdoc />
 public Task<NuPakPackageInfo[]> GetMeta_NuPakPackages(string feedId, string prefixId, string? search, int skip, int take) => GetAsync<NuPakPackageInfo[]>(nameof(GetMeta_NuPakPackages) ,feedId, prefixId, search!, skip, take);
 /// <inheritdoc />
 public Task<NuPakVersionInfo[]> GetMeta_NuPakVersions(string feedId, string packageId) => GetAsync<NuPakVersionInfo[]>(nameof(GetMeta_NuPakVersions) ,feedId, packageId);
 /// <inheritdoc />
 public Task PostMeta_NuPakVersionRecycle(string feedId, string versionId) => PostAsync(nameof(PostMeta_NuPakVersionRecycle) ,feedId, versionId);
 /// <inheritdoc />
 public Task PostMeta_NuPakVersionRestore(string feedId, string versionId) => PostAsync(nameof(PostMeta_NuPakVersionRestore) ,feedId, versionId);
 /// <inheritdoc />
 public Task<NuPakVersionInfo[]> GetMeta_NuPakRecycleBin(string feedId, string? prefixId, int skip, int take) => GetAsync<NuPakVersionInfo[]>(nameof(GetMeta_NuPakRecycleBin) ,feedId, prefixId!, skip, take);
 /// <inheritdoc />
 public Task PostMeta_NuPakVersionPurge(string feedId, string versionId) => PostAsync(nameof(PostMeta_NuPakVersionPurge) ,feedId, versionId);
 /// <inheritdoc />
 public Task<NuPakEmptyResult> PostMeta_NuPakRecycleBinEmpty(string feedId, string? prefixId) => PostAsync<NuPakEmptyResult>(nameof(PostMeta_NuPakRecycleBinEmpty) ,feedId, prefixId!);
 /// <inheritdoc />
 public Task<ta_NuPakAudit[]> GetMeta_NuPakAudit(string feedId, NuPakAuditFilter filter, int skip, int take) => GetAsync<ta_NuPakAudit[]>(nameof(GetMeta_NuPakAudit) ,feedId, filter, skip, take);
}
