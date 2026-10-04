namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Pengaturan tampilan brand aplikasi: logo, teks-teks di panel branding layar login, dan tema
   /// terang/gelap yang dipakai seluruh aplikasi. Dipasang lewat <c>EmAppBuilder.ApplyBranding</c>;
   /// semua propertinya opsional dan jatuh ke nilai bawaan generik kalau dibiarkan kosong.
   /// </summary>
   /// <remarks>
   /// Kelas ini hanya data dan sama untuk semua client. Cara memuat logo dan menerjemahkan warna tema
   /// menjadi urusan core masing-masing platform.
   /// </remarks>
   public sealed class BrandingInfo
   {
      // Nilai bawaan generik dipakai kalau properti terkait di bawah dibiarkan kosong. Sengaja tidak
      // menyebut nama perusahaan tertentu supaya masuk akal untuk aplikasi mana pun yang belum
      // memanggil EmAppBuilder.ApplyBranding sama sekali.
      private const string DefaultTitle = "Your Company";
      private const string DefaultTagline = "Enterprise Application";

      private const string DefaultDescriptionText =
         "One workspace for your operations, assets, and people — connected end to end.";

      private ThemeBase _lightTheme = new LightTheme();
      private ThemeBase _darkTheme = new DarkTheme();

      /// <summary>
      /// Lokasi logo aplikasi. Cara menafsirkannya ditentukan core platform yang menampilkannya:
      /// <list type="bullet">
      /// <item>WPF: pack URI ke resource milik project aplikasi, mis.
      /// <c>"pack://application:,,,/Em.Ui.Wpf;component/Logo.png"</c>. Core WPF hanya memuat bitmap
      /// (<c>.png</c>, <c>.jpg</c>, <c>.ico</c>, <c>.bmp</c>); format lain (mis. <c>.svg</c>) butuh
      /// pemuat logo tambahan yang dipasang aplikasi.</item>
      /// <item>MAUI: nama file gambar di <c>Resources/Images</c> milik project aplikasi, huruf kecil,
      /// dengan akhiran <c>.png</c> juga untuk file <c>.svg</c> (MAUI mengubahnya jadi bitmap saat
      /// build).</item>
      /// </list>
      /// Kalau dibiarkan kosong atau gagal dimuat, dipakai logo bawaan core platform.
      /// </summary>
      public string? LogoSource { get; set; }

      /// <summary>Pilihan layar login WPF; bawaannya Material. Belum dipakai MAUI.</summary>
      public LoginStyle LoginStyle { get; set; } = LoginStyle.Material;

      /// <summary>
      /// Background terang, hanya untuk WPF dengan LoginStyle.Material; diabaikan Classic dan MAUI.
      /// Tafsiran sama dengan LogoSource: pack URI atau URI absolut, bitmap kecuali ada ILogoImageLoader.
      /// Disarankan lebar sekitar 1920 px. Jika hanya satu gambar diisi, dipakai di kedua mode;
      /// mode gelap meredupkan gambar terang yang dipinjam. Gagal dimuat jatuh ke mode lain lalu gradien.
      /// </summary>
      public string? LightLoginBackground { get; set; }

      /// <summary>
      /// Background gelap, hanya untuk WPF dengan LoginStyle.Material; diabaikan Classic dan MAUI.
      /// Tafsiran sama dengan LogoSource: pack URI atau URI absolut, bitmap kecuali ada ILogoImageLoader.
      /// Disarankan lebar sekitar 1920 px. Jika hanya satu gambar diisi, dipakai di kedua mode;
      /// mode gelap meredupkan gambar terang yang dipinjam. Gagal dimuat jatuh ke mode lain lalu gradien.
      /// </summary>
      public string? DarkLoginBackground { get; set; }

      /// <summary>
      /// Lokasi ikon aplikasi untuk window (ikon di taskbar dan baris judul) dan logo kecil di baris judul
      /// window utama. Hanya dipakai WPF: pack URI ke berkas <c>.ico</c> (atau bitmap) milik project
      /// aplikasi, mis. <c>"pack://application:,,,/Em.Ui.Wpf;component/Logo.ico"</c>. Ikon berkas
      /// <c>.exe</c> di Explorer tidak ikut berubah; itu diatur <c>ApplicationIcon</c> di project
      /// aplikasi. Kalau dibiarkan kosong atau gagal dimuat, dipakai ikon bawaan core.
      /// </summary>
      public string? IconSource { get; set; }

      /// <summary>
      /// Judul brand di panel login (mis. <c>"EM"</c>), ditampilkan besar di bawah logo. Kalau
      /// dibiarkan kosong, dipakai <c>"Your Company"</c>.
      /// </summary>
      public string? Title { get; set; }

      /// <summary>
      /// Sub-judul brand di panel login (mis. <c>"Corporate Service Management"</c>), ditampilkan tepat
      /// di bawah <see cref="Title"/>. Kalau dibiarkan kosong, dipakai <c>"Enterprise Application"</c>.
      /// </summary>
      public string? Tagline { get; set; }

      /// <summary>
      /// Paragraf deskripsi singkat di panel login, di bawah garis pemisah. Kalau dibiarkan kosong,
      /// dipakai kalimat generik yang tidak menyebut nama perusahaan tertentu.
      /// </summary>
      public string? Description { get; set; }

      /// <summary>
      /// Teks hak cipta di footer panel login (mis. <c>"© 2026 EM. All rights reserved."</c>). Kalau
      /// dibiarkan kosong, dipakai <c>"© {tahun berjalan} Your Company. All rights reserved."</c>.
      /// </summary>
      public string? Copyright { get; set; }

      /// <summary>
      /// Tema untuk mode terang. Bawaannya <see cref="Shared.LightTheme"/> (palet standar Em).
      /// </summary>
      /// <exception cref="ArgumentException">Tema yang dipasang bukan tema mode terang.</exception>
      public ThemeBase LightTheme {
         get => _lightTheme;
         set => _lightTheme = Validate(value, ThemeVariant.Light);
      }

      /// <summary>
      /// Tema untuk mode gelap. Bawaannya <see cref="Shared.DarkTheme"/> (palet standar Em).
      /// </summary>
      /// <exception cref="ArgumentException">Tema yang dipasang bukan tema mode gelap.</exception>
      public ThemeBase DarkTheme {
         get => _darkTheme;
         set => _darkTheme = Validate(value, ThemeVariant.Dark);
      }

      /// <summary>
      /// Judul brand yang sebenarnya ditampilkan di panel login: <see cref="Title"/> kalau diisi,
      /// atau <c>"Your Company"</c> kalau tidak.
      /// </summary>
      public string DisplayTitle => Title ?? DefaultTitle;

      /// <summary>
      /// Sub-judul brand yang sebenarnya ditampilkan: <see cref="Tagline"/> kalau diisi, atau
      /// <c>"Enterprise Application"</c> kalau tidak.
      /// </summary>
      public string DisplayTagline => Tagline ?? DefaultTagline;

      /// <summary>
      /// Paragraf deskripsi yang sebenarnya ditampilkan: <see cref="Description"/> kalau diisi, atau
      /// kalimat generik bawaan kalau tidak.
      /// </summary>
      public string DisplayDescription => Description ?? DefaultDescriptionText;

      /// <summary>
      /// Teks hak cipta yang sebenarnya ditampilkan: <see cref="Copyright"/> kalau diisi, atau
      /// <c>"© {tahun berjalan} Your Company. All rights reserved."</c> kalau tidak.
      /// </summary>
      public string DisplayCopyright => Copyright ?? $"© {DateTime.Now.Year} {DefaultTitle}. All rights reserved.";

      /// <summary>
      /// Tema untuk mode yang diminta: <see cref="LightTheme"/> atau <see cref="DarkTheme"/>.
      /// </summary>
      public ThemeBase GetTheme(ThemeVariant variant) => variant == ThemeVariant.Light ? LightTheme : DarkTheme;

      private static ThemeBase Validate(ThemeBase theme, ThemeVariant expected) {
         ArgumentNullException.ThrowIfNull(theme);
         if (theme.Variant != expected)
            throw new ArgumentException($"A {theme.Variant} theme cannot be used as the {expected} theme.", nameof(theme));
         return theme;
      }
   }
}
