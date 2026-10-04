using Em;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;
using Microsoft.Extensions.DependencyInjection;
namespace Em.Api.Core.NuPak;

[Module(Defaults.AdministrativeToolsModuleName)]
public sealed partial class NuPakServices(NuPakDbContext db, NuPakStore store, NuPakSettings settings) : ServicesBase, INuPakServices
{
   private Em.Api.Core.Storage.ManagedStorageSettings Storage => HttpContext?.RequestServices?.GetService<Em.Api.Core.Storage.ManagedStorageSettings>()!;
   [GetAction(claim: INuPakServices.ManagerClaim)] public Task<StorageFeatureStatus> GetMeta_NuPakStorageStatus() => Task.FromResult(Storage.Status(2));
   [GetAction(claim: INuPakServices.SettingsClaim)] public Task<StorageSettingsDetail> GetMeta_NuPakSettings() => Task.FromResult(Storage.Detail(2));
   [PostAction(claim: INuPakServices.SettingsClaim)] public Task<StorageDirectoryValidation> PostGetMeta_NuPakValidateDirectory(StorageFeatureSettings draft) => Task.FromResult(Storage.Validate(2,draft));
   [PostAction(claim: INuPakServices.SettingsClaim)] public Task<StorageSettingsDetail> PostGetMeta_NuPakSettingsSave(StorageSettingsSave request) => Task.FromResult(Storage.Save(2,request));
   private NuPakActor Actor => new("User", CallerUserId, Request.cUserAccount ?? Request.DebugKeyName ?? "Unknown user", HttpContext?.Connection.RemoteIpAddress?.ToString());
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakStatus> GetMeta_NuPakStatus() {
      var flags = await settings.ReadAsync(db, AbortToken);
      var active = HttpContext?.RequestServices?.GetService<Em.Api.Core.Storage.ManagedStorageSettings>()?.Active(2).Enabled ?? true;
      return new(flags.Enabled && active, await db.Feeds.CountAsync(AbortToken), flags.Enabled && active ? await db.Feeds.CountAsync(f=>f.cNuPakFeedEnabled,AbortToken) : 0, await db.Prefixes.CountAsync(AbortToken), await db.Packages.CountAsync(AbortToken),
         await db.Versions.CountAsync(v => v.cNuPakVersionState == 1, AbortToken), await db.Versions.CountAsync(v => v.cNuPakVersionState == -2, AbortToken));
   }
   [PostAction(claim: INuPakServices.SettingsClaim)]
   public Task<NuPakStatus> PostGetMeta_NuPakSetEnabled(bool enabled) => SetFlag("NuPakEnable", enabled, enabled ? "Enable" : "Disable");
   private async Task<NuPakStatus> SetFlag(string key, bool value, string action) {
      await store.Gate.WaitAsync(AbortToken);
      try {
         await settings.Gate.WaitAsync(AbortToken);
         try {
         await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
         var row = await db.Meta.AsTracking().SingleOrDefaultAsync(r => r.cMetaKey == key);
         if (row is null) { row = new() { cMetaKey = key, cMetaDescription = "NuGet runtime setting" }; db.Meta.Add(row); }
         row.cMetaValue = value.ToString(); row.ustamp = DateTime.UtcNow;
         NuPakOperations.Audit(db, Actor, action, detail: value.ToString());
         await db.SaveChangesAsync(); await tx.CommitAsync();
         } finally { settings.Gate.Release(); }
      } finally { store.Gate.Release(); }
      return await GetMeta_NuPakStatus();
   }
   [GetAction(claim: INuPakServices.ManagerClaim)]
   public async Task<NuPakStorageInfo> GetMeta_NuPakStorageSize() => new(
      await db.Versions.Where(v => v.cNuPakVersionState == 1).SumAsync(v => (long?)v.cNuPakVersionSize, AbortToken) ?? 0,
      await db.Versions.Where(v => v.cNuPakVersionState == -2).SumAsync(v => (long?)v.cNuPakVersionSize, AbortToken) ?? 0,
      await db.Packages.CountAsync(AbortToken), await db.Versions.CountAsync(AbortToken));

}
