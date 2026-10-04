namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Penerap tema tambahan, untuk pustaka kontrol pihak ketiga yang punya sistem tema sendiri
   /// (mis. pustaka yang dipakai modul untuk grid atau editor khusus). Engine sudah menerapkan warna
   /// tema ke kontrolnya sendiri; penerap ini menerjemahkan tema yang sama ke pustaka tersebut supaya
   /// semua layar tetap satu warna.
   /// </summary>
   /// <remarks>
   /// Didaftarkan lewat <c>AddThemeApplier&lt;T&gt;()</c> di builder aplikasi. Setiap penerap yang
   /// terdaftar dipanggil di thread UI setiap kali tema aktif diterapkan - saat startup dan setiap kali
   /// pengguna mengganti tema - sesudah engine selesai menerapkan warnanya sendiri.
   /// </remarks>
   public interface IThemeApplier
   {
      /// <summary>
      /// Menerapkan tema yang sekarang aktif ke pustaka kontrol yang dilayani penerap ini.
      /// </summary>
      /// <param name="theme">Tema aktif; <see cref="ThemeBase.Variant"/> menyebut modenya.</param>
      void Apply(ThemeBase theme);
   }
}
