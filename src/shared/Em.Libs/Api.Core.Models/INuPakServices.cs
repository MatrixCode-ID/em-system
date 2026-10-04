using Em.Shared;
namespace Em.Api.Core.Models;

public interface INuPakServices : IServices
{
   Task<StorageFeatureStatus> GetMeta_NuPakStorageStatus();
   Task<StorageSettingsDetail> GetMeta_NuPakSettings();
   Task<StorageDirectoryValidation> PostGetMeta_NuPakValidateDirectory(StorageFeatureSettings draft);
   Task<StorageSettingsDetail> PostGetMeta_NuPakSettingsSave(StorageSettingsSave request);
   const string ManagerClaim = "NuGet Manager Access";
   const string SettingsClaim = "NuGet Settings Manage";
   Task<NuPakStatus> GetMeta_NuPakStatus();
   Task<NuPakStatus> PostGetMeta_NuPakSetEnabled(bool enabled);
   Task<NuPakFeedInfo[]> GetMeta_NuPakFeeds();
   Task<NuPakFeedInfo> GetMeta_NuPakFeed(string feedId);
   Task<NuPakFeedInfo> PostGetMeta_NuPakFeedCreate(string slug, string name, string? description);
   Task<NuPakFeedInfo> PostGetMeta_NuPakFeedUpdate(string feedId, string name, string? description, bool enabled, bool anonymousRead);
   Task PostMeta_NuPakFeedDelete(string feedId);
   Task<NuPakStorageInfo> GetMeta_NuPakFeedStorageSize(string feedId);
   Task<ta_NuPakAudit[]> GetMeta_NuPakServerAudit(NuPakAuditFilter filter, int skip, int take);
   Task<NuPakStorageInfo> GetMeta_NuPakStorageSize();
   Task<NuPakPrefixInfo[]> GetMeta_NuPakPrefixes(string feedId);
   Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixCreate(string feedId, string name, string? description);
   Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixUpdate(string feedId, string prefixId, string name, string? description, bool active);
   Task PostMeta_NuPakPrefixDelete(string feedId, string prefixId);
   Task<NuPakAccessInfo[]> GetMeta_NuPakPrefixAccess(string feedId, string prefixId);
   Task<NuPakPackageInfo[]> GetMeta_NuPakPackages(string feedId, string prefixId, string? search, int skip, int take);
   Task<NuPakVersionInfo[]> GetMeta_NuPakVersions(string feedId, string packageId);
   Task PostMeta_NuPakVersionRecycle(string feedId, string versionId);
   Task PostMeta_NuPakVersionRestore(string feedId, string versionId);
   Task<NuPakVersionInfo[]> GetMeta_NuPakRecycleBin(string feedId, string? prefixId, int skip, int take);
   Task PostMeta_NuPakVersionPurge(string feedId, string versionId);
   Task<NuPakEmptyResult> PostMeta_NuPakRecycleBinEmpty(string feedId, string? prefixId);
   Task<ta_NuPakAudit[]> GetMeta_NuPakAudit(string feedId, NuPakAuditFilter filter, int skip, int take);
}
public sealed record NuPakStatus(bool Enabled, int Feeds, int EffectiveFeeds, int Prefixes, int Packages, int Versions, int RecycledVersions);
public sealed record NuPakFeedInfo(string Id, string Slug, string Name, string? Description, bool Enabled, bool AnonymousRead,
   bool EffectiveEnabled, int Prefixes, int Grants, int Packages, int Versions, int RecycledVersions) {
   public string ServiceIndex => $"/nuget/{Slug}/v3/index.json";
   public override string ToString() => $"{Name} / {Slug}";
}
public sealed record NuPakStorageInfo(long ActiveBytes, long RecycleBytes, int Packages, int Versions) {
   public long TotalBytes => ActiveBytes + RecycleBytes;
}
public sealed record NuPakPrefixInfo(string Id, string Name, string? Description, bool Active, int Packages, long Bytes) {
   public override string ToString() => $"{Name} · {Packages:N0} packages · {Bytes:N0} B";
}
public sealed record NuPakPackageInfo(string Id, string Name, int Versions, long Bytes, string? LatestVersion) {
   public override string ToString() => $"{Name} {LatestVersion} · {Versions} versions";
}
public sealed record NuPakVersionInfo(string Id, string PackageId, string PackageName, string Version, string Original, bool Prerelease,
   int State, long Bytes, string Hash, string? PushedBy, DateTime PushedAt, DateTime? RecycledAt) {
   public override string ToString() => $"{PackageName} {Version} · {Bytes:N0} B · {PushedBy ?? "Deleted robot"} · {PushedAt:u}";
}
public sealed record NuPakAccessInfo(string RobotId, string RobotName, string Access) {
   public override string ToString() => $"{RobotName}: {Access}";
}
public sealed record NuPakEmptyResult(int Count, long Bytes, int Failed);
public sealed record NuPakAuditFilter {
   public string? Package { get; init; }
   public string? Version { get; init; }
   public string? Actor { get; init; }
   public string? Action { get; init; }
   public string? Result { get; init; }
   public DateTime? From { get; init; }
   public DateTime? To { get; init; }
}
