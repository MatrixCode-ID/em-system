namespace Em.Api.Core.Models
{
   /// <summary>
   /// Dilempar saat ada permintaan membaca atau menulis record milik akun sistem — akun debugger dan
   /// akun administrator. Keduanya cuma berdiri sebagai pengganti user yang login; tidak pernah ada
   /// baris user yang ditulis untuk mereka, jadi permintaannya ditolak di tempat, bukan dijawab kosong
   /// seolah record-nya pernah ada lalu dihapus.
   /// <para>
   /// Turunan <see cref="InvalidOperationException"/> supaya pemanggil lama yang menangkap exception
   /// secara umum tidak berubah perilakunya. Tipe tersendiri diadakan karena ada alur yang perlu
   /// membedakan penolakan ini dari kegagalan lain — layar login, yang harus melaporkannya sebagai
   /// "kredensial salah" alih-alih sebagai gangguan server — dan membedakannya lewat teks pesan jelas
   /// tidak bisa diandalkan.
   /// </para>
   /// </summary>
   public class SystemAccountException : InvalidOperationException
   {
      /// <summary>
      /// Membuat exception penolakan akun sistem.
      /// </summary>
      /// <param name="message">Penjelasan kenapa permintaannya ditolak.</param>
      /// <param name="accountId">Id akun sistem yang diminta.</param>
      public SystemAccountException(string message, string accountId) : base(message) {
         AccountId = accountId;
      }

      /// <summary>
      /// Id akun sistem yang membuat permintaan ini ditolak.
      /// </summary>
      public string AccountId { get; }
   }
}
