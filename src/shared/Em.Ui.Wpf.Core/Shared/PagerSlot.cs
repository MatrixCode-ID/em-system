using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Satu posisi pada deretan tombol halaman: entah sebuah nomor halaman yang bisa diklik,
   /// atau sebuah sela ("...") yang mewakili halaman-halaman yang tidak muat ditampilkan.
   /// Sela itu sendiri bisa diklik untuk membuka isian lompat-ke-halaman.
   /// </summary>
   public sealed class PagerSlot : NotifyPropertyBase
   {
      /// <summary>
      /// Membuat sela ("...") di antara dua deret nomor halaman.
      /// </summary>
      public static PagerSlot Gap() => new() { IsGap = true };

      /// <summary>
      /// Membuat satu tombol nomor halaman.
      /// </summary>
      /// <param name="page">Nomor halaman yang diwakili, dimulai dari 1.</param>
      /// <param name="isCurrent">Apakah halaman ini yang sedang ditampilkan.</param>
      public static PagerSlot Of(int page, bool isCurrent) =>
         new() { Page = page, IsCurrent = isCurrent };

      /// <summary>
      /// Nomor halaman yang diwakili posisi ini. Tidak berarti apa-apa kalau <see cref="IsGap"/>
      /// bernilai <c>true</c>.
      /// </summary>
      public int Page {
         get => Get<int>();
         set => Set(value);
      }

      /// <summary>
      /// Menandakan posisi ini adalah sela ("..."), bukan nomor halaman.
      /// </summary>
      public bool IsGap {
         get => Get<bool>();
         set => Set(value);
      }

      /// <summary>
      /// Menandakan halaman ini yang sedang aktif. Di-bind dua arah ke tombol halaman, jadi
      /// nilainya juga berubah ketika pemakai mengklik tombolnya.
      /// </summary>
      public bool IsCurrent {
         get => Get<bool>();
         set => Set(value);
      }
   }
}
