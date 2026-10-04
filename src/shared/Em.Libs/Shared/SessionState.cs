namespace Em.Shared
{
   /// <summary>
   /// Status sebuah sesi masuk. Mengikuti aturan nilai yang sama seperti status lain di aplikasi
   /// ini: nilai negatif berarti sesinya tidak bisa dipakai lagi, nilai nol ke atas berarti sesinya
   /// masih hidup.
   /// </summary>
   public enum SessionState
   {
      /// <summary>Sudah dihapus dan tidak bisa dipulihkan lagi.</summary>
      Deleted = -3,

      /// <summary>
      /// Dicabut sebelum waktunya habis - penggunanya keluar, administrator mengakhirinya, atau
      /// refresh token-nya sudah ditukar dengan yang baru.
      /// </summary>
      Revoked = -2,

      /// <summary>Umurnya sudah lewat. Tidak bisa lagi menerbitkan token baru.</summary>
      Expired = -1,

      /// <summary>Masih berjalan dan refresh token-nya masih bisa ditukar.</summary>
      Active = 1
   }
}
