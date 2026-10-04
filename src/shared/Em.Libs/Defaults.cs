using System.Text.Json;

namespace Em
{
   public static class Defaults
   {
      /// <summary>
      /// <see cref="JsonSerializerOptions"/> standar untuk komunikasi API antara backend dan UI - dipakai baik
      /// saat backend menyusun response (<c>Results.Json</c>) maupun saat UI membaca response tersebut, supaya
      /// penamaan property JSON konsisten di kedua sisi. Sengaja case-sensitive (tanpa naming policy) dan
      /// mengikuti persis nama property C# (mis. <c>ValidResult</c>, bukan <c>validResult</c>), karena
      /// <c>Em.Api.Core</c>/<c>Em.Libs</c> direncanakan jadi paket NuGet publik yang API-nya bisa dipakai
      /// sistem lain di luar solution ini - kontrak JSON yang predictable lebih penting daripada konvensi
      /// camelCase.
      /// </summary>
      public static JsonSerializerOptions ResponseJsonOptions { get; } = new() {
         PropertyNamingPolicy = null,
         PropertyNameCaseInsensitive = false
      };

      public const int StandardTimeoutSeconds = 30;

      /// <summary>
      /// Handler HTTP bawaan untuk memanggil API: apa adanya, dengan validasi sertifikat TLS tetap
      /// menyala. Pakai <see cref="CreateHttpClientHandler(bool)"/> kalau validasinya memang harus
      /// dimatikan - menyebutkan niat itu adalah satu-satunya cara mendapatkannya.
      /// </summary>
      public static HttpClientHandler DefaultHttpClientHandler => new();

      /// <summary>
      /// Membuat handler HTTP yang validasi sertifikat TLS-nya bisa dimatikan. Dipisahkan dari
      /// <see cref="DefaultHttpClientHandler"/> supaya mematikannya selalu jadi pilihan yang ditulis
      /// pemanggilnya, bukan keadaan bawaan yang tidak pernah dipilih siapa-siapa.
      /// </summary>
      /// <param name="ignoreSslErrors">
      /// <c>true</c> berarti sertifikat apa pun diterima. Ini mematikan TLS sebagai penjamin keaslian
      /// host, sehingga handshake RSA di atasnya pun tidak lagi bisa mendeteksi pihak di tengah -
      /// pantas dipakai hanya untuk server development bersertifikat self-signed.
      /// </param>
      public static HttpClientHandler CreateHttpClientHandler(bool ignoreSslErrors) {
         var handler = new HttpClientHandler();
         if (ignoreSslErrors) {
            handler.ServerCertificateCustomValidationCallback =
               HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
         }

         return handler;
      }

      /// <summary>
      /// Nama module tempat seluruh action kredensial tinggal - sign in, refresh token, sign out, dan
      /// urusan password. Ditulis di sini supaya atribut <c>[Module]</c> di kedua sisi dan jalur
      /// refresh di dalam client menyebut kata yang sama persis; kalau ketiganya boleh mengetik
      /// sendiri, satu salah ketik baru ketahuan saat token tidak pernah bisa diperbarui.
      /// </summary>
      public const string CredentialModuleName = "core.credential";

      /// <summary>
      /// Nama module alat-alat administrasi bawaan engine, mis. pengelola CDN. Claim alat-alat ini
      /// ditulis dengan awalan nama ini, dan pencocokan claim di server membandingkannya dengan
      /// module tempat action-nya terdaftar - jadi kedua sisi harus menyebut kata yang sama persis.
      /// </summary>
      public const string AdministrativeToolsModuleName = "Administrative Tools";

      /// <summary>
      /// Nama module tempat seluruh action approval tinggal - melihat request, memutuskannya, menarik
      /// kembali, dan berkomentar. Satu module untuk semua jenis dokumen, karena action-nya memang satu
      /// untuk semuanya; hak yang berlaku justru datang dari claim module pemilik dokumennya, diperiksa
      /// di dalam action setelah jenis dokumennya diketahui.
      /// </summary>
      public const string ApprovalModuleName = "core.approval";

      public const string DebuggerUserId = "99999999999999999999999999";
      
      public const string AdminUserId = "00000000000000000000000000";

      /// <summary>
      /// Nama akun debugger - akun yang dipakai pengembang saat menjalankan aplikasi tanpa melewati
      /// layar login. Sepasang dengan <see cref="AdminUserAccount"/> dan ada di sini karena alasan
      /// yang sama: akun ini tidak punya baris pengguna, jadi namanya tidak bisa dibaca dari sana.
      /// Nama ini hanya berarti kalau request-nya membawa token debug yang lolos verifikasi.
      /// </summary>
      public const string DebuggerUserAccount = "debugger";

      /// <summary>
      /// Nama header HTTP tempat token debug dikirim. Token inilah yang membuktikan bahwa pemanggil
      /// benar-benar pengembang yang berhak, sehingga ia boleh masuk tanpa password maupun access
      /// token. Ditulis di sini supaya server yang memeriksanya dan client yang mengirimnya menyebut
      /// kata yang sama persis.
      /// </summary>
      public const string DebugTokenHeader = "X-Em-Debug-Token";

      /// <summary>
      /// Nama header HTTP tempat client menyebut akun yang sedang aktif di layarnya. Isinya sebuah
      /// objek JSON yang membawa id sekaligus nama akun; bentuknya ditulis sekali di
      /// <see cref="Shared.UserHeaderProtocol"/> supaya client yang menyusunnya dan server yang
      /// membacanya menyebut kata yang sama persis.
      /// <para>
      /// Header ini tidak pernah menjadi sumber identitas dengan sendirinya - ia hanya dipercaya
      /// kalau <see cref="DebugTokenHeader"/> ikut dikirim dan lolos verifikasi; di luar itu
      /// identitas tetap datang dari access token.
      /// </para>
      /// </summary>
      public const string UserHeader = "X-Em-User";

      /// <summary>
      /// Nama header HTTP tempat payload sebuah action ber-stream dikirim. Pada action seperti itu body
      /// request sudah terpakai untuk isi stream-nya mentah-mentah, jadi parameter lain - paling banyak
      /// satu objek - menumpang di header ini. Isinya disusun dan dibaca lewat
      /// <see cref="Shared.StreamPayloadProtocol"/>, supaya client yang mengirim dan server yang
      /// membacanya menyebut kata dan bentuk yang sama persis.
      /// </summary>
      public const string StreamPayloadHeader = "Em-X-StreamPayload";

      /// <summary>
      /// Nama akun administrator bawaan, yang diketik di layar login. Akun ini tidak punya baris di
      /// tabel pengguna, jadi namanya tidak bisa dibaca dari sana - ia ditulis di sini supaya server
      /// yang mengenalinya saat login dan client yang menampilkannya menyebut kata yang sama persis.
      /// Nama ini juga dipesan: tidak ada pengguna biasa yang boleh memakainya.
      /// </summary>
      public const string AdminUserAccount = "admin";

      /// <summary>
      /// Nama tampilan akun administrator bawaan - yang muncul di layar, bukan yang diketik saat
      /// login. Sepasang dengan <see cref="AdminUserAccount"/> dan ada di sini karena alasan yang
      /// sama: tidak ada baris pengguna yang menyimpannya.
      /// </summary>
      public const string AdminUserFullName = "System Administrator";

      /// <summary>
      /// Penanda jenis kredensial password. Nilainya ikut tersimpan di baris kredensial, jadi kedua
      /// sisi harus menyebut kata yang sama persis - karena itu tempatnya di sini, bukan di salah
      /// satu sisi saja: yang memeriksa password ada di server, sementara yang membuatkan baris
      /// kosongnya saat user baru dibuat ada di client.
      /// </summary>
      public const string PasswordCredentialType = "PASSWORD";

      /// <summary>
      /// Nilai "tanpa batas akhir" untuk kolom masa berlaku yang tidak menerima <c>null</c>. Satu
      /// tanggal yang jauh di depan, dipilih sebagai tanggal terjauh yang masih diterima tipe
      /// <c>datetime</c> SQL Server, supaya perbandingan "masih berlaku" tetap berupa satu
      /// perbandingan tanggal biasa - tanpa cabang khusus di setiap tempat yang membacanya.
      /// </summary>
      public static readonly DateTime NoExpiry = new(9999, 12, 31);
   }
}