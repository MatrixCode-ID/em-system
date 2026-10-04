using System.Text.Json;
using System.Text.Json.Serialization;

namespace Em.Shared
{
   /// <summary>
   /// Aturan penyusunan isi header <see cref="Defaults.UserHeader"/> - keterangan client tentang akun
   /// siapa yang sedang aktif di layarnya. Kelas ini dipakai kedua sisi: client yang menyusunnya dan
   /// server yang membacanya harus menyebut kata yang sama persis.
   /// </summary>
   /// <remarks>
   /// Bentuknya sebuah objek JSON yang membawa id sekaligus nama akun, mis.
   /// <c>{"cUserId":"01K5Z0X9P7QW3M8V2ND6TJHFAB","cUserAccount":"budi"}</c>. Id ikut dibawa karena nama
   /// akun bisa berganti sementara id tidak, sehingga yang dipakai server adalah identitas yang memang
   /// permanen dan namanya tinggal jadi keterangan yang terbaca manusia di log.
   /// <para>
   /// <see cref="TryRead"/> juga masih menerima satu nama akun polos (teks yang tidak diawali
   /// <c>{</c>), supaya pengembang tetap bisa mengetik <c>X-Em-User: budi</c> di curl/Postman tanpa
   /// menyusun JSON.
   /// </para>
   /// <para>
   /// Aturan yang tidak berubah sedikit pun: isi header ini tidak pernah menjadi sumber identitas
   /// dengan sendirinya. Ia hanya dipercaya kalau request-nya juga membawa token debug yang lolos
   /// verifikasi - kalau pernah dipercaya sendirian, siapa pun cukup menyebut id mana saja untuk
   /// menjadi pemiliknya.
   /// </para>
   /// </remarks>
   public static class UserHeaderProtocol
   {
      /// <summary>
      /// Panjang maksimum isi header yang mau diproses. Longgar untuk sepasang id dan nama akun;
      /// gunanya supaya pemanggil yang belum terbukti apa-apa tidak bisa memaksa server mem-parse
      /// blob besar.
      /// </summary>
      public const int MaxHeaderLength = 1024;

      /// <summary>
      /// Menyusun isi header dari id dan/atau nama akun yang sedang aktif. Anggota yang kosong
      /// dilewatkan, jadi header tidak pernah memuat <c>"cUserId":null</c>.
      /// </summary>
      /// <param name="cUserId">Id pengguna yang sedang aktif, atau <c>null</c> kalau belum diketahui.</param>
      /// <param name="cUserAccount">Nama akun yang sedang aktif, atau <c>null</c> kalau belum diketahui.</param>
      /// <returns>
      /// Isi header siap kirim, atau teks kosong kalau tidak ada satu pun yang bisa disebutkan - dan
      /// header yang tidak menyebut siapa-siapa memang lebih baik tidak dikirim sama sekali.
      /// </returns>
      /// <remarks>
      /// Serialisasinya memakai encoder bawaan <see cref="System.Text.Json"/>, yang meng-escape
      /// karakter non-ASCII menjadi <c>\uXXXX</c>. Nilai yang dihasilkan karena itu selalu aman
      /// dikirim apa adanya sebagai nilai header, berapa pun isi nama akunnya.
      /// </remarks>
      public static string Create(string? cUserId, string? cUserAccount) {
         var hasId = !string.IsNullOrWhiteSpace(cUserId);
         var hasAccount = !string.IsNullOrWhiteSpace(cUserAccount);
         if (!hasId && !hasAccount) {
            return string.Empty;
         }

         return JsonSerializer.Serialize(new UserHeaderPayload(
            hasId ? cUserId : null,
            hasAccount ? cUserAccount : null));
      }

      /// <summary>
      /// Membaca isi header menjadi id dan nama akun yang disebutnya.
      /// </summary>
      /// <param name="header">Isi header apa adanya.</param>
      /// <param name="cUserId">Id yang disebut header, atau <c>null</c> kalau tidak disebut.</param>
      /// <param name="cUserAccount">Nama akun yang disebut header, atau <c>null</c> kalau tidak disebut.</param>
      /// <returns>
      /// <c>true</c> kalau header menyebut setidaknya salah satu dari keduanya. Header yang ada tapi
      /// tidak bisa dibaca sama sekali dianggap tidak menyebut siapa-siapa, bukan sebuah kesalahan:
      /// yang menentukan identitas tetap token yang dibawa request, bukan header ini.
      /// </returns>
      public static bool TryRead(string? header, out string? cUserId, out string? cUserAccount) {
         cUserId = null;
         cUserAccount = null;

         if (string.IsNullOrWhiteSpace(header) || header.Length > MaxHeaderLength) {
            return false;
         }

         var text = header.Trim();

         // Bentuk lama - satu nama akun polos. Dipertahankan supaya pengembang tetap bisa mengetiknya
         // langsung di curl/Postman, dan supaya tripwire di gerbang tetap menangkap header yang
         // ditulis dengan bentuk itu.
         if (text[0] != '{') {
            cUserAccount = text;
            return true;
         }

         UserHeaderPayload? payload;
         try {
            payload = JsonSerializer.Deserialize<UserHeaderPayload>(text);
         }
         catch (JsonException) {
            return false;
         }

         if (payload is null) {
            return false;
         }

         cUserId = string.IsNullOrWhiteSpace(payload.cUserId) ? null : payload.cUserId!.Trim();
         cUserAccount = string.IsNullOrWhiteSpace(payload.cUserAccount) ? null : payload.cUserAccount!.Trim();
         return cUserId is not null || cUserAccount is not null;
      }

      private sealed record UserHeaderPayload(
         [property: JsonPropertyName("cUserId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
         string? cUserId,
         [property: JsonPropertyName("cUserAccount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
         string? cUserAccount);
   }
}
