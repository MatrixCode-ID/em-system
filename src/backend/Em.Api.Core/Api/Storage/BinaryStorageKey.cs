using System.Text;

namespace Em.Api.Core.Storage
{
   /// <summary>
   /// Aturan bentuk kunci <see cref="IBinaryStorage"/>, beserta cara menyusun dan membakukannya.
   /// Dipakai setiap implementasi penyimpanan, supaya kunci yang sah di satu implementasi juga sah di
   /// implementasi lain - termasuk yang menyimpan isinya di luar mesin ini.
   /// </summary>
   /// <remarks>
   /// Kunci terdiri dari beberapa bagian yang dipisahkan garis miring, seperti path. Yang dilarang
   /// adalah segalanya yang membuat sebuah kunci bisa menunjuk tempat lain dari yang terbaca: bagian
   /// kosong, bagian berisi titik saja, garis miring balik, dan karakter yang tidak terbaca. Pembatasan
   /// ini lebih ketat daripada yang dituntut sistem berkas, karena kunci yang sama harus tetap sah saat
   /// penyimpanannya kelak berpindah ke jaringan.
   /// </remarks>
   public static class BinaryStorageKey
   {
      /// <summary>Panjang maksimum sebuah kunci, dihitung dalam karakter.</summary>
      public const int MaxLength = 1024;

      /// <summary>Panjang maksimum satu bagian kunci.</summary>
      public const int MaxSegmentLength = 255;

      /// <summary>Pemisah antar bagian kunci.</summary>
      public const char Separator = '/';

      /// <summary>
      /// Membakukan sebuah kunci: garis miring ganda dirapatkan, garis miring di awal dan akhir
      /// dibuang, lalu hasilnya diperiksa terhadap aturan bentuk kunci.
      /// </summary>
      /// <param name="key">Kunci yang dibakukan.</param>
      /// <returns>Kunci dalam bentuk bakunya.</returns>
      /// <exception cref="ArgumentException">
      /// Dilempar kalau kuncinya kosong, terlalu panjang, memuat bagian yang tidak sah, atau memuat
      /// karakter yang tidak diizinkan. Pesannya menyebut bagian mana yang menolaknya.
      /// </exception>
      public static string Normalize(string key) {
         if (string.IsNullOrWhiteSpace(key)) {
            throw new ArgumentException("A storage key must not be empty.", nameof(key));
         }

         if (key.Contains('\\')) {
            throw new ArgumentException(
               $"Storage key '{key}' contains a backslash; the only separator allowed is '{Separator}'.", nameof(key));
         }

         var segments = key.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
         if (segments.Length == 0) {
            throw new ArgumentException($"Storage key '{key}' has no usable part.", nameof(key));
         }

         foreach (var segment in segments) {
            EnsureValidSegment(key, segment);
         }

         var normalized = string.Join(Separator, segments);
         if (normalized.Length > MaxLength) {
            throw new ArgumentException(
               $"Storage key '{normalized}' is {normalized.Length} characters, which exceeds the maximum of {MaxLength}.",
               nameof(key));
         }

         return normalized;
      }

      /// <summary>
      /// Menyusun kunci dari beberapa bagian, lalu membakukannya. Bagian yang kosong dilewati, jadi
      /// pemanggil tidak perlu mengurus garis miring di sambungannya.
      /// </summary>
      /// <param name="parts">Bagian-bagian kuncinya, berurutan.</param>
      /// <returns>Kunci dalam bentuk bakunya.</returns>
      /// <exception cref="ArgumentException">Dilempar dengan alasan yang sama seperti <see cref="Normalize"/>.</exception>
      public static string Combine(params string?[] parts) {
         ArgumentNullException.ThrowIfNull(parts);
         var builder = new StringBuilder();

         foreach (var part in parts) {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (builder.Length > 0) builder.Append(Separator);
            builder.Append(part);
         }

         return Normalize(builder.ToString());
      }

      /// <summary>Apakah sebuah kunci sah menurut aturan bentuk kunci.</summary>
      /// <param name="key">Kunci yang diperiksa.</param>
      public static bool IsValid(string? key) {
         if (key is null) return false;

         try {
            Normalize(key);
            return true;
         }
         catch (ArgumentException) {
            return false;
         }
      }

      private static void EnsureValidSegment(string key, string segment) {
         if (segment.Length > MaxSegmentLength) {
            throw new ArgumentException(
               $"Storage key '{key}' has a part of {segment.Length} characters, which exceeds the maximum of {MaxSegmentLength}.",
               nameof(key));
         }

         // A part made of dots only is what lets a key point outside the place it reads as,
         // so it is refused here rather than resolved away later.
         if (segment.All(r => r == '.')) {
            throw new ArgumentException(
               $"Storage key '{key}' has a part made of dots only ('{segment}'), which is not a name.", nameof(key));
         }

         foreach (var c in segment) {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') continue;

            throw new ArgumentException(
               $"Storage key '{key}' contains the character '{c}', which is not allowed. " +
               "A key part may only contain ASCII letters, digits, '-', '_' and '.'.", nameof(key));
         }
      }
   }
}
