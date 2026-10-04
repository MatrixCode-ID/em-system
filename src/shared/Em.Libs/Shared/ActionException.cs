namespace Em.Shared
{
   /// <summary>
   /// Kegagalan sebuah action yang sudah tahu status HTTP-nya sendiri. Dipakai dua arah: dilempar
   /// service di server supaya jawabannya tidak jatuh ke 500, lalu dilempar ulang di client dengan
   /// status yang sama saat jawaban itu dibaca — jadi pemanggil di UI bisa membedakan "kredensial
   /// salah" dari "server tidak bisa dihubungi" tanpa mencocokkan teks pesan.
   /// <para>
   /// Tanpa ini setiap exception dari action dibungkus jadi 500 oleh dispatcher, sehingga kesalahan
   /// yang normal terjadi — password salah, token kedaluwarsa, permintaan yang tidak diizinkan —
   /// tidak bisa dibedakan dari server yang benar-benar rusak.
   /// </para>
   /// <para>
   /// Turunan <see cref="InvalidOperationException"/> mengikuti pola
   /// <c>SystemAccountException</c>: pemanggil lama yang menangkap exception secara umum tidak
   /// berubah perilakunya.
   /// </para>
   /// </summary>
   public class ActionException : InvalidOperationException
   {
      /// <summary>
      /// Membuat kegagalan action berikut status HTTP yang ingin dikirim ke pemanggil.
      /// </summary>
      /// <param name="message">
      /// Penjelasan yang akan sampai ke pemanggil apa adanya. Jangan menaruh keterangan yang tidak
      /// boleh diketahui pihak luar di sini — isinya ikut terkirim ke client.
      /// </param>
      /// <param name="statusCode">
      /// Status HTTP untuk jawaban ini. Yang lazim dipakai: <c>400</c> permintaannya sendiri keliru,
      /// <c>401</c> pemanggilnya belum terbukti siapa, <c>403</c> pemanggilnya sudah jelas siapa
      /// tetapi tidak berhak, <c>404</c> yang diminta tidak ada.
      /// </param>
      public ActionException(string message, int statusCode) : base(message) {
         StatusCode = statusCode;
      }

      /// <summary>
      /// Status HTTP yang dibawa kegagalan ini.
      /// </summary>
      public int StatusCode { get; }
   }
}
