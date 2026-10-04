using System.Buffers.Text;

namespace Em.Shared
{
   /// <summary>
   /// Aturan penyusunan payload untuk probe koneksi (action <c>Core/Handshake</c>): client mengirim nonce acak,
   /// server menandatanganinya, lalu client memverifikasi tanda tangan itu dengan public key yang dikembalikan
   /// server. Kelas ini dipakai kedua sisi supaya byte yang ditandatangani dan yang diverifikasi persis sama.
   /// </summary>
   /// <remarks>
   /// Payload yang ditandatangani sengaja diberi prefix domain (<c>em.probe.v1:</c>), bukan nonce mentah.
   /// Tujuannya membatasi arti tanda tangan tersebut: server hanya pernah menandatangani byte yang berawalan
   /// prefix ini, sehingga tanda tangan dari probe tidak bisa dipakai ulang sebagai tanda tangan atas pesan
   /// protokol lain. Konsekuensinya, setiap payload lain yang nanti ditandatangani server wajib memakai prefix
   /// domain yang berbeda.
   /// </remarks>
   public static class ProbeProtocol
   {
      /// <summary>
      /// Panjang minimum nonce dalam byte. Nonce yang terlalu pendek membuat tanda tangan bisa ditebak/di-replay.
      /// </summary>
      public const int MinNonceLength = 16;

      /// <summary>
      /// Panjang maksimum nonce dalam byte. Dibatasi supaya server tidak bisa dipaksa menandatangani blob besar
      /// dari pemanggil yang belum terautentikasi.
      /// </summary>
      public const int MaxNonceLength = 64;

      /// <summary>
      /// Panjang nonce yang dipakai client saat memulai probe.
      /// </summary>
      public const int DefaultNonceLength = 32;

      private static readonly byte[] Domain = "em.probe.v1:"u8.ToArray();

      /// <summary>
      /// Menyusun byte yang benar-benar ditandatangani server: prefix domain diikuti nonce.
      /// </summary>
      /// <param name="nonce">Nonce acak dari client.</param>
      public static byte[] BuildSignaturePayload(ReadOnlySpan<byte> nonce) => [..Domain, ..nonce];

      /// <summary>
      /// Meng-encode nonce menjadi string Base64Url supaya aman dipakai sebagai nilai query string.
      /// </summary>
      public static string EncodeNonce(ReadOnlySpan<byte> nonce) => Base64Url.EncodeToString(nonce);

      /// <summary>
      /// Men-decode nonce dari string Base64Url sekaligus memvalidasi panjangnya terhadap
      /// <see cref="MinNonceLength"/> dan <see cref="MaxNonceLength"/>.
      /// </summary>
      /// <param name="value">Nonce ter-encode Base64Url.</param>
      /// <exception cref="ArgumentException">Dilempar kalau <paramref name="value"/> bukan Base64Url yang valid.</exception>
      /// <exception cref="ArgumentOutOfRangeException">Dilempar kalau panjang nonce di luar batas yang diizinkan.</exception>
      public static byte[] DecodeNonce(string value) {
         byte[] nonce;
         try {
            nonce = Base64Url.DecodeFromChars(value);
         }
         catch (FormatException x) {
            throw new ArgumentException("Nonce is not valid Base64Url text.", nameof(value), x);
         }

         if (nonce.Length is < MinNonceLength or > MaxNonceLength) {
            throw new ArgumentOutOfRangeException(nameof(value), nonce.Length,
               $"Nonce must be between {MinNonceLength} and {MaxNonceLength} bytes.");
         }

         return nonce;
      }
   }
}
