using System.Globalization;
using System.Windows.Data;
using FontAwesome6;
using Em.Shared;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Menerjemahkan <see cref="UiIconType"/> - token ikon yang tersimpan di data - menjadi gambar
   /// ikon yang benar-benar digambar WPF. Pemetaannya tinggal di sini, bukan di enum-nya, karena
   /// gambar mana yang dipakai adalah urusan lapisan tampilan: klien lain boleh memilih gambar lain
   /// untuk token yang sama tanpa satu baris data pun ikut berubah.
   /// </summary>
   /// <remarks>
   /// Token yang belum punya pasangan di sini - termasuk <see cref="UiIconType.Unspecified"/> -
   /// digambar sebagai label polos. Ikon hanyalah tampilan: satu token yang belum dikenal tidak
   /// boleh menjatuhkan layar yang menggambarnya.
   /// </remarks>
   public class UiIconConverter : IValueConverter
   {
      private static readonly Dictionary<UiIconType, EFontAwesomeIcon> Icons = new() {
         [UiIconType.Shield] = EFontAwesomeIcon.Solid_ShieldHalved,
         [UiIconType.Key] = EFontAwesomeIcon.Solid_Key,
         [UiIconType.Lock] = EFontAwesomeIcon.Solid_Lock,
         [UiIconType.Crown] = EFontAwesomeIcon.Solid_Crown,
         [UiIconType.Gavel] = EFontAwesomeIcon.Solid_Gavel,
         [UiIconType.CheckCircle] = EFontAwesomeIcon.Solid_CircleCheck,
         [UiIconType.User] = EFontAwesomeIcon.Solid_User,
         [UiIconType.Users] = EFontAwesomeIcon.Solid_Users,
         [UiIconType.UserTie] = EFontAwesomeIcon.Solid_UserTie,
         [UiIconType.IdCard] = EFontAwesomeIcon.Solid_IdCard,
         [UiIconType.Headset] = EFontAwesomeIcon.Solid_Headset,
         [UiIconType.Building] = EFontAwesomeIcon.Solid_Building,
         [UiIconType.Cart] = EFontAwesomeIcon.Solid_CartShopping,
         [UiIconType.Boxes] = EFontAwesomeIcon.Solid_BoxesStacked,
         [UiIconType.Truck] = EFontAwesomeIcon.Solid_Truck,
         [UiIconType.Factory] = EFontAwesomeIcon.Solid_Industry,
         [UiIconType.Wrench] = EFontAwesomeIcon.Solid_Wrench,
         [UiIconType.ClipboardCheck] = EFontAwesomeIcon.Solid_ClipboardCheck,
         [UiIconType.ChartLine] = EFontAwesomeIcon.Solid_ChartLine,
         [UiIconType.Coins] = EFontAwesomeIcon.Solid_Coins,
         [UiIconType.Invoice] = EFontAwesomeIcon.Solid_FileInvoiceDollar,
         [UiIconType.Calculator] = EFontAwesomeIcon.Solid_Calculator,
         [UiIconType.Database] = EFontAwesomeIcon.Solid_Database,
         [UiIconType.Gear] = EFontAwesomeIcon.Solid_Gear,
      };

      /// <summary>
      /// Menghasilkan <see cref="EFontAwesomeIcon"/> untuk token ikon yang diberikan.
      /// </summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is UiIconType icon && Icons.TryGetValue(icon, out var glyph)
            ? glyph
            : EFontAwesomeIcon.Solid_Tag;

      /// <summary>
      /// Tidak didukung: satu gambar ikon bisa saja dipakai lebih dari satu token, jadi jalan
      /// pulangnya tidak punya jawaban tunggal.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("A drawn icon cannot be converted back to an icon token.");
   }
}
