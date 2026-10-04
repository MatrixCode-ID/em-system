using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core;
using Em.Api.Core.Registry;

namespace Em.Api.Shared
{
   public partial class EmAppBuilder
   {
      // null = registry mati. Disimpan seperti yang ditulis di Program.cs; path absolutnya baru
      // dihitung di BuildApp karena ContentRootPath baru diketahui di sana.
      internal string? ContainerRegistryPath { get; private set; }

      /// <summary>
      /// Menyalakan container registry: jalur <c>/v2</c> (OCI Distribution) melayani <c>docker login</c>,
      /// <c>push</c>, dan <c>pull</c> bagi robot yang diberi hak. Metadata (root, nama container, tag,
      /// manifest, robot, hak) ada di database lewat tabel <c>ta_Ctn*</c> (skrip
      /// <c>doc/sqlscript/mssql/tables/030-registry.sql</c>); isi layer ada di disk. Root, folder dan nama container
      /// dikelola lewat <c>ICtnServices</c> dengan claim <c>Container Manager Access</c>. Robot memakai
      /// <c>IRobotServices</c> dengan claim <c>User Manager Access</c>.
      /// Tanpa panggilan ini, setiap alamat di bawah <c>/v2</c> dijawab 404.
      /// </summary>
      /// <param name="localStorePath">
      /// Folder penyimpan blob dan unggahan sementara. Path absolut dipakai apa adanya; path relatif
      /// dihitung dari folder konten aplikasi. Dibuat sendiri saat startup kalau belum ada.
      /// </param>
      /// <exception cref="ArgumentException">Dilempar kalau <paramref name="localStorePath"/> kosong.</exception>
      /// <exception cref="InvalidOperationException">Dilempar kalau registry sudah dinyalakan sebelumnya.</exception>
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
         Logger.LogInformation("Container registry enabled: {Path}", localStorePath);
      }
   }
}
