using System.Buffers.Text;
using System.Text.Json;

namespace Em.Shared
{
   /// <summary>
   /// Aturan penyusunan isi header <see cref="Defaults.StreamPayloadHeader"/> - payload yang ikut
   /// bersama sebuah action ber-stream. Kelas ini dipakai kedua sisi: client yang menyusunnya dan
   /// server yang membacanya harus memakai bentuk yang sama persis.
   /// </summary>
   /// <remarks>
   /// Bentuknya JSON UTF-8 dari satu objek, lalu di-encode Base64Url supaya aman dikirim sebagai nilai
   /// header apa pun isinya (termasuk karakter non-ASCII dan baris baru). Serialisasinya memakai
   /// <see cref="Defaults.ResponseJsonOptions"/>, jadi nama property JSON-nya persis nama property C#.
   /// <para>
   /// Tipe tujuan saat membaca tidak pernah datang dari client: server mengambilnya dari parameter
   /// action yang akan menerimanya, sehingga pemanggil tidak bisa menyuruh server membuat objek dari
   /// tipe sembarang.
   /// </para>
   /// </remarks>
   public static class StreamPayloadProtocol
   {
      /// <summary>
      /// Panjang maksimum isi header, dihitung sesudah di-encode. Header dimaksudkan untuk
      /// keterangan kecil yang menyertai stream (nama file, folder tujuan, pilihan timpa), bukan untuk
      /// data - data besar tempatnya di stream itu sendiri. Batas ini ditegakkan di kedua sisi: client
      /// menolak sebelum mengirim, server menjawab 400.
      /// </summary>
      public const int MaxHeaderLength = 4096;

      /// <summary>
      /// Menyusun isi header dari sebuah objek payload.
      /// </summary>
      /// <param name="payload">Objek yang dikirim; diserialisasi sebagai tipe aslinya.</param>
      /// <returns>Isi header siap kirim.</returns>
      /// <exception cref="InvalidOperationException">
      /// Hasilnya lebih panjang dari <see cref="MaxHeaderLength"/> - payload terlalu besar untuk sebuah
      /// header dan harus dikecilkan.
      /// </exception>
      public static string Encode(object payload) {
         ArgumentNullException.ThrowIfNull(payload);

         var json = JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), Defaults.ResponseJsonOptions);
         var encoded = Base64Url.EncodeToString(json);
         if (encoded.Length > MaxHeaderLength) {
            throw new InvalidOperationException(
               $"The stream payload is {encoded.Length:N0} characters once encoded; the limit is {MaxHeaderLength:N0}.");
         }

         return encoded;
      }

      /// <summary>
      /// Membaca isi header kembali menjadi objek bertipe <paramref name="type"/>.
      /// </summary>
      /// <param name="header">Isi header apa adanya.</param>
      /// <param name="type">Tipe tujuan, diambil dari parameter action penerimanya.</param>
      /// <param name="value">Objek hasil pembacaan, atau <c>null</c> kalau gagal (atau JSON-nya memang <c>null</c>).</param>
      /// <param name="error">Alasan kegagalan yang pantas dikirim balik ke pemanggil, atau <c>null</c> kalau berhasil.</param>
      /// <returns><c>true</c> kalau header berhasil dibaca menjadi tipe yang diminta.</returns>
      public static bool TryDecode(string header, Type type, out object? value, out string? error) {
         value = null;

         if (header.Length > MaxHeaderLength) {
            error = $"The {Defaults.StreamPayloadHeader} header is longer than {MaxHeaderLength:N0} characters.";
            return false;
         }

         byte[] json;
         try {
            json = Base64Url.DecodeFromChars(header.Trim());
         }
         catch (FormatException) {
            error = $"The {Defaults.StreamPayloadHeader} header is not valid Base64Url.";
            return false;
         }

         try {
            value = JsonSerializer.Deserialize(json, type, Defaults.ResponseJsonOptions);
         }
         catch (JsonException ex) {
            error = $"The {Defaults.StreamPayloadHeader} header does not hold a valid {type.Name}: {ex.Message}";
            return false;
         }

         error = null;
         return true;
      }
   }
}
