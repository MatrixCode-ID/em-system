namespace Em.Shared
{
   /// <summary>
   /// Status sebuah kredensial milik pengguna. Mengikuti aturan nilai yang sama seperti status
   /// lain di aplikasi ini: nilai negatif berarti kredensialnya tidak boleh dipakai untuk masuk,
   /// nilai nol ke atas berarti kredensialnya sudah terdaftar dan sah.
   /// </summary>
   public enum CredentialState
   {
      /// <summary>Sudah dihapus dan tidak bisa dipulihkan lagi.</summary>
      Deleted = -3,

      /// <summary>Dicabut, mis. karena perangkatnya hilang atau rahasianya bocor.</summary>
      Revoked = -2,

      /// <summary>
      /// Sudah disiapkan tetapi belum selesai didaftarkan - misalnya barisnya sudah dibuat tetapi
      /// penggunanya belum pernah mengisi password, atau kode pertama dari aplikasi authenticator
      /// belum dikonfirmasi. Belum bisa dipakai untuk masuk.
      /// </summary>
      Pending = -1,

      /// <summary>Terdaftar, tetapi sementara dinonaktifkan oleh pengguna atau administrator.</summary>
      Inactive = 0,

      /// <summary>Terdaftar dan siap dipakai untuk masuk.</summary>
      Active = 1
   }
}
