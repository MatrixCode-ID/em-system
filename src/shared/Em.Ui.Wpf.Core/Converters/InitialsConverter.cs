using System.Globalization;
using System.Windows.Data;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Mengubah sebuah nama (mis. nama lengkap kontak atau nama akun) menjadi inisial pendek
   /// untuk ditampilkan di dalam avatar bundar pada daftar. Diambil maksimal dua huruf: huruf
   /// pertama dari kata pertama dan kata terakhir, supaya inisial tetap terbaca meski nama
   /// terdiri dari banyak kata.
   /// </summary>
   /// <remarks>
   /// Kalau nama kosong atau tidak berisi huruf sama sekali, hasilnya adalah tanda tanya
   /// ("?"), sehingga avatar tetap punya isi dan ukuran barisnya tidak berubah.
   /// </remarks>
   public class InitialsConverter : IValueConverter
   {
      /// <summary>
      /// Menghasilkan inisial (maksimal dua huruf, huruf besar) dari nilai teks yang diberikan.
      /// </summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
         var words = (value as string ?? string.Empty)
            .Split([' ', '\t', '.', ',', '-'], StringSplitOptions.RemoveEmptyEntries);

         if (words.Length == 0) return "?";

         var first = words[0][0];
         var last = words[^1][0];

         return words.Length == 1
            ? char.ToUpper(first, culture).ToString()
            : string.Concat(char.ToUpper(first, culture), char.ToUpper(last, culture));
      }

      /// <summary>
      /// Tidak didukung: inisial tidak bisa dikembalikan menjadi nama aslinya.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("Initials cannot be converted back to a name.");
   }
}
