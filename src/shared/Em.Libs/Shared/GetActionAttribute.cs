namespace Em.Shared
{
   /// <summary>
   /// Menandai sebuah method service (atau method interface yang diimplementasikan) sebagai
   /// action yang bisa diakses lewat HTTP <c>GET</c> melalui dispatcher <c>EmApp</c>.
   /// Method yang ditandai atribut ini wajib mengembalikan <c>Task</c> atau <c>Task&lt;T&gt;</c>.
   /// </summary>
   /// <param name="action">
   /// Nama action opsional untuk override nama method sebagai identifier route.
   /// Jika <c>null</c>, nama method itu sendiri yang dipakai sebagai nama action.
   /// </param>
   /// <param name="claim">
   /// Nama claim yang dipersyaratkan action ini, tanpa nama module - lihat <see cref="Claim"/>.
   /// </param>
   [AttributeUsage(AttributeTargets.Method)]
   public class GetActionAttribute(string? action = null, string? claim = null) : Attribute
   {
      /// <summary>
      /// Menandai action <c>GET</c> yang batas waktunya berbeda dari batas waktu seluruh aplikasi.
      /// Dipakai hanya kalau memang perlu - action yang tidak menyebutkannya ikut
      /// <c>EmAppBuilder.HttpRequestTimeout</c>, dan itu yang berlaku untuk hampir semuanya.
      /// </summary>
      /// <param name="requestTimeoutSecond">
      /// Batas waktu kerja action ini dalam detik. Angka positif menggantikan batas waktu
      /// aplikasi; angka negatif berarti action ini tidak dibatasi waktu sama sekali.
      /// </param>
      /// <param name="action">
      /// Nama action opsional untuk override nama method sebagai identifier route, sama seperti
      /// pada constructor tanpa batas waktu.
      /// </param>
      /// <param name="claim">
      /// Nama claim yang dipersyaratkan action ini, sama seperti pada constructor tanpa batas waktu -
      /// lihat <see cref="Claim"/>.
      /// </param>
      /// <remarks>
      /// Ditulis sebagai overload constructor, bukan sebagai property seperti
      /// <see cref="IsPublicAction"/>, karena argumen atribut hanya boleh bertipe konstanta -
      /// <c>TimeSpan</c> maupun <c>int?</c> tidak sah di sana. Lewat overload, batasan itu hanya
      /// mengenai parameternya, sehingga <see cref="RequestTimeout"/> bisa benar-benar bertipe
      /// <c>TimeSpan?</c> dan "tidak disebut" cukup diwakili <c>null</c> - tanpa angka sandi yang
      /// harus dihafal pembacanya.
      /// <para>
      /// Perlu diingat saat memperpanjangnya: batas waktu di sini hanya menyatakan sampai kapan
      /// server mau bekerja, bukan sampai kapan client mau menunggu. Client punya batas waktunya
      /// sendiri, dan kalau batas itu tidak ikut dinaikkan, yang bertambah cuma lama server
      /// mengerjakan jawaban yang sudah tidak ditunggu siapa-siapa.
      /// </para>
      /// </remarks>
      /// <exception cref="ArgumentOutOfRangeException">
      /// Dilempar kalau <paramref name="requestTimeoutSecond"/> bernilai nol - batas waktu nol
      /// detik membatalkan action sebelum ia sempat berjalan, jadi ia hampir pasti salah ketik dan
      /// lebih baik berisik daripada diam.
      /// </exception>
      public GetActionAttribute(int requestTimeoutSecond, string? action = null, string? claim = null)
         : this(action, claim) {
         if (requestTimeoutSecond == 0) {
            throw new ArgumentOutOfRangeException(nameof(requestTimeoutSecond),
               "A request timeout of zero seconds would cancel the action before it starts. Pass a positive number of seconds, a negative number for no limit at all, or omit the argument to follow the application-wide timeout.");
         }

         RequestTimeout = requestTimeoutSecond < 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(requestTimeoutSecond);
      }

      /// <summary>
      /// Nama action yang di-override, atau <c>null</c> jika memakai nama method secara default.
      /// </summary>
      public string? Action { get; } = action;

      /// <summary>
      /// Nama claim yang dipersyaratkan action ini, ditulis tanpa nama module - module-nya diambil
      /// dari tempat action ini terdaftar, jadi tidak perlu (dan tidak boleh) disebut ulang di sini.
      /// </summary>
      /// <remarks>
      /// <c>null</c> - dan itu yang berlaku untuk hampir semua action - <b>bukan</b> berarti "tanpa
      /// syarat". Ia berarti pemanggil cukup punya claim <i>apa pun</i> milik module action ini; yang
      /// tidak punya satu pun claim di module itu tetap ditolak. Yang berarti "tanpa syarat" hanya
      /// <see cref="IsPublicAction"/>, dan itu properti yang berbeda.
      /// <para>
      /// Diisi berarti pemanggil harus punya persis claim itu di module yang sama - punya claim lain
      /// di module yang sama tidak cukup. Dipakai untuk action yang butuh hak lebih sempit daripada
      /// "boleh masuk ke module ini":
      /// <c>[GetAction(claim: "ViewPricing")]</c>.
      /// </para>
      /// <para>
      /// Administrator dan jalur token debug melewati pemeriksaan ini seluruhnya, sama seperti di sisi
      /// UI - jadi nilai di sini tidak pernah bisa menutup pintu bagi keduanya.
      /// </para>
      /// </remarks>
      public string? Claim { get; } = claim;

      /// <summary>
      /// Batas waktu khusus untuk action ini, atau <c>null</c> kalau action ini tidak menyebutkan
      /// batas waktunya sendiri dan karena itu ikut batas waktu seluruh aplikasi.
      /// <c>Timeout.InfiniteTimeSpan</c> berarti action ini sengaja dibiarkan tanpa batas.
      /// </summary>
      /// <remarks>
      /// Hanya bisa terisi lewat overload constructor yang menerima <c>requestTimeoutSecond</c> -
      /// tidak ada cara mengisinya sebagai named argument, karena <c>TimeSpan</c> bukan tipe
      /// argumen atribut yang sah.
      /// </remarks>
      public TimeSpan? RequestTimeout { get; }

      /// <summary>
      /// <c>true</c> jika action ini boleh diakses tanpa autentikasi. Default <c>false</c>, artinya action
      /// tertutup kecuali ditandai eksplisit — action baru yang lupa dianotasi otomatis ikut tertutup, bukan
      /// terbuka. Ditulis sebagai property (bukan parameter constructor) supaya terbaca di call site:
      /// <c>[GetAction(IsPublicAction = true)]</c>.
      /// </summary>
      /// <remarks>
      /// Ini satu-satunya penanda yang benar-benar berarti "tanpa syarat": action yang membawanya
      /// dilayani bahkan tanpa identitas sama sekali, jadi <see cref="Claim"/> tidak pernah diperiksa
      /// untuknya.
      /// </remarks>
      public bool IsPublicAction { get; set; }
   }
}
