using System.Reflection;

namespace Em.Shared
{
   /// <summary>
   /// Menandai sebuah class service dengan nama module, dipakai untuk pengelompokan/identifikasi
   /// module (mis. untuk dokumentasi atau diagnostik) di sisi dispatcher.
   /// </summary>
   /// <param name="name">Nama module. Jika <c>null</c>, tidak ada nama module eksplisit yang di-set.</param>
   [AttributeUsage(AttributeTargets.Class)]
   public class ModuleAttribute(string? name = null) : Attribute
   {
      /// <summary>
      /// Nama module yang di-set lewat atribut ini, atau <c>null</c> jika tidak diisi.
      /// </summary>
      public string? Name { get; } = name;

      /// <summary>
      /// Menurunkan nama module dari <see cref="ModuleAttribute"/> pada <paramref name="serviceType"/>:
      /// nama eksplisit atribut kalau ada, atau nama tipe kalau atributnya kosong. Satu-satunya tempat
      /// aturan ini ditulis, supaya server dan client tidak pernah menurunkan nama module yang berbeda
      /// untuk tipe yang sama.
      /// </summary>
      /// <param name="serviceType">Tipe implementasi service yang ditandai <see cref="ModuleAttribute"/>.</param>
      /// <param name="required">
      /// <c>true</c> kalau tipe wajib membawa <see cref="ModuleAttribute"/> - dilempar kalau tidak ada.
      /// <c>false</c> membuat tipe tanpa atribut jatuh ke nama tipenya sendiri, dipakai untuk service UI
      /// yang sudah lama berjalan tanpa atribut ini.
      /// </param>
      /// <exception cref="InvalidOperationException">
      /// Dilempar kalau <paramref name="required"/> <c>true</c> dan <paramref name="serviceType"/> tidak
      /// membawa <see cref="ModuleAttribute"/>.
      /// </exception>
      public static string ResolveName(Type serviceType, bool required = true) {
         var attribute = serviceType.GetCustomAttribute<ModuleAttribute>();
         if (attribute is null) {
            if (required) {
               throw new InvalidOperationException(
                  $"Type '{serviceType.FullName}' must be decorated with [Module] attribute to take part in claims or action routing.");
            }

            return serviceType.Name;
         }

         return string.IsNullOrWhiteSpace(attribute.Name) ? serviceType.Name : attribute.Name;
      }
   }
}