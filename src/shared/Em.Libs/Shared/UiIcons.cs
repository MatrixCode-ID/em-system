namespace Em.Shared
{
   /// <summary>
   /// Penerjemah antara token ikon yang tersimpan di data dan <see cref="UiIconType"/> yang dipakai
   /// di dalam program. Yang tersimpan adalah nama membernya, bukan angkanya, supaya isi data tetap
   /// terbaca manusia dan tidak ikut bergeser maknanya kalau urutan member di enum berubah.
   /// </summary>
   public static class UiIcons
   {
      /// <summary>
      /// Menerjemahkan token ikon yang tersimpan menjadi <see cref="UiIconType"/>. Token yang kosong,
      /// salah tulis, atau tidak dikenal versi program ini tidak dianggap kesalahan - semuanya jatuh
      /// ke <paramref name="fallback"/>. Ikon hanyalah tampilan: satu token asing yang ikut terbawa
      /// dari data lama, hasil impor, atau klien lain tidak boleh menggagalkan apa pun.
      /// </summary>
      /// <param name="token">Token ikon yang tersimpan, mis. <c>"Shield"</c>. Boleh <c>null</c>.</param>
      /// <param name="fallback">Nilai yang dipakai kalau tokennya tidak bisa diterjemahkan.</param>
      /// <returns>Ikon yang sesuai dengan token tersebut, atau <paramref name="fallback"/>.</returns>
      public static UiIconType Parse(string? token, UiIconType fallback) {
         if (string.IsNullOrWhiteSpace(token)) return fallback;

         // Enum.TryParse juga menerima teks berisi angka - termasuk angka yang bukan member mana pun -
         // dan akan mengembalikan nilai yang tidak ada namanya. Karena itu hasilnya masih diperiksa
         // dengan Enum.IsDefined sebelum diterima.
         return Enum.TryParse<UiIconType>(token.Trim(), ignoreCase: true, out var icon)
                && Enum.IsDefined(icon)
            ? icon
            : fallback;
      }

      /// <summary>
      /// Menerjemahkan <see cref="UiIconType"/> menjadi token yang disimpan di data.
      /// </summary>
      /// <param name="icon">Ikon yang akan disimpan.</param>
      /// <returns>
      /// Nama token ikonnya, atau <c>null</c> untuk <see cref="UiIconType.Unspecified"/> - "belum
      /// dipilih" tidak punya token, dan memang tidak seharusnya meninggalkan jejak apa pun di data.
      /// </returns>
      public static string? ToToken(UiIconType icon) {
         return icon == UiIconType.Unspecified ? null : icon.ToString();
      }
   }
}
