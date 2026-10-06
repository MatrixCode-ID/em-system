using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Aturan tunggal "boleh dibuka atau tidak" untuk sebuah navigasi, dipakai baik oleh menu home
   /// maupun oleh <c>NavigateTo</c> - supaya yang disembunyikan di menu dan yang ditolak saat dibuka
   /// langsung tidak pernah berbeda. Mode debug tidak diperiksa di sini: itu milik <c>EmApp</c>
   /// masing-masing sisi, karena <c>IsDebugMode</c> ada di sana.
   /// </summary>
   public static class NavigationAccess
   {
      /// <summary>
      /// Menjawab apakah module milik <paramref name="navigation"/> dideklarasikan di
      /// <paramref name="catalog"/> (katalog claim server aktif digabung claim bawaan client). Navigasi
      /// tanpa ikatan module selalu dianggap ada; navigasi dengan claim wajib butuh kunci claim itu;
      /// selebihnya cukup satu claim di module yang sama. Berbeda dari <see cref="CanOpen"/>, aturan ini
      /// berlaku untuk administrator dan mode debug juga: modul yang dimatikan server (mis. module uji
      /// lewat <c>modules</c> di konfigurasi API) tidak punya layar yang bisa dipakai, jadi menunya disembunyikan.
      /// </summary>
      /// <param name="navigation">Navigasi yang hendak dibuka.</param>
      /// <param name="catalog">Katalog claim yang dikenal aplikasi.</param>
      public static bool IsDeclared(INavigation navigation, IReadOnlyList<ClaimAction> catalog) {
         if (navigation.ModuleName is null) return true;

         if (navigation.RequiredClaim is { } claim) {
            return catalog.Any(r => string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));
         }

         return catalog.Any(r => string.Equals(r.ModuleName, navigation.ModuleName, StringComparison.OrdinalIgnoreCase));
      }

      /// <summary>
      /// Menjawab apakah <paramref name="user"/> boleh membuka <paramref name="navigation"/>, di luar
      /// mode debug. Urutan pemeriksaannya mengikat: navigasi tanpa ikatan module selalu boleh, tidak
      /// ada user aktif berarti tidak boleh, user administrator selalu boleh, lalu baru diperiksa
      /// claim yang dipersyaratkan (kalau ada) atau cukup claim apa pun di module yang sama.
      /// <para>
      /// Sengaja tidak memakai indexer <see cref="ClaimCollection"/>: indexer itu melempar exception
      /// di DEBUG untuk claim yang belum ada di katalog server, sedangkan di sini kunci yang tidak ada
      /// cukup berarti "tidak punya" - ketiadaan module di server bukan kesalahan pemanggil.
      /// </para>
      /// </summary>
      /// <param name="navigation">Navigasi yang hendak dibuka.</param>
      /// <param name="user">Pengguna yang sedang aktif, atau <c>null</c> kalau belum ada yang masuk.</param>
      public static bool CanOpen(INavigation navigation, User? user) {
         if (navigation.ModuleName is null) return true;

         if (user is null) return false;

         if (user.cUserIsAdmin) return true;

         if (navigation.RequiredClaim is { } claim) {
            return user.AvailableClaims.Any(r =>
               string.Equals(r.Key, claim.Key, StringComparison.OrdinalIgnoreCase));
         }

         return user.AvailableClaims.Any(r =>
            string.Equals(r.ModuleName, navigation.ModuleName, StringComparison.OrdinalIgnoreCase));
      }
   }
}
