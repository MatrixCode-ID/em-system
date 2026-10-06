using Em.Api.Core.Models;
using Em.Api.Core;
using Em.Api.Core.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Em.Api.Shared;

public partial class EmAppBuilder
{
   public void AddManagedStorageSettings() => AddManagedStorageSettings("default",
      new StorageFeatureSettings { Enabled = true, Directory = "./data/cdn", MaxUploadMb = 200 },
      new StorageFeatureSettings { Enabled = true, Directory = "./data/container-registry" });

   internal StorageFeatureSettings NuGetDefaults { get; private set; } = new() { Enabled = true, Directory = "./data/nuget", MaxUploadMb = 250 };
   internal string? StorageSettingsHostId { get; private set; }
   internal StorageFeatureSettings? CdnDefaults { get; private set; }
   internal StorageFeatureSettings? RegistryDefaults { get; private set; }

   /// <summary>Manage both stores in ta_Meta with a per-host key. Changes apply on the next API startup.
   /// Requires the registry schema even when disabled. Do not combine with EnableCdn/AddContainerRegistry.</summary>
   public void AddManagedStorageSettings(string hostId, StorageFeatureSettings cdn, StorageFeatureSettings registry) {
      AddManagedStorageSettings(hostId, cdn, registry, new() { Enabled = true, Directory = "./data/nuget", MaxUploadMb = 250 });
   }
   public void AddManagedStorageSettings(string hostId, StorageFeatureSettings cdn, StorageFeatureSettings registry, StorageFeatureSettings nuget) {
      ArgumentNullException.ThrowIfNull(nuget); NuGetDefaults = nuget;
      if (string.IsNullOrWhiteSpace(hostId)) throw new ArgumentException("Storage settings host ID is required.");
      ArgumentNullException.ThrowIfNull(cdn);
      ArgumentNullException.ThrowIfNull(registry);
      if (StorageSettingsHostId is not null || CdnRootPath is not null || ContainerRegistryPath is not null)
         throw new InvalidOperationException("Managed storage settings cannot be combined with static CDN/registry configuration or registered twice.");
      StorageSettingsHostId = hostId;
      CdnDefaults = cdn;
      RegistryDefaults = registry;
      AddDbContext<CtnContext>();
      Services.AddScoped<IRobotAccessManager, CtnRobotAccessManager>();
      AddContainerDeploy();
   }
}
