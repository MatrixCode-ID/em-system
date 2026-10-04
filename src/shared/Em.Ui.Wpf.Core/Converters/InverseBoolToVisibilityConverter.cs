using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Kebalikan <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>: <c>true</c>
   /// menyembunyikan, <c>false</c> menampilkan. Dipakai sepasang dengan yang asli untuk dua sisi
   /// dari satu keadaan - mis. teks yang tampil saat diam dan isian yang menggantikannya saat
   /// diedit - supaya keduanya membaca satu property yang sama alih-alih dua yang bisa berselisih.
   /// </summary>
   /// <remarks>
   /// Nilai yang bukan <see cref="bool"/> - termasuk binding yang jalurnya tidak ketemu - dianggap
   /// <c>false</c>, sehingga yang terlihat adalah keadaan diam, bukan layar yang kosong.
   /// </remarks>
   public class InverseBoolToVisibilityConverter : IValueConverter
   {
      /// <summary>Menghasilkan <see cref="Visibility.Collapsed"/> untuk <c>true</c>, dan sebaliknya.</summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is true ? Visibility.Collapsed : Visibility.Visible;

      /// <summary>Mengembalikan <see cref="Visibility"/> menjadi nilai boolean yang membangkitkannya.</summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is Visibility.Collapsed or Visibility.Hidden;
   }
}
