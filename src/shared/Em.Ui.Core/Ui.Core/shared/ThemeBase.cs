namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Satu set warna tema untuk satu mode (terang atau gelap), mengikuti peran warna Material 3.
   /// Layar dan kontrol menyebut perannya, bukan kode warnanya, sehingga mengganti tema cukup
   /// mengganti objek ini. Pasangan <c>On...</c> adalah warna teks/ikon yang terbaca di atas peran
   /// pasangannya, mis. <see cref="OnPrimary"/> di atas <see cref="Primary"/>.
   /// </summary>
   /// <remarks>
   /// Objek ini hanya data - tidak menerapkan apa pun sendiri. Tiap client (dan pustaka kontrol yang
   /// dipakai modul, lewat <see cref="IThemeApplier"/>) membaca tema ini dan menerjemahkannya ke sistem
   /// tampilannya masing-masing.
   /// <para>
   /// Palet standar Em ada di <see cref="LightTheme"/> dan <see cref="DarkTheme"/>. Aplikasi yang
   /// butuh warna lain cukup menurunkan salah satunya, atau mengganti sebagian peran lewat object
   /// initializer, mis. <c>new LightTheme { Brand = ThemeColor.Parse("#7A1F2B") }</c>, lalu memasangnya
   /// ke <see cref="BrandingInfo"/>.
   /// </para>
   /// </remarks>
   public abstract class ThemeBase
   {
      /// <summary>
      /// Membuat tema untuk mode yang diberikan. Semua peran warna masih kosong (transparan) sampai
      /// diisi oleh kelas turunan atau object initializer.
      /// </summary>
      /// <param name="variant">Mode yang dilayani tema ini.</param>
      protected ThemeBase(ThemeVariant variant) {
         Variant = variant;
      }

      /// <summary>
      /// Mode yang dilayani tema ini: terang atau gelap.
      /// </summary>
      public ThemeVariant Variant { get; }

      #region Primary

      /// <summary>
      /// Warna utama: aksi tunggal yang paling penting di sebuah layar, dan penanda pilihan aktif.
      /// </summary>
      public ThemeColor Primary { get; init; }

      /// <summary>
      /// Teks/ikon di atas <see cref="Primary"/>.
      /// </summary>
      public ThemeColor OnPrimary { get; init; }

      /// <summary>
      /// Bidang bernuansa warna utama yang lebih tenang, mis. latar item terpilih.
      /// </summary>
      public ThemeColor PrimaryContainer { get; init; }

      /// <summary>
      /// Teks/ikon di atas <see cref="PrimaryContainer"/>.
      /// </summary>
      public ThemeColor OnPrimaryContainer { get; init; }

      #endregion

      #region Secondary

      /// <summary>
      /// Warna pendamping: penanda yang menyertai warna utama, mis. bidang terpilih di menu samping.
      /// </summary>
      public ThemeColor Secondary { get; init; }

      /// <summary>
      /// Teks/ikon di atas <see cref="Secondary"/>.
      /// </summary>
      public ThemeColor OnSecondary { get; init; }

      /// <summary>
      /// Bidang bernuansa warna pendamping yang lebih tenang.
      /// </summary>
      public ThemeColor SecondaryContainer { get; init; }

      /// <summary>
      /// Teks/ikon di atas <see cref="SecondaryContainer"/>.
      /// </summary>
      public ThemeColor OnSecondaryContainer { get; init; }

      #endregion

      #region Surface

      /// <summary>
      /// Latar halaman dan window.
      /// </summary>
      public ThemeColor Surface { get; init; }

      /// <summary>
      /// Teks/ikon utama di atas semua bidang.
      /// </summary>
      public ThemeColor OnSurface { get; init; }

      /// <summary>
      /// Teks/ikon sekunder yang lebih redup, mis. label dan keterangan.
      /// </summary>
      public ThemeColor OnSurfaceVariant { get; init; }

      /// <summary>
      /// Bidang bertingkat paling rendah di atas <see cref="Surface"/>.
      /// </summary>
      public ThemeColor SurfaceContainerLow { get; init; }

      /// <summary>
      /// Bidang bertingkat standar, mis. kartu.
      /// </summary>
      public ThemeColor SurfaceContainer { get; init; }

      /// <summary>
      /// Bidang bertingkat paling tinggi, mis. kolom isian, strip di dalam kartu, atau popup.
      /// </summary>
      public ThemeColor SurfaceContainerHigh { get; init; }

      #endregion

      #region Outline

      /// <summary>
      /// Garis pembatas kartu dan kolom isian.
      /// </summary>
      public ThemeColor Outline { get; init; }

      /// <summary>
      /// Garis pemisah yang lebih tenang, mis. antar baris.
      /// </summary>
      public ThemeColor OutlineVariant { get; init; }

      #endregion

      #region Status

      /// <summary>
      /// Kesalahan dan aksi yang merusak/berbahaya.
      /// </summary>
      public ThemeColor Error { get; init; }

      /// <summary>
      /// Bidang bernuansa kesalahan, mis. latar pesan galat.
      /// </summary>
      public ThemeColor ErrorContainer { get; init; }

      /// <summary>
      /// Teks/ikon di atas <see cref="ErrorContainer"/>.
      /// </summary>
      public ThemeColor OnErrorContainer { get; init; }

      /// <summary>
      /// Status berhasil/aktif.
      /// </summary>
      public ThemeColor Success { get; init; }

      /// <summary>
      /// Status yang perlu perhatian tetapi bukan kesalahan.
      /// </summary>
      public ThemeColor Warning { get; init; }

      /// <summary>
      /// Status informatif yang netral.
      /// </summary>
      public ThemeColor Info { get; init; }

      #endregion

      #region Others

      /// <summary>
      /// Tirai di belakang panel atau dialog yang sedang terbuka.
      /// </summary>
      public ThemeColor Scrim { get; init; }

      /// <summary>
      /// Warna panel merek, mis. panel branding di layar login dan kepala menu samping.
      /// </summary>
      public ThemeColor Brand { get; init; }

      /// <summary>
      /// Teks/ikon dan hiasan di atas <see cref="Brand"/>. Di layar login warna ini juga dipakai
      /// untuk hiasan samar di panel branding, jadi tampilnya bisa tipis, bukan bidang solid.
      /// </summary>
      public ThemeColor OnBrand { get; init; }

      #endregion
   }
}
