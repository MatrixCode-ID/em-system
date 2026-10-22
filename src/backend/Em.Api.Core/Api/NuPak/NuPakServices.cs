using Em;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using Microsoft.Extensions.DependencyInjection;
namespace Em.Api.Core.NuPak;

/// <summary>NuPak management actions: feeds, prefixes, packages, robot access, audit, and storage settings.</summary>
[Module(Defaults.AdministrativeToolsModuleName)]
public sealed partial class NuPakServices(NuPakDbContext db, NuPakStore store, NuPakSettings settings) : ServicesBase, INuPakServices
{
   private Em.Api.Core.Storage.ManagedStorageSettings Storage => HttpContext?.RequestServices?.GetService<Em.Api.Core.Storage.ManagedStorageSettings>()!;
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)] public Task<StorageFeatureStatus> GetMeta_NuPakStorageStatus() => Task.FromResult(Storage.Status(2));
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.SettingsClaim)] public Task<StorageSettingsDetail> GetMeta_NuPakSettings() => Task.FromResult(Storage.Detail(2));
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)] public Task<StorageDirectoryValidation> PostGetMeta_NuPakValidateDirectory(StorageFeatureSettings draft) => Task.FromResult(Storage.Validate(2,draft));
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)] public async Task<StorageSettingsDetail> PostGetMeta_NuPakSettingsSave(StorageSettingsSave request) {
      var saved = Storage.Save(2, request);
      await AuditSystemAsync("System.Settings", "nuget", "storage.changed", "Changed NuGet storage settings", "settings", request.Settings);
      return saved;
   }
   private NuPakActor Actor => new("User", CallerUserId, Request.cUserAccount ?? Request.DebugKeyName ?? "Unknown user", HttpContext?.Connection.RemoteIpAddress?.ToString());
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakStatus> GetMeta_NuPakStatus() {
      var serviceEnabled = await GetService<FeatureServiceRegistry>()!.Get("NuPak").ReadAsync();
      var active = HttpContext?.RequestServices?.GetService<Em.Api.Core.Storage.ManagedStorageSettings>()?.Active(2).Enabled ?? true;
      return new(serviceEnabled && active, await db.Feeds.CountAsync(AbortToken), serviceEnabled && active ? await db.Feeds.CountAsync(f=>f.cNuPakFeedEnabled,AbortToken) : 0, await db.Prefixes.CountAsync(AbortToken), await db.Packages.CountAsync(AbortToken),
         await db.Versions.CountAsync(v => v.cNuPakVersionState == 1, AbortToken), await db.Versions.CountAsync(v => v.cNuPakVersionState == -2, AbortToken));
   }
   /// <inheritdoc />
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public Task<NuPakStatus> PostGetMeta_NuPakSetEnabled(bool enabled) => SetEnabled(enabled);
   private async Task<NuPakStatus> SetEnabled(bool value) {
      Request.RequireAdmin();
      await settings.Gate.WaitAsync();
      try { await GetService<IFeatureServiceServices>()!.PostMeta_NuPakServiceState(value); }
      finally { settings.Gate.Release(); }
      return await GetMeta_NuPakStatus();
   }
   /// <inheritdoc />
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakStorageInfo> GetMeta_NuPakStorageSize() => new(
      await db.Versions.Where(v => v.cNuPakVersionState == 1).SumAsync(v => (long?)v.cNuPakVersionSize, AbortToken) ?? 0,
      await db.Versions.Where(v => v.cNuPakVersionState == -2).SumAsync(v => (long?)v.cNuPakVersionSize, AbortToken) ?? 0,
      await db.Packages.CountAsync(AbortToken), await db.Versions.CountAsync(AbortToken));

}
