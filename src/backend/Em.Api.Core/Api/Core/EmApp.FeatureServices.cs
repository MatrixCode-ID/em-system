using Em.Api.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
         null, INuPakServices.ManagerClaim, INuPakServices.SettingsClaim);
      Register("ObjectStorage", "Object Storage", builder.ObjectStorageAdded, null,
         IObjectStorageServices.ManagerClaim, IObjectStorageServices.SettingsClaim);
      if (builder.NuPakRegistered && _storageSettings.Active(2).Enabled)
         AddMetaSwitch("NuPak", "NuGet feed", "NuPakEnable");
      if (builder.ObjectStorageAdded)
         AddMetaSwitch("ObjectStorage", "Object Storage", "ObjectStore.ServiceEnabled");
      Services.AddSingleton(_featureServices);
   }
   private void MapFeatureServiceGate(WebApplication app) {
      app.Use(async (http, next) => {
         var feature = http.Request.Path.StartsWithSegments("/cdn") ? "Cdn"
            : http.Request.Path.StartsWithSegments("/nuget") ? "NuPak"
            : http.Request.Path.StartsWithSegments("/store") ? "ObjectStorage"
            : http.Request.Path.StartsWithSegments("/v2") ? "Ctn" : null;
         if (feature is not null && !await _featureServices.AllowPublicAsync(feature, http)) {
            if (feature == "Cdn") await _skinPages.CdnNotFoundAsync(http, assetsAvailable: false);
            return;
         }
         await next(http);
      });
   }

   private void AddMetaSwitch(string feature, string label, string key) {
      _featureServices.Add(feature, new FeatureServiceSwitch(label, async () => {
         using var scope = _rootServiceProvider.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
         var value = await db.ta_Metas.Where(m => m.cMetaKey == key).Select(m => m.cMetaValue).SingleOrDefaultAsync();
         return FeatureServiceSwitch.MetaEnabled(value);
      }, async enabled => {
         using var scope = _rootServiceProvider.CreateScope();
         var db = scope.ServiceProvider.GetRequiredService<ApiCoreContext>();
         var row = await db.ta_Metas.AsTracking().SingleOrDefaultAsync(m => m.cMetaKey == key);
         if (row is null) { row = new() { cMetaKey = key, cMetaDescription = label + " service switch" }; db.ta_Metas.Add(row); }
         row.cMetaValue = enabled.ToString(); row.ustamp = DateTime.UtcNow;
         await db.SaveChangesAsync();
      }));
   }

}
