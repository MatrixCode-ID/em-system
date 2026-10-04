using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Em.Shared
{
   /// <summary>
   /// Representasi satu parameter pada request POST ke dispatcher <c>EmApp</c>. Body request POST
   /// berupa JSON array dari objek ini, satu entri per parameter method target, dicocokkan
   /// berdasarkan <see cref="ParameterOrdinal"/> (posisi parameter, bukan nama).
   /// </summary>
   public class PostMethodPayload
   {
      /// <summary>
      /// Konstruktor kosong, dipakai saat deserialisasi JSON dari body request.
      /// </summary>
      public PostMethodPayload() { }

      /// <summary>
      /// Membuat instance <see cref="PostMethodPayload"/> dari sebuah nilai, dipakai di sisi client
      /// (mis. frontend) untuk menyusun body request POST sebelum dikirim ke server.
      /// </summary>
      /// <typeparam name="T">Tipe nilai parameter.</typeparam>
      /// <param name="ordinal">Posisi/urutan parameter pada method target (dimulai dari 0).</param>
      /// <param name="value">Nilai parameter. Tidak boleh <c>null</c>.</param>
      /// <returns>Instance <see cref="PostMethodPayload"/> yang siap diserialisasi ke JSON.</returns>
      /// <exception cref="ArgumentNullException">Dilempar jika <paramref name="value"/> <c>null</c>.</exception>
      public static PostMethodPayload Build<T>(int ordinal, T value) {
         ArgumentNullException.ThrowIfNull(value);
         return new PostMethodPayload {
            ParameterType = typeof(T).FullName!,
            ParameterOrdinal = ordinal,
            ValueData = JsonSerializer.SerializeToNode(value, value.GetType()) ?? JsonValue.Create((string?)null)!
         };
      }

      /// <summary>
      /// Versi non-generic dari <see cref="Build{T}"/>, untuk pemanggil yang argumennya sudah telanjur
      /// bertipe <see cref="object"/> - mis. elemen sebuah <c>params object[]</c>. Kalau kasus seperti itu
      /// memakai versi generic-nya, <c>T</c> selalu tersimpul jadi <see cref="object"/> dan
      /// <see cref="ParameterType"/> ikut terisi <c>System.Object</c>, sehingga tidak pernah cocok dengan
      /// tipe parameter method target di server. Di sini nama tipe diambil dari tipe nyata
      /// <paramref name="value"/> saat runtime, jadi selalu sejalan dengan isi <see cref="ValueData"/>.
      /// </summary>
      /// <param name="ordinal">Posisi/urutan parameter pada method target (dimulai dari 0).</param>
      /// <param name="value">Nilai parameter. Tidak boleh <c>null</c>.</param>
      /// <returns>Instance <see cref="PostMethodPayload"/> yang siap diserialisasi ke JSON.</returns>
      /// <exception cref="ArgumentNullException">Dilempar jika <paramref name="value"/> <c>null</c>.</exception>
      public static PostMethodPayload Build(int ordinal, object value) {
         ArgumentNullException.ThrowIfNull(value);
         var valueType = value.GetType();
         return new PostMethodPayload {
            ParameterType = valueType.FullName!,
            ParameterOrdinal = ordinal,
            ValueData = JsonSerializer.SerializeToNode(value, valueType) ?? JsonValue.Create((string?)null)!
         };
      }

      /// <summary>
      /// Nama lengkap tipe (assembly-qualified/full name) dari nilai parameter, dipakai untuk
      /// resolusi tipe saat deserialisasi lewat <see cref="ConstructObject{T}"/>.
      /// </summary>
      public string ParameterType { get; init; } = "";

      /// <summary>
      /// Posisi/urutan parameter pada method target (dimulai dari 0), dipakai untuk pencocokan
      /// argumen secara posisional, bukan berdasarkan nama parameter.
      /// </summary>
      public int ParameterOrdinal { get; init; }

      /// <summary>
      /// Data nilai parameter dalam bentuk JSON node mentah, akan dideserialisasi ke tipe target
      /// saat <see cref="ConstructObject{T}"/> dipanggil.
      /// </summary>
      public JsonNode ValueData { get; init; } = null!;

      /// <summary>
      /// Mencoba me-resolve <see cref="ParameterType"/> menjadi <see cref="Type"/> yang sesungguhnya,
      /// dengan mencari lebih dulu lewat <see cref="Type.GetType(string, bool, bool)"/>, lalu
      /// fallback mencari di semua assembly yang ter-load pada <see cref="AppDomain.CurrentDomain"/>.
      /// </summary>
      /// <returns>Tipe yang ditemukan, atau <c>null</c> jika tidak ditemukan atau <see cref="ParameterType"/> kosong.</returns>
      private Type? GetResultType() {
         if (string.IsNullOrWhiteSpace(ParameterType)) {
            return null;
         }

         var resolvedType = Type.GetType(ParameterType, throwOnError: false, ignoreCase: true);
         if (resolvedType is not null) {
            return resolvedType;
         }

         foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            resolvedType = assembly.GetType(ParameterType, throwOnError: false, ignoreCase: true);
            if (resolvedType is not null) {
               return resolvedType;
            }
         }

         return null;
      }

      /// <summary>
      /// Mendeserialisasi <see cref="ValueData"/> menjadi nilai bertipe <typeparamref name="T"/>,
      /// setelah memvalidasi bahwa <see cref="ParameterType"/> memang cocok dengan <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">Tipe yang diharapkan untuk hasil deserialisasi.</typeparam>
      /// <param name="resultValue">Nilai hasil deserialisasi, jika berhasil; sebaliknya nilai default.</param>
      /// <param name="errorMessage">Pesan error jika gagal; string kosong jika berhasil.</param>
      /// <returns><c>true</c> jika deserialisasi berhasil; <c>false</c> jika gagal (lihat <paramref name="errorMessage"/>).</returns>
      /// <remarks>
      /// Pencocokan tipe dilakukan setelah <c>Nullable&lt;&gt;</c> dilepas dari kedua sisi. Ini perlu karena
      /// nilai yang di-<c>box</c> ke <see cref="object"/> kehilangan sifat nullable-nya - <c>Build</c> hanya
      /// melihat tipe dasarnya (mis. <c>ContactSearchType</c>), sementara parameter method target bisa saja
      /// dideklarasikan sebagai <c>ContactSearchType?</c>. Tanpa pelepasan ini semua parameter value type
      /// yang nullable akan selalu ditolak.
      /// <para>
      /// Tipe turunan ikut diterima, bukan cuma yang persis sama: pengirim kerap memegang model yang lebih
      /// lengkap daripada yang diminta method target - mis. baris hasil view yang mewarisi baris tabelnya -
      /// dan <c>Build</c> mencatat tipe nyata nilainya, bukan tipe parameter yang dituju. Isinya tetap
      /// dibaca sebagai <typeparamref name="T"/>, jadi kolom tambahan milik tipe turunan diabaikan dan yang
      /// sampai ke method target tetap persis tipe yang dideklarasikannya. Arah sebaliknya tetap ditolak:
      /// tipe induk tidak menjanjikan semua isi yang diminta turunannya.
      /// </para>
      /// </remarks>
      public bool ConstructObject<T>(out T resultValue, out string errorMessage) {
         resultValue = default!;
         errorMessage = "";
         var targetType = GetResultType();

         if (targetType is null) {
            errorMessage = "Type not found";
            return false;
         }

         var payloadType = Nullable.GetUnderlyingType(targetType) ?? targetType;
         var expectedType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

         // IsAssignableFrom rather than equality: a caller holding a subclass of what the target method
         // asks for is sending more than enough, and the deserialization below reads it back as the
         // declared type anyway. Array covariance comes along with it, which is what lets a batch of
         // rows go through the same way a single one does.
         if (!expectedType.IsAssignableFrom(payloadType)) {
            errorMessage = "Target type not supported";
            return false;
         }

         try {
            // Sengaja dideserialisasi ke typeof(T), bukan ke tipe hasil resolusi: untuk parameter nullable
            // hasilnya harus berupa Nullable<T> supaya cocok saat di-assign balik ke argumen method.
            var deserializedValue = JsonSerializer.Deserialize(ValueData.ToJsonString(), typeof(T));

            if (deserializedValue is T typedValue) {
               resultValue = typedValue;
               return true;
            }

            if (deserializedValue is null && default(T) is null) {
               resultValue = default!;
               return true;
            }

            errorMessage = "Invalid type";
            return false;
         }
         catch (Exception x) {
            errorMessage = x.Message;
            return false;
         }
      }
   }
}
