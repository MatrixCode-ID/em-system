using System.Globalization;
using System.Windows.Data;
using FontAwesome6;
using Em.Shared;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Translates <see cref="UiIconType"/> - the icon token stored in data - into the icon image that WPF
   /// really draws. The mapping lives here, not in the enum, because which image is used is a business of
   /// the presentation layer: another client may choose another image for the same token without a single
   /// line of data changing.
   /// </summary>
   /// <remarks>
   /// A token that has no counterpart here - including <see cref="UiIconType.Unspecified"/> - is drawn as
   /// a plain label. An icon is only presentation: an unknown token must not bring down the screen that
   /// draws it.
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
      /// Produces an <see cref="EFontAwesomeIcon"/> for the given icon token.
      /// </summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         value is UiIconType icon && Icons.TryGetValue(icon, out var glyph)
            ? glyph
            : EFontAwesomeIcon.Solid_Tag;

      /// <summary>
      /// Not supported: one icon image may be used by more than one token, so the way back has no single
      /// answer.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("A drawn icon cannot be converted back to an icon token.");
   }
}
