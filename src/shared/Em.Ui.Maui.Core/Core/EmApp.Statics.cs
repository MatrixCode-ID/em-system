using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Em.Api.Core;
using Em.Api.Core.Models;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Controls;
using Em.Ui.Maui.Navigations;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Objek aplikasi utama untuk sisi MAUI (analog dengan <c>EmApp</c> di backend dan di client
   /// desktop), menampung DI container, konfigurasi navigasi/module, koneksi API tersimpan, dan tema
   /// aplikasi. Dibuat sekali lewat <see cref="BuildApp"/> saat startup aplikasi.
   /// </summary>
   /// <remarks>
   /// Hanya mengenal satu layar pada satu waktu. Tidak ada layout bertab di sini - jalur navigasinya
   /// satu garis lurus, persis seperti layout single page di client desktop.
   /// </remarks>
   public partial class EmApp
   {
      #region Fields and Constants


      /// <summary>
      /// Nama navigasi layar login, didaftarkan aplikasi sendiri lewat
      /// <see cref="InitInternalNavigation"/>. Ada sebagai konstanta karena bukan cuma pendaftarnya
      /// yang menyebut nama ini: <see cref="ShowLoginScreen"/> memakainya untuk membuka layar login,
      /// dan menu akun memakainya saat user keluar - ketiganya harus menunjuk navigasi yang sama persis.
      /// </summary>
      public const string LogonNavigationName = "admin.logon";

      /// <summary>Nama navigasi layar ganti kata sandi milik pengguna yang sedang masuk.</summary>
      public const string ChangePasswordNavigationName = "admin.changepassword";

      /// <summary>Nama navigasi layar home bawaan.</summary>
      public const string HomeNavigationName = "Home";

      #endregion

      /// <summary>
      /// Membangun instance <see cref="EmApp"/>: mendaftarkan service internal (hashing, penyimpan
      /// sesi, dsb.), menjalankan callback <paramref name="builder"/> agar module bisa mendaftarkan
      /// service dan navigasi masing-masing, lalu membangun <see cref="ServiceProvider"/> dari DI container.
      /// </summary>
      /// <param name="args">Argumen command-line aplikasi.</param>
      /// <param name="builder">Callback konfigurasi, dipakai module untuk memanggil <c>AddServices</c>/<c>AddNavigation</c>.</param>
      /// <returns>Instance <see cref="EmApp"/> yang siap dijalankan lewat <see cref="Run{TApp}"/>.</returns>
      public static EmApp BuildApp(string[] args, Action<EmAppBuilder> builder) {
         var app = new EmApp(args);

         InitInternalServices(app);
         InitBuilder(app, builder);

         // Ditutup di sini, bukan di ujung InitBuilder: dari luar, "selesai dibangun" berarti selesai
         // seluruh BuildApp, dan satu-satunya tempat yang boleh menentukan saat itu adalah baris ini.
         app.SealInternalClaims();

         app._serviceProvider = app.Services.BuildServiceProvider();
         return app;
      }

      /// <summary>
      /// Menyiapkan aplikasi MAUI-nya: mendaftarkan <typeparamref name="TApp"/> sebagai kelas aplikasi,
      /// menitipkan objek ini ke container MAUI supaya bisa diminta lewat constructor, lalu
      /// mengembalikan <see cref="MauiApp"/> yang tinggal dijalankan platform.
      /// </summary>
      /// <typeparam name="TApp">Kelas <c>Application</c> milik aplikasi.</typeparam>
      /// <param name="configure">
      /// Kesempatan aplikasi menambahkan konfigurasi MAUI-nya sendiri - font, logging, handler - sebelum
      /// dibangun. Boleh dikosongkan.
      /// </param>
      /// <remarks>
      /// Inilah padanan MAUI dari <c>Run</c> di client desktop. Bedanya, yang di sana blocking sampai
      /// aplikasi ditutup, sementara di sini siklus hidupnya dipegang platform - layar pertama baru
      /// dipasang nanti lewat <see cref="CreateRootPage"/>.
      /// </remarks>
      public MauiApp Run<TApp>(Action<MauiAppBuilder>? configure = null) where TApp : class, IApplication {
         var mauiBuilder = MauiApp.CreateBuilder();
         mauiBuilder.UseMauiApp<TApp>();

         // Font ikon didaftarkan di sini, bukan di aplikasi yang memakainya: berkas font-nya ikut
         // dibawa library ini, jadi library ini pula yang bertanggung jawab memperkenalkannya. Kalau
         // pendaftarannya diserahkan ke host, satu baris yang terlupa membuat seluruh ikon muncul
         // sebagai kotak kosong tanpa pesan kesalahan apa pun.
         mauiBuilder.ConfigureFonts(fonts =>
            fonts.AddFont("Font Awesome 7 Free-Solid-900.otf", FontIcons.FontFamily));

         // Container MAUI hanya perlu tahu satu hal: objek aplikasi ini. Pendaftaran service module
         // seluruhnya tinggal di container milik EmApp sendiri (lihat BuildApp), sama seperti di
         // client desktop - jadi kelas Application cukup meminta EmApp lewat constructor-nya.
         mauiBuilder.Services.AddSingleton(this);
         mauiBuilder.Services.AddSingleton<IEmApp>(this);
         mauiBuilder.Services.AddSingleton<IEmAppUi>(this);

         configure?.Invoke(mauiBuilder);
         return mauiBuilder.Build();
      }

      /// <summary>
      /// Mendaftarkan service inti bawaan aplikasi ke DI container: hashing, instance
      /// <see cref="EmApp"/> itu sendiri (lewat ketiga kontraknya), penyimpan sesi, dan service data
      /// inti yang dipakai lintas module - saat ini data kontak dan data kredensial pengguna.
      /// </summary>
      private static void InitInternalServices(EmApp app) {
         app.Services.AddSingleton<IStringHasher, Argon2Hashing>();
         app.Services.AddSingleton(app);
         app.Services.AddSingleton<IEmApp>(app);
         app.Services.AddSingleton<IEmAppUi>(app);
         app.Services.AddSingleton<INavigationHost>(app);
         app.Services.AddSingleton<IContactServices, ContactService>();
         app.Services.AddSingleton<ICredentialServices, CredentialService>();
         app.Services.AddSingleton<IBusinessTaskServices, BusinessTaskService>();
         app.Services.AddSingleton<IApprovalServices, ApprovalService>();
         app.Services.AddSingleton<ISessionStorage, SecureStorageSessionStorage>();
      }

      /// <summary>
      /// Menjalankan callback <paramref name="builder"/> untuk konfigurasi dari module, lalu memasang
      /// hasilnya ke aplikasi: nama, brand, aturan sandi, navigasi, dan konfigurasi debug.
      /// </summary>
      private static void InitBuilder(EmApp app, Action<EmAppBuilder> builder) {
         var pars = new EmAppBuilder {
            Services = app.Services
         };

         InitInternalNavigation(app);
         InitInternalClaims(app);
         builder(pars);

         app.ApplicationName = pars.ApplicationName ?? "Set ApplicationName to change!";
         // Dibuat sesudah nama aplikasi diketahui, karena nama itulah yang jadi awalan setiap kunci
         // pengaturan - membuatnya lebih awal berarti menulis ke awalan yang salah.
         app.Settings = new AppSettings(app.ApplicationName);

         var home = pars.CustomHomeNavigation ?? new Navigation {
            Name = HomeNavigationName,
            Title = "Home",
            OrderIndex = -1,
            Subtitle = "",
            Description = "",
            BodyType = BodyType.Of<DefaultHomeControl>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false
         };
         // AddNavigation runs before the stack is built because it is what hands the navigation its
         // EmApp - and without it home would never appear in Navigations and could not be resolved
         // by name either.
         app.AddNavigation(home);
         app._mainStack = new NavigationStack(app, home);

         // BrandingInfo itself resolves every unset property to a generic default (its Display*
         // members and the standard light/dark themes), so a plain empty instance is enough here when
         // the application never calls EmAppBuilder.ApplyBranding.
         app.Branding = pars.Branding ?? new BrandingInfo();
         // Handed to the palette before App builds its resources: every style reads its colours once,
         // while it loads, so a value written any later would never reach them.
         Styles.Palette.Branding = app.Branding;
         // Setiap property PasswordPolicy sudah membawa nilai bawaannya sendiri, jadi instance kosong
         // adalah aturan yang berlaku kalau aplikasi tidak pernah memanggil UsePasswordPolicy.
         app.PasswordPolicy = pars.PasswordPolicy ?? new PasswordPolicy();

         pars.Navigations.EachOf(app.AddNavigation);
         InitDebugMode(app, pars);
      }

      private static void InitDebugMode(EmApp app, EmAppBuilder pars) {
         if (pars.DebugBuilder == null || pars.DebugBuilder.Connections.Count <= 0) return;
         app.IsDebugMode = true;
         app.DebugConnections = [.. pars.DebugBuilder.Connections];
         app.DefaultDebugConnection = pars.DebugBuilder.DefaultConnection;

         // Ditandatangani sekali di sini, sesudah callback debug selesai dan sebelum layar pertama muncul,
         // supaya key yang salah ketik ketahuan sekarang - bukan nanti saat setiap request dijawab dengan
         // "action not found" tanpa keterangan apa-apa. Yang diteruskan ke koneksi adalah tokennya, bukan
         // key-nya, jadi jalur request tidak pernah menyentuh kriptografi.
         var debugToken = pars.DebugBuilder.CreateDebugToken();
         app.DebugConnections.EachOf(r => r.DebugToken = debugToken);

         app.SetActiveUser(CreateDebuggerUser(app));
      }

      // Built here rather than alongside the application object, so that an application started
      // without a debug connection never has this account at hand at all: it is an administrator
      // that stands in for signing in, and the only place it is allowed to come from is debug mode.
      // Its row exists nowhere on the server - Defaults.DebuggerUserId is what tells it apart.
      private static User CreateDebuggerUser(EmApp app) =>
         User.Build(app, new vi_User {
            cUserId = Defaults.DebuggerUserId,
            cUserAccount = Defaults.DebuggerUserAccount,
            cContactId = Defaults.DebuggerUserId,
            cUserState = UserState.Active,
            cUserIsAdmin = true,
            ustamp = default,
            datestamp = default,
            json_object = null,
            cContactFullName = "SYSTEM DEBUGGER",
            cContactState = ContactState.Active,
            cContactType = ContactType.Organization,
            cContactNote = "Dummy User for debugging application by developer",
            cAddressName = "N/A",
            cAddressLocation = "N/A",
            cAddressZip = "N/A",
            cCommType = CommunationType.Email,
            cCommState = ContactCommunicationState.ActiveAsUserReference,
            cCommValue = "N/A",
            cCommNote = "N/A"
         });

      // Akun administrator bawaan tidak punya baris pengguna di mana pun - sama seperti akun
      // debugger di atas, dan dibuatkan di sini dengan alasan yang sama: ia berdiri menggantikan
      // seorang pengguna tanpa pernah tersimpan sebagai satu. Bedanya, akun ini benar-benar masuk
      // lewat layar login, jadi ia ada di build apa pun, bukan cuma di mode debug.
      private static User CreateAdminUser(EmApp app) =>
         User.Build(app, new vi_User {
            cUserId = Defaults.AdminUserId,
            cUserAccount = Defaults.AdminUserAccount,
            cContactId = Defaults.AdminUserId,
            cUserState = UserState.Active,
            cUserIsAdmin = true,
            ustamp = default,
            datestamp = default,
            json_object = null,
            cContactFullName = Defaults.AdminUserFullName,
            cContactState = ContactState.Active,
            cContactType = ContactType.Organization,
            cContactNote = "Built-in administrator account; it has no stored record of its own.",
            cAddressName = "N/A",
            cAddressLocation = "N/A",
            cAddressZip = "N/A",
            cCommType = CommunationType.Email,
            cCommState = ContactCommunicationState.ActiveAsUserReference,
            cCommValue = "N/A",
            cCommNote = "N/A"
         });

      // Claim milik layar bawaan client. Masih kosong di sisi MAUI: layar pengelola pengguna dan
      // pengelola role belum ada di sini, dan sebuah claim tanpa layar yang memakainya hanya akan
      // muncul di daftar pemberian hak sebagai baris yang tidak mengerjakan apa-apa.
      private static void InitInternalClaims(EmApp app) {
      }

      private static void InitInternalNavigation(EmApp app) {
         app.AddNavigation(new Navigation {
            Name = LogonNavigationName,
            Title = "Login",
            Subtitle = "",
            OrderIndex = -1,
            Description = "Enter your username and password to proceed.",
            BodyType = BodyType.Of<LoginControl>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            // Nothing on the navigation toolbar applies before anyone has signed in - there is no
            // page to go back to, and the login screen carries its own theme switch.
            IsToolbarVisible = false
         });

         app.AddNavigation(new Navigation {
            Name = ChangePasswordNavigationName,
            Title = "Change Password",
            Subtitle = "",
            OrderIndex = -1,
            Description = "Change the password of the account you are signed in with.",
            BodyType = BodyType.Of<ChangePasswordControl>(),
            Kind = NavigationKind.Editor,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false
         });
      }
   }
}
