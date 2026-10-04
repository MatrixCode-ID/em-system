using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Builder konfigurasi aplikasi WPF, diteruskan ke callback pada <c>EmApp.BuildApp</c> sehingga
   /// setiap module bisa mendaftarkan service dan navigasi masing-masing.
   /// </summary>
   public partial class EmAppBuilder
   {
      internal EmAppBuilder() { }

      /// <summary>
      /// Nama aplikasi, dipakai sebagai judul window utama dan nama subkey Registry.
      /// Wajib di-set oleh aplikasi (bukan module) saat konfigurasi awal.
      /// </summary>
      public string? ApplicationName { get; set; }

      /// <summary>
      /// Pengaturan tampilan brand yang berlaku saat ini (logo, teks layar login, dan tema). Diisi
      /// lewat <see cref="ApplyBranding"/>; kalau aplikasi tidak pernah memanggilnya, tetap berupa
      /// <see cref="BrandingInfo"/> kosong sehingga seluruh propertinya jatuh ke nilai bawaan generik.
      /// </summary>
      internal BrandingInfo? Branding { get; private set; }

      /// <summary>
      /// Menyetel tampilan brand aplikasi: logo, teks-teks layar login, dan tema terang/gelap.
      /// Panggil sekali saat konfigurasi awal aplikasi (bukan module); property <paramref name="info"/>
      /// yang dibiarkan kosong jatuh ke nilai bawaan generik, lihat masing-masing property di
      /// <see cref="BrandingInfo"/>.
      /// </summary>
      /// <param name="info">Pengaturan brand yang akan dipakai.</param>
      public void ApplyBranding(BrandingInfo info) {
         Branding = info;
      }

      /// <summary>
      /// Mendaftarkan penerap tema tambahan untuk pustaka kontrol pihak ketiga yang punya sistem tema
      /// sendiri. Penerapnya dipanggil setiap kali tema aktif diterapkan - lihat
      /// <see cref="IThemeApplier"/>. Aman dipanggil berulang kali untuk tipe yang sama: yang terdaftar
      /// tetap satu.
      /// </summary>
      /// <typeparam name="T">Tipe penerap tema.</typeparam>
      public void AddThemeApplier<T>() where T : class, IThemeApplier {
         Services.TryAddEnumerable(ServiceDescriptor.Singleton<IThemeApplier, T>());
      }

      /// <summary>
      /// Mendaftarkan pemuat logo untuk format gambar yang tidak dikenal core (mis. <c>.svg</c>) -
      /// lihat <see cref="ILogoImageLoader"/>. Aman dipanggil berulang kali untuk tipe yang sama: yang
      /// terdaftar tetap satu.
      /// </summary>
      /// <typeparam name="T">Tipe pemuat logo.</typeparam>
      public void AddLogoImageLoader<T>() where T : class, ILogoImageLoader {
         Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILogoImageLoader, T>());
      }

      /// <summary>
      /// Aturan kata sandi yang disetel aplikasi lewat <see cref="UsePasswordPolicy"/>, atau
      /// <c>null</c> kalau tidak pernah disetel - dan kalau begitu yang dipakai adalah nilai bawaan
      /// setiap property <see cref="PasswordPolicy"/>.
      /// </summary>
      internal PasswordPolicy? PasswordPolicy { get; private set; }

      /// <summary>
      /// Menyetel aturan kata sandi aplikasi. <paramref name="options"/> menerima objek aturan yang
      /// sudah berisi nilai bawaan, jadi callback-nya cukup menyebut yang berbeda saja - termasuk
      /// mematikan semuanya lewat <see cref="PasswordPolicy.Disable"/>. Panggil sekali saat
      /// konfigurasi awal aplikasi (bukan module).
      /// </summary>
      /// <param name="options">Callback yang mengubah aturan sesuai kebutuhan aplikasi.</param>
      /// <example>
      /// <code>
      /// builder.UsePasswordPolicy(opt => {
      ///    opt.MinLength = 8;
      ///    opt.MixedCaseRule = PasswordRuleLevel.Required;
      ///    opt.SymbolRule = PasswordRuleLevel.Off;
      /// });
      /// </code>
      /// </example>
      public void UsePasswordPolicy(Action<PasswordPolicy> options) {
         ArgumentNullException.ThrowIfNull(options);

         var policy = new PasswordPolicy();
         options(policy);
         PasswordPolicy = policy;
      }

      internal IServiceCollection Services { get; init; } = null!;

      /// <summary>
      /// Public key RSA server untuk mode debug, dipakai memverifikasi koneksi debug ke server.
      /// </summary>
      public string ServerRsaPublicKey { get; internal set; } = null!;

      /// <summary>
      /// Mendaftarkan service module ke DI container sebagai scoped service.
      /// </summary>
      /// <typeparam name="T1">Tipe interface service, harus mengimplementasikan <see cref="IServices"/>.</typeparam>
      /// <typeparam name="T2">Tipe implementasi konkret dari <typeparamref name="T1"/>.</typeparam>
      public void AddServices<T1, T2>() where T1 : IServices {
         Services.AddScoped(typeof(T1), typeof(T2));
      }
      
      internal DebugBuilder? DebugBuilder { get; set; }

      internal List<Navigation> Navigations { get; } = [];
      internal Navigation? CustomHomeNavigation { get; set; }

      /// <summary>
      /// Mendaftarkan <paramref name="navigation"/> terikat module <typeparamref name="T"/>: user
      /// boleh membukanya kalau punya claim apa pun di module itu. <typeparamref name="T"/> wajib
      /// bertanda <c>[Module]</c> - lihat <see cref="ClaimAction.Create{T}"/>.
      /// </summary>
      /// <typeparam name="T">Class service pemilik navigasi ini.</typeparam>
      /// <param name="navigation">Navigasi yang didaftarkan.</param>
      public void AddNavigation<T>(Navigation navigation) where T : IServices {
         navigation.ModuleName = ModuleAttribute.ResolveName(typeof(T));
         Navigations.Add(navigation);
      }

      /// <summary>
      /// Mendaftarkan <paramref name="navigation"/> terikat claim tertentu: user boleh membukanya
      /// hanya kalau punya claim <paramref name="claimName"/> persis, pada module <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">Class service pemilik navigasi ini.</typeparam>
      /// <param name="navigation">Navigasi yang didaftarkan.</param>
      /// <param name="claimName">Nama claim yang dipersyaratkan, tanpa nama module di depannya.</param>
      public void AddNavigation<T>(Navigation navigation, string claimName) where T : IServices {
         var claim = ClaimAction.Create<T>(claimName);
         navigation.ModuleName = claim.ModuleName;
         navigation.RequiredClaim = claim;
         Navigations.Add(navigation);
      }

      /// <summary>
      /// Mendaftarkan <paramref name="navigation"/> tanpa ikatan module atau claim sama sekali:
      /// terbuka untuk siapa pun yang sudah masuk.
      /// </summary>
      /// <param name="navigation">Navigasi yang didaftarkan.</param>
      public void AddNavigation(Navigation navigation) {
         Navigations.Add(navigation);
      }

      internal ApplicationLayout AppLayout { get; set; } = ApplicationLayout.MultiTab;

      /// <summary>
      /// Memakai layout satu halaman: window utama menampilkan satu layar pada satu waktu, dengan
      /// home, tombol Back/Forward, dan detach. Kalau tidak pernah dipanggil, aplikasi memakai layout
      /// multi-tab, di mana setiap layar yang dibuka menjadi tab sendiri di window utama.
      /// </summary>
      public void UseSinglePageLayout() {
         AppLayout = ApplicationLayout.SinglePage;
      }

      /// <summary>
      /// Menyalakan animasi perpindahan layar: layar baru bergeser masuk sambil memudar, dari kanan
      /// untuk Forward dan layar yang baru dibuka, dari kiri untuk Back dan Home. Hanya berlaku di
      /// layout satu halaman (lihat <see cref="UseSinglePageLayout"/>); di layout multi-tab setelan ini
      /// diabaikan. Bawaannya <c>false</c>.
      /// </summary>
      public bool EnableAnimation { get; set; }

      /// <summary>
      /// Lama animasi perpindahan layar saat <see cref="EnableAnimation"/> menyala. Bawaannya 200 ms;
      /// usahakan tetap pendek (sekitar 150-250 ms) supaya Back/Forward yang sering dipakai tidak terasa
      /// menghambat. <see cref="TimeSpan.Zero"/> sama dengan mematikan animasi.
      /// </summary>
      /// <exception cref="ArgumentOutOfRangeException">Nilainya negatif.</exception>
      public TimeSpan TransitionTime {
         get;
         set {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
         }
      } = TimeSpan.FromMilliseconds(200);

      /// <summary>
      /// Mengganti layar home bawaan dengan milik aplikasi sendiri. Kalau tidak pernah dipanggil,
      /// yang dipakai adalah layar home bawaan <c>Em.Ui.Wpf.Core</c>. Di layout multi-tab tidak ada
      /// home, jadi pemanggilan ini tidak berpengaruh di sana.
      /// </summary>
      /// <param name="customHomeNavigation">Navigasi yang dijadikan home.</param>
      public void UseHomeNavigation(Navigation customHomeNavigation) {
         CustomHomeNavigation = customHomeNavigation;
      }
   }
}