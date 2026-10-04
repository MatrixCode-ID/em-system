using System.Windows.Media;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Pemuat logo tambahan untuk format gambar yang tidak dikenal WPF sendiri (mis. <c>.svg</c>).
   /// Core hanya memuat bitmap (<c>.png</c>, <c>.jpg</c>, <c>.ico</c>, <c>.bmp</c>); aplikasi yang
   /// memakai logo dalam format lain memasang pemuat ini lewat
   /// <see cref="EmAppBuilder.AddLogoImageLoader{T}"/>.
   /// </summary>
   /// <remarks>
   /// Setiap pemuat yang terdaftar ditanya bergiliran sebelum core mencoba memuatnya sebagai bitmap.
   /// Pemuat yang tidak mengenali formatnya cukup mengembalikan <c>null</c>.
   /// </remarks>
   public interface ILogoImageLoader
   {
      /// <summary>
      /// Mencoba memuat logo dari <paramref name="source"/>.
      /// </summary>
      /// <param name="source">Lokasi logo, biasanya pack URI ke resource milik project aplikasi.</param>
      /// <returns>
      /// Gambar logo, atau <c>null</c> kalau format ini bukan urusan pemuat ini. Kegagalan memuat file
      /// yang memang formatnya dikenali boleh dilempar sebagai exception; core lalu memakai logo bawaan.
      /// </returns>
      ImageSource? TryLoad(Uri source);
   }
}
