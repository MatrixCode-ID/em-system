using Em.Api.Core.Models;
using Em.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Core;

public partial class EmApp {
   private readonly FeatureServiceRegistry _featureServices = new();
   private readonly HashSet<string> _disabledFeatureClaims = new(StringComparer.OrdinalIgnoreCase);
   private bool IsDisabledFeatureAction(string? claim) => claim is not null && _disabledFeatureClaims.Contains(claim);

   private void ApplyFeatureServiceRegistration(EmAppBuilder builder) {
      void Register(string feature, string label, bool enabled, string? folder, params string[] claims) {
         if (!enabled) { foreach (var claim in claims) _disabledFeatureClaims.Add(claim); return; }
         if (folder is not null)
            _featureServices.Add(feature, FeatureServiceSwitch.FileBacked(label, Path.Combine(folder, "settings", "service.json")));
      }
      Register("Cdn", "CDN", _cdn.IsEnabled, _cdn.StoragePath,
         ICdnServices.CdnClaim, ICdnServices.SettingsClaim, ICdnReleaseServices.ReleaseClaim);
      Register("Ctn", "Container Registry", _ctnStore.IsEnabled, _ctnStore.RootPath,
         ICtnServices.CtnClaim, ICtnServices.SettingsClaim);
      Register("NuPak", "NuGet feed", builder.NuPakRegistered && _storageSettings.Active(2).Enabled,
         _storageSettings.Resolve(_storageSettings.Active(2).Directory), INuPakServices.ManagerClaim, INuPakServices.SettingsClaim);
      Register("ObjectStorage", "Object Storage", builder.ObjectStorageAdded, null,
         IObjectStorageServices.ManagerClaim, IObjectStorageServices.SettingsClaim);
      if (builder.ObjectStorageAdded) {
         _featureServices.Add("ObjectStorage", new FeatureServiceSwitch("Object Storage", async () => {
            using var scope = _rootServiceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
            var value = await db.Meta.Where(m => m.cMetaKey == "ObjectStore.ServiceEnabled").Select(m => m.cMetaValue).SingleOrDefaultAsync();
            return value is null || bool.Parse(value);
         }, async enabled => {
            using var scope = _rootServiceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
            var row = await db.Meta.AsTracking().SingleOrDefaultAsync(m => m.cMetaKey == "ObjectStore.ServiceEnabled");
            if (row is null) { row = new() { cMetaKey = "ObjectStore.ServiceEnabled", cMetaDescription = "Object storage service switch" }; db.Meta.Add(row); }
            row.cMetaValue = enabled.ToString(); row.ustamp = DateTime.UtcNow;
            await db.SaveChangesAsync();
         }));
      }
      Services.AddSingleton(_featureServices);
   }
}
