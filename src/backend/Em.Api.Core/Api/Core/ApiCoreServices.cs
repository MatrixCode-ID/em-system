using System.Buffers.Text;
using Em.Api.Core.Models;
using Em.Shared;

namespace Em.Api.Core
{
   [Module("core")]
   public class ApiCoreServices : ServicesBase, IEmApiCoreServices
   {
      private readonly ApiCoreContext _ctx;

      public ApiCoreServices(ApiCoreContext ctx) {
         _ctx = ctx;
      }

      #region API Test Suites
      
      /// <summary>
      /// Mengembalikan public key RSA server apa adanya, untuk keperluan provisioning/pinning oleh caller yang
      /// sudah terautentikasi (mis. menyalin key server ke konfigurasi client).
      /// PERHATIAN: key dari sini belum terbukti benar-benar milik server yang dituju — tidak ada bukti
      /// kepemilikan private key yang menyertainya. Pihak yang perlu memvalidasi key wajib memakai
      /// <see cref="Handshake"/>, bukan action ini.
      /// </summary>
      [GetAction] // IsPublicAction sengaja dibiarkan false: ini bukan endpoint anonim.
      public async Task<string> GetServerPublicKey() {
         var rsaPairs = await GetServerRsaKeyAsync();
         return rsaPairs.PublicKey;
      }

      /// <summary>
      /// Membuktikan ke client bahwa server ini benar-benar memegang private key dari public key yang
      /// dikembalikan, dengan menandatangani nonce acak kiriman client. Client memverifikasi tanda tangan
      /// tersebut memakai public key pada hasil, sehingga public key yang basi atau host yang salah langsung
      /// terdeteksi.
      /// </summary>
      /// <param name="nonce">
      /// Nonce acak dari client, ter-encode Base64Url, panjangnya antara <c>ProbeProtocol.MinNonceLength</c>
      /// dan <c>ProbeProtocol.MaxNonceLength</c> byte.
      /// </param>
      /// <remarks>
      /// Action ini publik karena posisinya mendahului autentikasi: client belum punya public key server yang
      /// terverifikasi, jadi belum bisa mengenkripsi kredensial apa pun. Server hanya menandatangani payload
      /// berprefix domain probe (lihat <c>ProbeProtocol</c>) dan tidak pernah mendekripsi data kiriman client,
      /// supaya endpoint ini tidak berfungsi sebagai decryption oracle.
      /// </remarks>
      [GetAction(IsPublicAction = true)]
      public async Task<ServerHandshakeResult> Handshake(string nonce) {
         var nonceBytes = ProbeProtocol.DecodeNonce(nonce);
         var rsaPairs = await GetServerRsaKeyAsync();
         using var rsa = rsaPairs.CreateRsa();
         return new ServerHandshakeResult {
            PublicKey = rsaPairs.PublicKey,
            Signature = Base64Url.EncodeToString(rsaPairs.SignData(ProbeProtocol.BuildSignaturePayload(nonceBytes))),
            KeySize = rsa.KeySize
         };
      }

      // Ketiganya tertutup: tidak ada satu pun jalur sebelum login yang memanggilnya - stempel waktu
      // dan id baris baru semuanya dibutuhkan di dalam workspace. Kalau nanti ada kebutuhan pra-login
      // yang memerlukannya, buka lagi satu per satu berikut alasannya, bukan sebagai satu blok.
      [GetAction]
      public async Task<DateTimeOffset> GetTimeStamp() => 
         await Task.FromResult(DateTimeOffset.Now);

      [GetAction]
      public async Task<Ulid> GetUlid() => await Task.FromResult(Ulid.NewUlid());

      [GetAction]
      public async Task<Ulid[]> GetUlidMany(int count) {
         var results = new Ulid[count];
         for (var i = 0; i < count; i++) {
            results[i] = Ulid.NewUlid();
         }
         return await Task.FromResult(results);
      }
      
      #endregion
   }
}
