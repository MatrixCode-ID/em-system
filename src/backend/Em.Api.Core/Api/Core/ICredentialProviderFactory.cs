using Microsoft.Extensions.DependencyInjection;
using Em.Api.Core.Models;

namespace Em.Api.Core
{
   /// <summary>
   /// Pendaftaran satu jenis kredensial ke aplikasi. Setiap jenis kredensial menyediakan satu
   /// implementasi dan mendaftarkannya ke DI container, sehingga alur login maupun halaman
   /// pengaturan akun bisa menawarkan semua jenis kredensial yang ada tanpa perlu menyebut satu
   /// per satu nama class-nya - menambah jenis baru cukup dengan menambah satu pendaftaran.
   /// </summary>
   public interface ICredentialProviderFactory
   {
      /// <summary>
      /// Nama jenis kredensial yang dibuat, sama dengan <see cref="CredentialProviderBase.Name"/>
      /// milik hasil <see cref="CreateAsync"/>.
      /// </summary>
      string Name { get; }

      /// <summary>
      /// Membuat kredensial jenis ini untuk <paramref name="user"/>, lengkap dengan data yang sudah
      /// tersimpan untuknya.
      /// </summary>
      /// <param name="user">Pengguna yang kredensialnya ingin dipakai.</param>
      /// <param name="services">Service data kredensial milik request yang sedang berjalan.</param>
      /// <returns>Kredensial yang siap diperiksa atau didaftarkan.</returns>
      Task<CredentialProviderBase> CreateAsync(ta_User user, CredentialServices services);
   }

   /// <summary>
   /// Cara ringkas mengambil kredensial milik seorang pengguna lewat jenis-jenis kredensial yang
   /// terdaftar di aplikasi.
   /// </summary>
   public static class CredentialProviderExtensions
   {
      /// <summary>
      /// Mengambil satu jenis kredensial milik <paramref name="user"/> berdasarkan namanya.
      /// </summary>
      /// <param name="services">Service data kredensial milik request yang sedang berjalan.</param>
      /// <param name="user">Pengguna yang kredensialnya ingin dipakai.</param>
      /// <param name="name">Nama jenis kredensial, mis. <c>"PASSWORD"</c>.</param>
      /// <returns>Kredensial yang diminta.</returns>
      /// <exception cref="InvalidOperationException">Tidak ada jenis kredensial bernama itu yang terdaftar.</exception>
      public static Task<CredentialProviderBase> GetCredentialAsync(this CredentialServices services, ta_User user,
         string name) {
         var factory = services.App.ServiceProvider.GetServices<ICredentialProviderFactory>()
            .FirstOrDefault(f => f.Name == name)
            ?? throw new InvalidOperationException($"No credential provider named '{name}' is registered.");
         return factory.CreateAsync(user, services);
      }

      /// <summary>
      /// Mengambil semua jenis kredensial milik <paramref name="user"/>. Yang dikembalikan adalah
      /// seluruh jenis yang terdaftar di aplikasi, termasuk yang belum didaftarkan penggunanya -
      /// pakai <see cref="CredentialProviderBase.IsEnrolled"/> untuk memilah mana yang sudah siap
      /// dipakai masuk dan mana yang baru bisa ditawarkan untuk didaftarkan.
      /// </summary>
      /// <param name="services">Service data kredensial milik request yang sedang berjalan.</param>
      /// <param name="user">Pengguna yang kredensialnya ingin dipakai.</param>
      /// <returns>Semua kredensial milik pengguna tersebut.</returns>
      public static async Task<CredentialProviderBase[]> GetCredentialsAsync(this CredentialServices services,
         ta_User user) {
         var factories = services.App.ServiceProvider.GetServices<ICredentialProviderFactory>();

         // Created one after another rather than all at once: each one reads and may write through
         // the same service, and there is nothing to gain from overlapping a handful of small reads
         // at the price of a failure that no longer says which credential caused it.
         var result = new List<CredentialProviderBase>();
         foreach (var factory in factories) result.Add(await factory.CreateAsync(user, services));
         return [.. result];
      }
   }
}
