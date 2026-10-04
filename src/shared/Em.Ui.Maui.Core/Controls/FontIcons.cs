namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// Kumpulan ikon Font Awesome dalam bentuk karakter glyph, siap dipasang ke
   /// <see cref="FontIcon.Glyph"/>.
   /// </summary>
   /// <remarks>
   /// Client ini memakai Font Awesome 7 Free, yang berkasnya ikut dibawa library ini dan didaftarkan
   /// sendiri olehnya saat aplikasi dibangun. Nilai di sini ditulis sebagai codepoint Unicode, bukan
   /// nama enum dari sebuah paket: paket Font Awesome yang dipakai client WPF tidak punya target
   /// Android, dan bagian yang netral framework pun tidak memuat codepoint-nya. Codepoint ikon lama
   /// stabil antar versi mayor, jadi tabel ini tetap cocok dengan Font Awesome 6 di client WPF.
   /// </remarks>
   public static class FontIcons
   {
      /// <summary>Nama alias font yang memuat semua glyph di class ini.</summary>
      public const string FontFamily = "FontAwesomeSolid";

      /// <summary>Panjang sisi bawaan sebuah ikon dalam satuan perangkat.</summary>
      public const double DefaultSize = 24d;

      /// <summary>Panah ke kiri, kembali ke layar sebelumnya.</summary>
      public const string ArrowLeft = "";

      /// <summary>Panah ke kanan, maju ke layar berikutnya.</summary>
      public const string ArrowRight = "";

      /// <summary>Panah melingkar searah jarum jam, memuat ulang isi layar.</summary>
      public const string RotateRight = "";

      /// <summary>Rumah, layar utama.</summary>
      public const string House = "";

      /// <summary>Kaca pembesar, penanda kotak pencarian.</summary>
      public const string MagnifyingGlass = "";

      /// <summary>Panah ke bawah, penanda grup yang sedang terbuka.</summary>
      public const string ChevronDown = "";

      /// <summary>Panah ke kanan, penanda grup yang sedang tertutup.</summary>
      public const string ChevronRight = "";

      /// <summary>Roda gigi, penanda pengaturan.</summary>
      public const string Gear = "";

      /// <summary>Steker, penanda sambungan ke server.</summary>
      public const string Plug = "";

      /// <summary>Panah masuk pintu, memulai sesi.</summary>
      public const string RightToBracket = "";

      /// <summary>Panah keluar pintu, mengakhiri sesi.</summary>
      public const string RightFromBracket = "";

      /// <summary>Kunci, penanda kata sandi.</summary>
      public const string Key = "\uf084";

      /// <summary>Centang, penanda syarat yang sudah terpenuhi.</summary>
      public const string Check = "\uf00c";

      /// <summary>Amplop, penanda alamat surel.</summary>
      public const string Envelope = "";

      /// <summary>Tanda silang, menutup panel.</summary>
      public const string Xmark = "";

      /// <summary>Dua bilah bertumpuk, penanda server.</summary>
      public const string Server = "";

      /// <summary>Lingkaran separuh terisi, tombol ganti tema.</summary>
      /// <remarks>
      /// Satu glyph untuk kedua arah, sama seperti client desktop: tombolnya berarti "ganti tema",
      /// bukan "tema yang sedang aktif", jadi gambarnya tidak perlu ikut berganti. Yang menyebut tema
      /// tujuannya adalah teks di sampingnya, di tempat yang memang punya teks.
      /// </remarks>
      public const string CircleHalfStroke = "\uf042";

      /// <summary>Bulan sabit, penanda tema gelap.</summary>
      public const string Moon = "";

      /// <summary>Matahari, penanda tema terang.</summary>
      public const string Sun = "";

      /// <summary>Siluet orang, penanda pengguna.</summary>
      public const string User = "";

      /// <summary>Gembok terbuka, penanda pengguna yang belum masuk.</summary>
      public const string LockOpen = "";

      /// <summary>Empat persegi besar, penanda kumpulan module.</summary>
      public const string TableCellsLarge = "";

      /// <summary>Jendela, penanda sebuah layar module.</summary>
      public const string WindowMaximize = "";
   }
}
