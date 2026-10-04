using Em.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Kontrak objek aplikasi khusus sisi MAUI, menambahkan hal-hal yang dibaca layar - pengaturan
   /// brand dan aturan kata sandi - di atas kontrak dasar <see cref="IEmApp"/>. Ada supaya sebuah
   /// layar bisa meminta keduanya dari DI container tanpa harus bergantung pada class aplikasi yang
   /// konkret.
   /// </summary>
   public interface IEmAppUi : IEmApp
   {
      /// <summary>
      /// Pengaturan tampilan brand yang berlaku, selalu terisi - lihat <see cref="BrandingInfo"/>
      /// soal nilai bawaannya.
      /// </summary>
      BrandingInfo Branding { get; }

      /// <summary>
      /// Aturan kata sandi yang berlaku, selalu terisi - lihat <see cref="Shared.PasswordPolicy"/>
      /// soal nilai bawaannya.
      /// </summary>
      PasswordPolicy PasswordPolicy { get; }
   }
}
