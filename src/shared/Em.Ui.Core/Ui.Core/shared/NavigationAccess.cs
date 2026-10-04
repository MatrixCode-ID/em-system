using Em.Api.Core.Models;

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
