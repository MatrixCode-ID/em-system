using System.Text.Json.Serialization;

namespace Em.Shared
{
   /// <summary>
   /// Deklarasi murni satu claim: module pemiliknya dan namanya. Tidak menyimpan jawaban ("apakah
   /// pengguna ini punya hak-nya") - itu urusan pemberian di database, bukan deklarasi ini.
   /// </summary>
   public record ClaimAction
   {
      /// <summary>
      /// Pemisah antara nama module dan nama claim di dalam <see cref="Key"/>. Dipilih karena tidak
      /// sah di dalam identifier C#, sehingga tidak akan pernah muncul tanpa sengaja di salah satu
      /// bagian - berbeda dari titik (sudah dipakai nama module seperti <c>core.contact</c>) maupun
      /// tanda hubung (wajar muncul di nama seperti <c>Read-Only</c>).
      /// </summary>
      public const char Separator = ':';

      // Lebar kolom cUserClaimName di ta_UserClaim.
      private const int MaxKeyLength = 255;

      public static ClaimAction Create<T>(string name) where T : IServices {
         var moduleName = ModuleAttribute.ResolveName(typeof(T));

         if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("Claim name must not be empty.", nameof(name));
         }

         if (name.Contains(Separator)) {
            throw new ArgumentException(
               $"Claim name '{name}' must not contain '{Separator}'.", nameof(name));
         }

         if (moduleName.Contains(Separator)) {
            throw new ArgumentException(
               $"Module name '{moduleName}' must not contain '{Separator}'.", nameof(name));
         }

         var result = new ClaimAction {
            Name = name,
            ModuleName = moduleName,
         };

         if (result.Key.Length > MaxKeyLength) {
            throw new ArgumentException(
               $"Claim key '{result.Key}' is {result.Key.Length} characters, which exceeds the maximum of {MaxKeyLength}.",
               nameof(name));
         }

         return result;
      }

      /// <summary>
      /// Pasangan dari <see cref="Key"/>: memecah satu string kunci kembali menjadi <see cref="ModuleName"/>
      /// dan <see cref="Name"/>. Dipakai server saat membaca baris <c>ta_UserClaim</c> - client tidak
      /// pernah menyusun maupun memecah kunci sendiri.
      /// </summary>
      /// <param name="key">
      /// Kunci berbentuk <c>module{Separator}name</c>. Kunci tanpa <see cref="Separator"/> sama sekali
      /// berarti baris lama atau rusak, dan dikembalikan apa adanya dengan module kosong alih-alih
      /// melempar - satu baris aneh tidak boleh mematikan seluruh layar yang membacanya.
      /// </param>
      public static ClaimAction FromKey(string key) {
         var separatorIndex = key.IndexOf(Separator);
         if (separatorIndex < 0) {
            return new ClaimAction { ModuleName = string.Empty, Name = key };
         }

         return new ClaimAction {
            ModuleName = key[..separatorIndex],
            Name = key[(separatorIndex + 1)..]
         };
      }

      public required string ModuleName { get; init; }
      public required string Name { get; init; }

      /// <summary>
      /// Kunci gabungan tersimpan di database, <c>module:claimname</c>. Diabaikan saat serialisasi:
      /// ia turunan dari dua properti lain, jadi mengirimnya lewat kabel berarti mengirim data yang
      /// sama dua kali sekaligus membuka celah kunci yang tidak konsisten dengan isinya.
      /// </summary>
      [JsonIgnore]
      public string Key => $"{ModuleName}{Separator}{Name}";
   }
}