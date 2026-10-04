using Em.Shared;

namespace Em.Api.Shared
{
   /// <summary>
   /// Satu public key pengembang yang terdaftar lewat <see cref="EmAppBuilder.AddDebugToken(string,string,int)"/>,
   /// beserta nama dan masa berlaku token yang ditandatanganinya. Key-nya sudah dibaca dan divalidasi saat
   /// pendaftaran, jadi baris ini tinggal dipakai memverifikasi - tidak ada penguraian ulang per request.
   /// </summary>
   /// <param name="Name">Nama key, yang disebut token dan yang muncul di log.</param>
   /// <param name="Key">Public key-nya; tidak pernah membawa private key.</param>
   /// <param name="Days">Masa berlaku token dalam hari sejak diterbitkan, atau <c>-1</c> kalau tanpa batas.</param>
   internal sealed record DebugTokenKey(string Name, RsaKeyPair Key, int Days)
   {
      /// <summary>
      /// <c>false</c> kalau key ini didaftarkan tanpa batas waktu, sehingga waktu penerbitan token tidak
      /// perlu diperiksa sama sekali.
      /// </summary>
      public bool HasExpiry => Days >= 0;

      /// <summary>
      /// Kapan sebuah token yang diterbitkan pada <paramref name="issuedAtUtc"/> berhenti diterima.
      /// </summary>
      public DateTime ExpiresAtUtc(DateTime issuedAtUtc) => issuedAtUtc.AddDays(Days);
   }
}
