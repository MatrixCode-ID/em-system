using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Builder konfigurasi aplikasi MAUI, diteruskan ke callback pada <c>EmApp.BuildApp</c> sehingga
   /// setiap module bisa mendaftarkan service dan navigasi masing-masing.
   /// </summary>
   /// <remarks>
   /// Bedanya dengan builder sisi WPF: di sini tidak ada pilihan layout sama sekali. Aplikasi MAUI
   /// hanya mengenal satu layar pada satu waktu (SPA), jadi tidak ada <c>UseSinglePageLayout</c> untuk
   /// dipanggil dan tidak ada pendaftaran main control ber-tab.
   /// </remarks>
   public class EmAppBuilder
   {
      internal EmAppBuilder() { }

      /// <summary>
      /// Nama aplikasi, dipakai sebagai judul halaman utama dan sebagai awalan kunci penyimpanan
      /// pengaturan aplikasi. Wajib di-set oleh aplikasi (bukan module) saat konfigurasi awal.
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

      /// <summary>
      /// Daftar navigasi yang didaftarkan module lewat <see cref="AddNavigation{T}(Navigation)"/> dan
      /// pasangannya. Setiap entri jadi satu layar yang bisa dibuka lewat namanya, dan yang
      /// <c>IsMenuVisible</c>-nya menyala ikut tampil di menu home.
      /// </summary>
      internal List<Navigation> Navigations { get; } = [];

      internal Navigation? CustomHomeNavigation { get; private set; }

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

      /// <summary>
      /// Mengganti layar home bawaan dengan milik aplikasi sendiri. Kalau tidak pernah dipanggil,
      /// yang dipakai adalah layar home bawaan <c>Em.Ui.Maui.Core</c>.
      /// </summary>
      /// <param name="customHomeNavigation">Navigasi yang dijadikan home.</param>
      public void UseHomeNavigation(Navigation customHomeNavigation) {
         CustomHomeNavigation = customHomeNavigation;
      }
   }
}
