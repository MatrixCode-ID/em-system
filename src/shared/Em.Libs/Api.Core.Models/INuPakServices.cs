using Em.Shared;
namespace Em.Api.Core.Models;

/// <summary>
/// NuGet server (NuPak) management: feeds, prefixes, packages, the recycle bin, audit and storage settings.
/// Requires claim <see cref="ManagerClaim"/>; storage settings require <see cref="SettingsClaim"/>.
/// </summary>
public interface INuPakServices : IServices
{
   /// <summary>Whether NuPak storage is enabled, managed from the UI, and waiting for a restart.</summary>
   Task<StorageFeatureStatus> GetMeta_NuPakStorageStatus();

   /// <summary>Current NuPak storage settings.</summary>
   Task<StorageSettingsDetail> GetMeta_NuPakSettings();

   /// <summary>Validates a storage directory without saving it.</summary>
   Task<StorageDirectoryValidation> PostGetMeta_NuPakValidateDirectory(StorageFeatureSettings draft);

   /// <summary>Saves the NuPak storage settings; they take effect after the API restarts.</summary>
   Task<StorageSettingsDetail> PostGetMeta_NuPakSettingsSave(StorageSettingsSave request);

   /// <summary>Claim name that opens NuGet management, written without its module name.</summary>
   const string ManagerClaim = "NuGet Manager Access";

   /// <summary>Claim name that allows changing the NuPak storage settings, written without its module name.</summary>
   const string SettingsClaim = "NuGet Settings Manage";

   /// <summary>Server-wide NuPak state and counters.</summary>
   Task<NuPakStatus> GetMeta_NuPakStatus();

   /// <summary>Turns the NuGet server on or off and returns the new state.</summary>
   Task<NuPakStatus> PostGetMeta_NuPakSetEnabled(bool enabled);

   /// <summary>Every feed with its counters.</summary>
   Task<NuPakFeedInfo[]> GetMeta_NuPakFeeds();

   /// <summary>One feed by ID.</summary>
   Task<NuPakFeedInfo> GetMeta_NuPakFeed(string feedId);

   /// <summary>Creates a feed; <paramref name="slug"/> becomes part of its URL.</summary>
   Task<NuPakFeedInfo> PostGetMeta_NuPakFeedCreate(string slug, string name, string? description);

   /// <summary>Changes the name, description, enabled state and anonymous read access of a feed.</summary>
   Task<NuPakFeedInfo> PostGetMeta_NuPakFeedUpdate(string feedId, string name, string? description, bool enabled, bool anonymousRead);

   /// <summary>Deletes a feed.</summary>
   Task PostMeta_NuPakFeedDelete(string feedId);

   /// <summary>Storage used by one feed.</summary>
   Task<NuPakStorageInfo> GetMeta_NuPakFeedStorageSize(string feedId);

   /// <summary>Server-wide audit rows matching <paramref name="filter"/>, newest first.</summary>
   Task<ta_NuPakAudit[]> GetMeta_NuPakServerAudit(NuPakAuditFilter filter, int skip, int take);

   /// <summary>Storage used by every feed together.</summary>
   Task<NuPakStorageInfo> GetMeta_NuPakStorageSize();

   /// <summary>Prefixes of a feed.</summary>
   Task<NuPakPrefixInfo[]> GetMeta_NuPakPrefixes(string feedId);

   /// <summary>Creates a package ID prefix in a feed.</summary>
   Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixCreate(string feedId, string name, string? description);

   /// <summary>Changes the name, description and active state of a prefix.</summary>
   Task<NuPakPrefixInfo> PostGetMeta_NuPakPrefixUpdate(string feedId, string prefixId, string name, string? description, bool active);

   /// <summary>Deletes a prefix.</summary>
   Task PostMeta_NuPakPrefixDelete(string feedId, string prefixId);

   /// <summary>Robot grants on a prefix.</summary>
   Task<NuPakAccessInfo[]> GetMeta_NuPakPrefixAccess(string feedId, string prefixId);

   /// <summary>Packages under a prefix, optionally filtered by <paramref name="search"/>.</summary>
   Task<NuPakPackageInfo[]> GetMeta_NuPakPackages(string feedId, string prefixId, string? search, int skip, int take);

   /// <summary>Versions of a package.</summary>
   Task<NuPakVersionInfo[]> GetMeta_NuPakVersions(string feedId, string packageId);

   /// <summary>Moves a version to the recycle bin.</summary>
   Task PostMeta_NuPakVersionRecycle(string feedId, string versionId);

   /// <summary>Restores a version from the recycle bin.</summary>
   Task PostMeta_NuPakVersionRestore(string feedId, string versionId);

   /// <summary>Versions in the recycle bin of a feed, optionally limited to one prefix.</summary>
   Task<NuPakVersionInfo[]> GetMeta_NuPakRecycleBin(string feedId, string? prefixId, int skip, int take);

   /// <summary>Permanently deletes a recycled version.</summary>
   Task PostMeta_NuPakVersionPurge(string feedId, string versionId);

   /// <summary>Permanently deletes every recycled version of a feed, optionally limited to one prefix.</summary>
   Task<NuPakEmptyResult> PostMeta_NuPakRecycleBinEmpty(string feedId, string? prefixId);

   /// <summary>Audit rows of one feed matching <paramref name="filter"/>, newest first.</summary>
   Task<ta_NuPakAudit[]> GetMeta_NuPakAudit(string feedId, NuPakAuditFilter filter, int skip, int take);
}

/// <summary>Server-wide NuPak state and counters.</summary>
/// <param name="Enabled">The NuGet server is on.</param>
/// <param name="Feeds">Number of feeds.</param>
/// <param name="EffectiveFeeds">Number of feeds actually served (enabled while the server is on).</param>
/// <param name="Prefixes">Number of prefixes.</param>
/// <param name="Packages">Number of packages.</param>
/// <param name="Versions">Number of active versions.</param>
/// <param name="RecycledVersions">Number of versions in the recycle bin.</param>
public sealed record NuPakStatus(bool Enabled, int Feeds, int EffectiveFeeds, int Prefixes, int Packages, int Versions, int RecycledVersions);

/// <summary>One NuGet feed with its counters.</summary>
/// <param name="Id">Feed ID.</param>
/// <param name="Slug">URL segment of the feed.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Enabled">The feed itself is enabled.</param>
/// <param name="AnonymousRead">Packages can be read without a token.</param>
/// <param name="EffectiveEnabled">The feed is actually served (enabled while the server is on).</param>
/// <param name="Prefixes">Number of prefixes.</param>
/// <param name="Grants">Number of robot grants.</param>
/// <param name="Packages">Number of packages.</param>
/// <param name="Versions">Number of active versions.</param>
/// <param name="RecycledVersions">Number of versions in the recycle bin.</param>
public sealed record NuPakFeedInfo(string Id, string Slug, string Name, string? Description, bool Enabled, bool AnonymousRead,
   bool EffectiveEnabled, int Prefixes, int Grants, int Packages, int Versions, int RecycledVersions) {
   /// <summary>Server-relative path of the feed's NuGet V3 service index.</summary>
   public string ServiceIndex => $"/nuget/{Slug}/v3/index.json";

   /// <inheritdoc/>
   public override string ToString() => $"{Name} / {Slug}";
}

/// <summary>Storage used by NuPak packages.</summary>
/// <param name="ActiveBytes">Size of active versions, in bytes.</param>
/// <param name="RecycleBytes">Size of versions in the recycle bin, in bytes.</param>
/// <param name="Packages">Number of packages.</param>
/// <param name="Versions">Number of versions.</param>
public sealed record NuPakStorageInfo(long ActiveBytes, long RecycleBytes, int Packages, int Versions) {
   /// <summary>Sum of <see cref="ActiveBytes"/> and <see cref="RecycleBytes"/>.</summary>
   public long TotalBytes => ActiveBytes + RecycleBytes;
}

/// <summary>One package ID prefix of a feed.</summary>
/// <param name="Id">Prefix ID.</param>
/// <param name="Name">Prefix text that package IDs start with.</param>
/// <param name="Description">Optional description.</param>
/// <param name="Active">Pushes under this prefix are accepted.</param>
/// <param name="Packages">Number of packages.</param>
/// <param name="Bytes">Total size, in bytes.</param>
public sealed record NuPakPrefixInfo(string Id, string Name, string? Description, bool Active, int Packages, long Bytes) {
   /// <inheritdoc/>
   public override string ToString() => $"{Name} · {Packages:N0} packages · {Bytes:N0} B";
}

/// <summary>One package of a feed.</summary>
/// <param name="Id">Package row ID.</param>
/// <param name="Name">Package ID.</param>
/// <param name="Versions">Number of versions.</param>
/// <param name="Bytes">Total size, in bytes.</param>
/// <param name="LatestVersion">Latest version, if any.</param>
public sealed record NuPakPackageInfo(string Id, string Name, int Versions, long Bytes, string? LatestVersion) {
   /// <inheritdoc/>
   public override string ToString() => $"{Name} {LatestVersion} · {Versions} versions";
}

/// <summary>One package version.</summary>
/// <param name="Id">Version row ID.</param>
/// <param name="PackageId">Package row ID.</param>
/// <param name="PackageName">Package ID.</param>
/// <param name="Version">Normalized version.</param>
/// <param name="Original">Version as written in the package.</param>
/// <param name="Prerelease">The version is a prerelease.</param>
/// <param name="State">Version state (active or recycled).</param>
/// <param name="Bytes">Package size, in bytes.</param>
/// <param name="Hash">Package hash.</param>
/// <param name="PushedBy">Robot that pushed it; <c>null</c> when the robot has been deleted.</param>
/// <param name="PushedAt">When it was pushed.</param>
/// <param name="RecycledAt">When it was moved to the recycle bin, if it was.</param>
public sealed record NuPakVersionInfo(string Id, string PackageId, string PackageName, string Version, string Original, bool Prerelease,
   int State, long Bytes, string Hash, string? PushedBy, DateTime PushedAt, DateTime? RecycledAt) {
   /// <inheritdoc/>
   public override string ToString() => $"{PackageName} {Version} · {Bytes:N0} B · {PushedBy ?? "Deleted robot"} · {PushedAt:u}";
}

/// <summary>One robot grant on a prefix.</summary>
/// <param name="RobotId">Robot ID.</param>
/// <param name="RobotName">Robot name.</param>
/// <param name="Access">Access code.</param>
public sealed record NuPakAccessInfo(string RobotId, string RobotName, string Access) {
   /// <inheritdoc/>
   public override string ToString() => $"{RobotName}: {Access}";
}

/// <summary>Result of emptying the recycle bin.</summary>
/// <param name="Count">Number of versions deleted.</param>
/// <param name="Bytes">Size freed, in bytes.</param>
/// <param name="Failed">Number of versions that could not be deleted.</param>
public sealed record NuPakEmptyResult(int Count, long Bytes, int Failed);

/// <summary>Audit filter; <c>null</c> fields do not filter.</summary>
public sealed record NuPakAuditFilter {
   /// <summary>Package ID.</summary>
   public string? Package { get; init; }

   /// <summary>Package version.</summary>
   public string? Version { get; init; }

   /// <summary>Actor name.</summary>
   public string? Actor { get; init; }

   /// <summary>Action name.</summary>
   public string? Action { get; init; }

   /// <summary>Result text.</summary>
   public string? Result { get; init; }

   /// <summary>Earliest time, inclusive.</summary>
   public DateTime? From { get; init; }

   /// <summary>Latest time, inclusive.</summary>
   public DateTime? To { get; init; }
}
