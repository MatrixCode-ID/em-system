namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Seberapa jauh sebuah aturan kata sandi berlaku. Mengikuti konvensi enum di repo ini: nilai
   /// negatif berarti tidak dipakai, nol ke atas berarti aktif.
   /// </summary>
   public enum PasswordRuleLevel
   {
      /// <summary>
      /// Aturan tidak dipakai sama sekali: tidak tampil di daftar aturan dan tidak ikut dihitung
      /// pengukur kekuatan sandi.
      /// </summary>
      Off = -1,

      /// <summary>
      /// Aturan ditampilkan dan ikut dihitung pengukur kekuatan sandi, tapi tidak menahan tombol
      /// simpan - sandi yang tidak memenuhinya tetap boleh dipakai. Ini yang membuat daftar aturan
      /// berfungsi sebagai saran, bukan penghalang.
      /// </summary>
      Advisory = 0,

      /// <summary>
      /// Aturan wajib dipenuhi: selama belum terpenuhi, sandi tidak bisa disimpan.
      /// </summary>
      Required = 1
   }

   /// <summary>
   /// Aturan kata sandi yang berlaku di aplikasi - panjang minimal dan aturan jenis karakter,
   /// masing-masing dengan tingkat berlakunya sendiri. Disetel lewat
   /// <see cref="EmAppBuilder.UsePasswordPolicy"/>; aplikasi yang tidak memanggilnya memakai nilai
   /// bawaan setiap property di bawah, yaitu aturan yang sama dengan sebelum objek ini ada.
   /// <para>
   /// Satu hal tidak bisa dimatikan lewat objek ini: kedua kotak sandi harus sama dan tidak boleh
   /// kosong. Itu bukan aturan kekuatan sandi melainkan syarat agar yang tersimpan memang yang
   /// diketik, jadi tetap berlaku walau <see cref="Disable"/> dipanggil.
   /// </para>
   /// </summary>
   public sealed class PasswordPolicy
   {
      /// <summary>
      /// Panjang minimal kata sandi. Hanya berarti kalau <see cref="MinLengthRule"/> tidak
      /// <see cref="PasswordRuleLevel.Off"/>.
      /// </summary>
      public int MinLength { get; set; } = 12;

      /// <summary>Tingkat berlakunya aturan panjang minimal.</summary>
      public PasswordRuleLevel MinLengthRule { get; set; } = PasswordRuleLevel.Required;

      /// <summary>Tingkat berlakunya aturan "memuat huruf besar dan huruf kecil sekaligus".</summary>
      public PasswordRuleLevel MixedCaseRule { get; set; } = PasswordRuleLevel.Advisory;

      /// <summary>Tingkat berlakunya aturan "memuat setidaknya satu angka".</summary>
      public PasswordRuleLevel DigitRule { get; set; } = PasswordRuleLevel.Advisory;

      /// <summary>Tingkat berlakunya aturan "memuat setidaknya satu simbol".</summary>
      public PasswordRuleLevel SymbolRule { get; set; } = PasswordRuleLevel.Advisory;

      /// <summary>
      /// Mematikan seluruh aturan kekuatan sandi sekaligus: tidak ada daftar aturan, tidak ada
      /// pengukur, dan tidak ada yang menahan tombol simpan selain keharusan kedua kotak sandi sama
      /// dan terisi. Disediakan supaya mematikan semuanya tidak perlu menyebut satu per satu, dan
      /// tetap ikut kalau nanti ada aturan baru.
      /// </summary>
      /// <example>
      /// <code>
      /// builder.UsePasswordPolicy(opt => opt.Disable());
      /// </code>
      /// </example>
      public void Disable() {
         MinLength = 0;
         MinLengthRule = PasswordRuleLevel.Off;
         MixedCaseRule = PasswordRuleLevel.Off;
         DigitRule = PasswordRuleLevel.Off;
         SymbolRule = PasswordRuleLevel.Off;
      }

      /// <summary>
      /// Apakah aturan panjang minimal ditampilkan. Panjang minimal nol tidak menyaring apa pun, jadi
      /// dianggap mati walau tingkatnya tidak <see cref="PasswordRuleLevel.Off"/>.
      /// </summary>
      public bool IsMinLengthShown => MinLengthRule != PasswordRuleLevel.Off && MinLength > 0;

      /// <summary>Apakah aturan huruf besar-kecil ditampilkan.</summary>
      public bool IsMixedCaseShown => MixedCaseRule != PasswordRuleLevel.Off;

      /// <summary>Apakah aturan angka ditampilkan.</summary>
      public bool IsDigitShown => DigitRule != PasswordRuleLevel.Off;

      /// <summary>Apakah aturan simbol ditampilkan.</summary>
      public bool IsSymbolShown => SymbolRule != PasswordRuleLevel.Off;

      /// <summary>
      /// Jumlah aturan yang ditampilkan, 0 sampai 4. Nol berarti daftar aturan dan pengukur kekuatan
      /// sandi tidak punya apa pun untuk digambar, jadi keduanya disembunyikan.
      /// </summary>
      public int ShownRuleCount =>
         (IsMinLengthShown ? 1 : 0) + (IsMixedCaseShown ? 1 : 0)
         + (IsDigitShown ? 1 : 0) + (IsSymbolShown ? 1 : 0);

      /// <summary>Apakah <paramref name="password"/> sudah memenuhi panjang minimal.</summary>
      public bool HasMinLength(string password) => password.Length >= MinLength;

      /// <summary>Apakah <paramref name="password"/> memuat huruf besar dan huruf kecil sekaligus.</summary>
      public static bool HasMixedCase(string password) =>
         password.Any(char.IsUpper) && password.Any(char.IsLower);

      /// <summary>Apakah <paramref name="password"/> memuat setidaknya satu angka.</summary>
      public static bool HasDigit(string password) => password.Any(char.IsDigit);

      /// <summary>Apakah <paramref name="password"/> memuat setidaknya satu tanda baca atau simbol.</summary>
      public static bool HasSymbol(string password) => password.Any(c => !char.IsLetterOrDigit(c));

      /// <summary>
      /// Jumlah aturan yang ditampilkan dan sudah dipenuhi <paramref name="password"/>. Dipakai
      /// sebagai pembilang pengukur kekuatan sandi, dengan <see cref="ShownRuleCount"/> sebagai
      /// penyebutnya.
      /// </summary>
      /// <param name="password">Kata sandi yang sedang diketik.</param>
      public int CountMetShownRules(string password) {
         var met = 0;
         if (IsMinLengthShown && HasMinLength(password)) met++;
         if (IsMixedCaseShown && HasMixedCase(password)) met++;
         if (IsDigitShown && HasDigit(password)) met++;
         if (IsSymbolShown && HasSymbol(password)) met++;
         return met;
      }

      /// <summary>
      /// Apakah <paramref name="password"/> memenuhi setiap aturan yang berstatus
      /// <see cref="PasswordRuleLevel.Required"/>. Aturan yang cuma saran tidak diperiksa di sini -
      /// itulah bedanya dengan daftar aturan yang tampil di layar.
      /// </summary>
      /// <param name="password">Kata sandi yang sedang diketik.</param>
      public bool IsSatisfiedBy(string password) {
         if (IsMinLengthShown && MinLengthRule == PasswordRuleLevel.Required && !HasMinLength(password)) return false;
         if (MixedCaseRule == PasswordRuleLevel.Required && !HasMixedCase(password)) return false;
         if (DigitRule == PasswordRuleLevel.Required && !HasDigit(password)) return false;
         if (SymbolRule == PasswordRuleLevel.Required && !HasSymbol(password)) return false;
         return true;
      }
   }
}
