using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Api.Core.Registry;
using Em.Api.Core.Registry.Deploy;

namespace Em.Api.Shared
{
   public partial class EmAppBuilder
   {
      // null = registry off. Kept as written in Program.cs; the absolute path is computed in BuildApp,
      // where ContentRootPath is known.
      internal string? ContainerRegistryPath { get; private set; }

      /// <summary>
      /// Enables the container registry: the <c>/v2</c> path (OCI Distribution) serves <c>docker login</c>,
      /// <c>push</c> and <c>pull</c> for robots that were granted access. Metadata (roots, container names, tags,
      /// manifests, robots, grants, deploy targets) lives in the database in the <c>ta_Ctn*</c> tables (script
      /// <c>doc/sqlscript/mssql/tables/030-registry.sql</c>); layer content lives on disk. Roots, folders, container
      /// names and deploy targets are managed through <c>ICtnServices</c> with the <c>Container Manager Access</c>
      /// claim. Robots use <c>IRobotServices</c> with the <c>User Manager Access</c> claim.
      /// Without this call every address below <c>/v2</c> answers 404.
      /// </summary>
      /// <param name="localStorePath">
      /// Folder for blobs and temporary uploads. An absolute path is used as is; a relative path is resolved
      /// against the application's content folder. Created at startup when missing.
      /// </param>
      /// <exception cref="ArgumentException"><paramref name="localStorePath"/> is empty.</exception>
      /// <exception cref="InvalidOperationException">The registry was already enabled.</exception>
      public void AddContainerRegistry(string localStorePath) {
         if (StorageSettingsHostId is not null) throw new InvalidOperationException("Static registry cannot be combined with managed storage settings.");
         if (string.IsNullOrWhiteSpace(localStorePath)) {
            throw new ArgumentException("The container registry store path must not be empty.", nameof(localStorePath));
         }

         if (ContainerRegistryPath is not null) {
            throw new InvalidOperationException(
               $"The container registry is already enabled for '{ContainerRegistryPath}'; '{nameof(AddContainerRegistry)}' may only be called once.");
         }

         ContainerRegistryPath = localStorePath;
         AddDbContext<CtnContext>();
         Services.AddScoped<IRobotAccessManager, CtnRobotAccessManager>();
         AddContainerDeploy();
         Logger.LogInformation("Container registry enabled: {Path}", localStorePath);
      }

      // Deploy to Docker servers rides on the registry: same tables, same claim, same switch.
      private void AddContainerDeploy() {
         Services.AddSingleton<ICtnDeployTransports, CtnDeployTransports>();
         Services.AddSingleton<CtnDeployRunner>();
         Services.AddScoped(sp => new CtnDeploySecrets(sp.GetRequiredService<ApiCoreContext>()));
      }
   }
}
