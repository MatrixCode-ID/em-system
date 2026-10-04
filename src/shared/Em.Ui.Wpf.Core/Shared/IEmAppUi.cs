using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Kontrak objek aplikasi khusus WPF di atas kontrak dasar <see cref="IEmApp"/>. Saat ini belum
   /// menambahkan anggota apa pun, tapi tetap didaftarkan di DI container sebagai tempat untuk
   /// kebutuhan yang memang hanya ada di sisi WPF.
   /// </summary>
   public interface IEmAppUi : IEmApp
   {
   }
}
