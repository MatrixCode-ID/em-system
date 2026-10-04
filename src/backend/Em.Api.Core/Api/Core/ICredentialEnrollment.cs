namespace Em.Api.Core
{
   /// <summary>
   /// Kontrak pendaftaran kredensial satu langkah: penggunanya cukup mengirim satu bukti dan
   /// kredensialnya langsung siap dipakai - seperti password, yang cukup diketik sekali.
   /// Dipisah dari <see cref="CredentialProviderBase"/> karena tidak semua jenis kredensial
   /// didaftarkan dari dalam aplikasi ini.
   /// </summary>
   /// <typeparam name="TProof">Jenis bukti yang diminta saat pendaftaran.</typeparam>
   public interface ICredentialEnrollment<in TProof>
   {
      /// <summary>
      /// Menyelesaikan pendaftaran dengan bukti dari pengguna, lalu menandai kredensialnya siap
      /// dipakai untuk masuk.
      /// </summary>
      /// <param name="proof">Bukti dari pengguna, mis. password baru atau kode pertama dari aplikasi authenticator.</param>
      Task ConfirmEnrollAsync(TProof proof);
   }

   /// <summary>
   /// Kontrak pendaftaran kredensial dua langkah: aplikasi menyiapkan dulu apa yang harus dibawa
   /// pengguna ke tempat lain - misalnya rahasia yang perlu dipindai aplikasi authenticator lewat
   /// QR code - baru kemudian pendaftarannya dikonfirmasi dengan bukti bahwa langkah pertama
   /// berhasil.
   /// </summary>
   /// <typeparam name="TSetup">Jenis data yang dihasilkan langkah penyiapan.</typeparam>
   /// <typeparam name="TProof">Jenis bukti yang diminta untuk mengonfirmasi penyiapan tadi.</typeparam>
   public interface ICredentialEnrollment<TSetup, in TProof> : ICredentialEnrollment<TProof>
   {
      /// <summary>
      /// Memulai pendaftaran dan mengembalikan data yang perlu ditampilkan ke pengguna. Selama
      /// <see cref="ICredentialEnrollment{TProof}.ConfirmEnrollAsync"/> belum dipanggil,
      /// kredensialnya belum bisa dipakai untuk masuk.
      /// </summary>
      /// <returns>Data penyiapan yang harus diteruskan ke pengguna.</returns>
      Task<TSetup> BeginEnrollAsync();
   }
}
