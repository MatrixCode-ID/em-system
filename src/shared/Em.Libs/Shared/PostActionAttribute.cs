namespace Em.Shared
{
   /// <summary>
   /// Menandai sebuah method service (atau method interface yang diimplementasikan) sebagai
   /// action yang bisa diakses lewat HTTP <c>POST</c> melalui dispatcher <c>EmApp</c>.
   /// Parameter method di-binding dari body request berupa JSON array <see cref="PostMethodPayload"/>.
   /// Method yang ditandai atribut ini wajib mengembalikan <c>Task</c> atau <c>Task&lt;T&gt;</c>.
   /// </summary>
   /// <param name="action">
   /// Nama action opsional untuk override nama method sebagai identifier route.
   /// Jika dikosongkan, nama method itu sendiri yang dipakai sebagai nama action.
   /// </param>
   /// <param name="claim">
   /// Nama claim yang dipersyaratkan action ini, tanpa nama module - lihat <see cref="Claim"/>.
   /// </param>
   [AttributeUsage(AttributeTargets.Method)]
   public class PostActionAttribute(string action = "", string? claim = null) : Attribute
   {
      /// <summary>
      /// Nama action yang di-override, atau string kosong jika memakai nama method secara default.
      /// </summary>
      public string Action { get; } = action;

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
      /// <c>[PostAction(claim: "ApprovePricing")]</c>.
      /// </para>
      /// <para>
      /// Administrator dan jalur token debug melewati pemeriksaan ini seluruhnya, sama seperti di sisi
      /// UI - jadi nilai di sini tidak pernah bisa menutup pintu bagi keduanya.
      /// </para>
      /// </remarks>
      public string? Claim { get; } = claim;

      /// <summary>
      /// <c>true</c> jika action ini boleh diakses tanpa autentikasi. Default <c>false</c>, artinya action
      /// tertutup kecuali ditandai eksplisit — action baru yang lupa dianotasi otomatis ikut tertutup, bukan
      /// terbuka. Ditulis sebagai property (bukan parameter constructor) supaya terbaca di call site:
      /// <c>[PostAction(IsPublicAction = true)]</c>.
      /// </summary>
      /// <remarks>
      /// Ini satu-satunya penanda yang benar-benar berarti "tanpa syarat": action yang membawanya
      /// dilayani bahkan tanpa identitas sama sekali, jadi <see cref="Claim"/> tidak pernah diperiksa
      /// untuknya.
      /// </remarks>
      public bool IsPublicAction { get; set; }
   }
}
