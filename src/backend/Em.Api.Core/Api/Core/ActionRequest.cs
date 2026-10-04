using Em.Shared;

namespace Em.Api.Core
{
   /// <summary>
   /// Dari mana identitas pemanggil dibuktikan.
   /// </summary>
   public enum CallerSource
   {
      /// <summary>Tidak ada yang membuktikan apa-apa - request datang tanpa identitas.</summary>
      None,

      /// <summary>Identitas datang dari access token yang dibawa header <c>Authorization</c>.</summary>
      AccessToken,

      /// <summary>Identitas datang dari token debug milik pengembang.</summary>
      DebugToken
   }

   /// <summary>
   /// Seluruh keterangan tentang satu request yang sedang dikerjakan: siapa pemanggilnya sejauh yang
   /// bisa dibuktikan, lewat jalan mana ia membuktikannya, dan ke action mana ia ditujukan. Disusun
   /// gerbang di <c>EmApp.ProcessRequest</c> sekali, lalu dibaca apa adanya - pasangan
   /// <see cref="ActionResult"/>: satu masuk, satu keluar.
   /// </summary>
   /// <remarks>
   /// Objek ini immutable, jadi aman dibaca dari mana saja selama request berjalan. Yang tidak boleh:
   /// menyimpannya ke field milik objek yang hidup lebih lama dari request-nya, atau membawanya ke
   /// pekerjaan latar. Ia potret satu request - dipakai di tempat lain, ia menjawab pertanyaan yang
   /// tidak pernah ditanyakan di sana.
   /// <para>
   /// Cara mendapatkannya: di dalam service module lewat <c>ServicesBase.Request</c>; di kelas
   /// pembantu yang dibuat DI dengan memintanya di konstruktor; di kode yang tidak punya keduanya
   /// dengan menerimanya sebagai parameter biasa.
   /// </para>
   /// </remarks>
   public sealed record ActionRequest
   {
      /// <summary>
      /// Satu instance untuk keadaan "tidak ada request" - dipakai sebagai nilai awal
      /// <c>ServicesBase.Request</c> dan sebagai jawaban di luar jalur request (startup, penyemaian,
      /// pekerjaan latar). Seluruh identitasnya kosong, jadi setiap pemeriksa hak di bawah menjawab
      /// 401: tidak ada request berarti tidak ada yang terbukti.
      /// </summary>
      public static ActionRequest None { get; } = new();

      /// <summary>
      /// Pengguna yang memanggil action ini, sesuai apa yang dibuktikan request-nya. <c>null</c>
      /// berarti tidak ada identitas sama sekali - yang wajar untuk action publik.
      /// </summary>
      public string? cUserId { get; init; }

      /// <summary>
      /// Nama akun pemanggil, dan hanya kalau namanya memang terbukti dari data. Jalur access token
      /// membiarkannya <c>null</c>: token hanya membawa id, dan menerjemahkannya jadi nama akun
      /// berarti satu query tambahan di setiap request. Nama yang menempel di header tidak pernah
      /// dipakai mengisinya - ia datang dari pemanggil, bukan dari data.
      /// </summary>
      public string? cUserAccount { get; init; }

      /// <summary>
      /// Sesi yang menerbitkan token pemanggil - inilah yang diakhiri saat pengguna keluar dari
      /// perangkat ini saja. Selalu <c>null</c> di jalur token debug, karena jalur itu memang tidak
      /// membuka sesi.
      /// </summary>
      public string? cUserSessionId { get; init; }

      /// <summary>
      /// Apakah pemanggil berhak penuh sebagai administrator. Selalu <c>false</c> selama
      /// <see cref="cUserId"/> kosong - tidak ada identitas, tidak ada hak.
      /// </summary>
      public bool IsAdmin { get; init; }

      /// <summary>
      /// Seluruh hak yang berlaku untuk pemanggil saat ini - yang diberikan langsung maupun yang datang
      /// lewat role, sudah disaring masa berlakunya. Dimuat ulang setiap request, bukan dibawa token:
      /// hak berubah jauh lebih sering daripada status administrator, dan kalau ia tersimpan di token
      /// maka setiap pencabutan hak harus mengakhiri seluruh sesi pemiliknya.
      /// </summary>
      /// <remarks>
      /// Kosong untuk pemanggil tanpa identitas, dan juga untuk administrator maupun jalur token debug -
      /// keduanya melewati pemeriksaan hak seluruhnya, jadi memuatnya berarti membayar satu query untuk
      /// jawaban yang tidak akan pernah dibaca. Kosong di sini karena itu bukan berarti "tidak punya
      /// hak apa pun"; periksa <see cref="IsAdmin"/> dan <see cref="IsDebugRequest"/> lebih dulu.
      /// <para>
      /// Namanya sengaja sama dengan <c>User.AvailableClaims</c> milik client: satu arti, dua sisi.
      /// </para>
      /// </remarks>
      public ClaimAction[] Claims { get; init; } = [];

      /// <summary>
      /// Lewat jalan mana identitas di atas dibuktikan.
      /// </summary>
      public CallerSource Source { get; init; }

      /// <summary><c>true</c> kalau request ini datang lewat token debug.</summary>
      public bool IsDebugRequest => Source == CallerSource.DebugToken;

      /// <summary><c>true</c> kalau ada pemanggil yang identitasnya terbukti.</summary>
      public bool IsAuthenticated => cUserId is not null;

      /// <summary>
      /// Nama key debug yang dipakai request ini. Terisi hanya di jalur token debug.
      /// </summary>
      public string? DebugKeyName { get; init; }

      /// <summary>
      /// <c>true</c> kalau pengembangnya sedang menyamar jadi akun lain alih-alih memakai akun
      /// debugger. Terisi hanya di jalur token debug.
      /// </summary>
      public bool IsImpersonating { get; init; }

      /// <summary>
      /// Action yang dituju, dalam bentuk <c>{module}/{action}</c>.
      /// </summary>
      public string RouteLabel { get; init; } = string.Empty;

      /// <summary>
      /// Alamat asal request sejauh yang diketahui host, atau <c>null</c> kalau tidak diketahui.
      /// </summary>
      public string? CallerAddress { get; init; }

      /// <summary>
      /// Kapan gerbang menerima request ini, dalam UTC.
      /// </summary>
      public DateTime ReceivedAtUtc { get; init; }

      #region Pemeriksa hak

      /// <summary>
      /// <c>true</c> kalau <paramref name="cUserId"/> adalah pemanggil itu sendiri.
      /// </summary>
      public bool IsSelf(string cUserId) => this.cUserId is { } caller && caller == cUserId;

      /// <summary>
      /// Menuntut adanya pemanggil yang terbukti, lalu mengembalikan id-nya.
      /// </summary>
      /// <exception cref="ActionException">401 kalau request tidak membawa identitas apa pun.</exception>
      public string RequireUserId() =>
         cUserId ?? throw new ActionException(NotSignedInMessage, 401);

      /// <summary>
      /// Menuntut adanya sesi yang terbukti, lalu mengembalikan id-nya. Jalur token debug tidak punya
      /// sesi, jadi ia selalu ditolak di sini - dan memang benar begitu: tidak ada sesi yang bisa
      /// diakhiri.
      /// </summary>
      /// <exception cref="ActionException">401 kalau request tidak membawa sesi.</exception>
      public string RequireSessionId() =>
         cUserSessionId ?? throw new ActionException(NotSignedInMessage, 401);

      /// <summary>
      /// Menuntut pemanggil yang berhak penuh sebagai administrator.
      /// </summary>
      /// <exception cref="ActionException">
      /// 401 kalau belum ada identitas; 403 kalau identitasnya ada tapi bukan administrator.
      /// </exception>
      public void RequireAdmin() {
         RequireUserId();

         // 403, bukan 401: siapa pemanggilnya sudah terbukti, dan membuktikannya lagi tidak akan
         // mengubah jawaban. Client yang memperbarui token setiap kali kena 401 akan mengejar token
         // baru terus-menerus untuk permintaan yang memang tidak akan pernah diizinkan, kalau yang
         // kedua ikut dijawab 401.
         if (!IsAdmin) {
            throw new ActionException("Only an administrator may do this.", 403);
         }
      }

      /// <summary>
      /// Menuntut pemanggil yang bertindak atas dirinya sendiri, atau seorang administrator kalau
      /// yang disebut orang lain. Tanpa pemeriksaan ini siapa pun bisa membaca sesi atau menimpa
      /// password orang lain.
      /// </summary>
      /// <param name="cUserId">Pengguna yang hendak dikenai tindakan.</param>
      /// <exception cref="ActionException">
      /// 401 kalau belum ada identitas; 403 kalau identitasnya ada tapi bukan dirinya dan bukan
      /// administrator - lihat alasannya di <see cref="RequireAdmin"/>.
      /// </exception>
      public void RequireSelfOrAdmin(string cUserId) {
         RequireUserId();
         if (IsSelf(cUserId)) return;

         if (!IsAdmin) {
            throw new ActionException("Only an administrator may do this for another user.", 403);
         }
      }

      // Satu kalimat untuk setiap cara sebuah request bisa datang tanpa identitas, supaya jawabannya
      // tidak menceritakan bagian mana dari token yang tidak ada.
      private const string NotSignedInMessage = "This action requires a signed-in caller.";

      #endregion
   }
}
