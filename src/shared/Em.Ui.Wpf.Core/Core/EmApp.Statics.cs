using System.Buffers.Text;
using System.Collections.ObjectModel;
using System.IO.Packaging;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using FontAwesome6;
using FontAwesome6.Fonts.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Em.Api.Core.Models;
using Em.Api.Core;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Navigations;
using Em.Ui.Wpf.Dialogs;
using Em.Ui.Wpf.Shared;
using Em.Ui.Wpf.Windows;
using Application = System.Windows.Application;

namespace Em.Ui.Wpf.Core
{
   /// <summary>
   /// Objek aplikasi utama untuk sisi WPF (analog dengan <c>EmApp</c> di backend), menampung
   /// DI container, navigasi dan module yang terdaftar, koneksi API tersimpan (di Registry), dan
   /// tema aplikasi. Dibuat sekali lewat <see cref="BuildApp"/> saat startup aplikasi.
   /// </summary>
   public partial class EmApp
   {
      #region Fields and Constants
      /// <summary>
      /// Nama navigasi layar login, didaftarkan aplikasi sendiri lewat
      /// <see cref="InitInternalNavigation"/>. Ada sebagai konstanta karena bukan cuma pendaftarnya
      /// yang menyebut nama ini: window utama memakainya untuk membuka layar login, dan menu akun
      /// memakainya saat user keluar - ketiganya harus menunjuk navigasi yang sama persis.
      /// </summary>
      public const string LogonNavigationName = "admin.logon";

      /// <summary>
      /// Nama navigasi viewer PDF bawaan aplikasi. Viewer ini tidak dijaga claim dan wajib dibuka dengan
      /// <see cref="PdfViewerNavigationPayload"/>; cara yang biasa dipakai adalah <see cref="ViewPdf"/> atau
      /// <see cref="NavigationEntry.ViewPdf"/>, bukan <c>NavigateTo</c> dengan nama ini.
      /// </summary>
      public const string PdfViewerNavigationName = "em.viewer.pdf";

      #endregion

       /// <summary>
      /// Membangun instance <see cref="EmApp"/>: mendaftarkan service internal (hashing, sesi, dsb.),
      /// menjalankan callback <paramref name="builder"/> agar module bisa mendaftarkan service dan
      /// navigasi masing-masing, lalu membangun <see cref="ServiceProvider"/> dari DI container.
      /// </summary>
      /// <param name="args">Argumen command-line aplikasi.</param>
      /// <param name="builder">Callback konfigurasi, dipakai module untuk memanggil <c>AddServices</c>/<c>AddNavigation</c>.</param>
      /// <returns>Instance <see cref="EmApp"/> yang siap dijalankan lewat <see cref="Run"/>.</returns>
      public static EmApp BuildApp(string[] args, Action<EmAppBuilder> builder) {
         // Before anything else: it may hand the process over to the launcher and exit.
         LauncherIntegration.Initialize(args);
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
      /// Mendaftarkan service inti bawaan aplikasi ke DI container: hashing, instance
      /// <see cref="EmApp"/> itu sendiri (lewat ketiga kontraknya), service data inti yang
      /// dipakai lintas module - saat ini data kontak dan data kredensial pengguna.
      /// </summary>
      private static void InitInternalServices(EmApp app) {
         app.Services.AddSingleton<IStringHasher, Argon2Hashing>();
         app.Services.AddSingleton(app);
         app.Services.AddSingleton<IEmApp>(app);
         app.Services.AddSingleton<IEmAppUi>(app);
         app.Services.AddSingleton<INavigationHost>(app);
         app.Services.AddSingleton<IContactServices, ContactService>();
         app.Services.AddSingleton<ICredentialServices, CredentialService>();
         app.Services.AddSingleton<ICdnServices, CdnService>();
         app.Services.AddSingleton<ICtnServices, CtnService>();
         app.Services.AddSingleton<INuPakServices, NuPakService>();
         app.Services.AddSingleton<IRobotServices, RobotService>();
         app.Services.AddSingleton<IBusinessTaskServices, BusinessTaskService>();
         app.Services.AddSingleton<IApprovalServices, ApprovalService>();
         app.Services.AddSingleton<ApprovalAccessCatalog>();
         app.Services.AddSingleton<BusinessTaskTracker>();
         app.Services.AddSingleton<ISessionStorage, RegistrySessionStorage>();
      }

      /// <summary>
      /// Menjalankan callback <paramref name="builder"/> untuk konfigurasi dari module, lalu menyiapkan
      /// stack utama sesuai layout yang dipilih dan mendaftarkan seluruh navigasi yang terkumpul.
      /// </summary>
      private static void InitBuilder(EmApp app, Action<EmAppBuilder> builder) {
         var pars = new EmAppBuilder() {
            Services = app.Services
         };
         // init internal navigation host and stack
         InitInternalNavigation(app, pars);
         InitInternalClaims(app);
         builder(pars);
         app.ApplicationLayout = pars.AppLayout;
         app.NavigationTransitionTime = pars.EnableAnimation && app.ApplicationLayout == ApplicationLayout.SinglePage
            ? pars.TransitionTime
            : TimeSpan.Zero;
         if (app.ApplicationLayout == ApplicationLayout.SinglePage) {
            var home = pars.CustomHomeNavigation ?? new Navigation {
               Name = "Home",
               Title = "Home",
               OrderIndex = -1,
               Subtitle = "",
               Description = "",
               BodyType = BodyType.Of<DefaultHomeControl>(),
               Kind = NavigationKind.Manager,
               EmApp = app,
               RequireParameter = false,
               IsMenuVisible = false,
               IsDetachVisible = false
            };
            // AddNavigation runs before the stack is built because it is what hands the navigation its
            // EmApp - and without it home would never appear in Navigations and could not be resolved
            // by name either.
            app.AddNavigation(home);
            app._mainStack = new NavigationStack(app, home);
         }
         // A multi-tab window has no home: its Apps and Tools menus take that place, and every screen
         // opened becomes a tab of its own on this stack.
         else app._mainStack = new NavigationStack(app, home: null, tabbed: true);
         app.ApplicationName = pars.ApplicationName ?? "Set ApplicationName to change!";
         // BrandingInfo itself resolves every unset property to a generic default (its Display*
         // members and the standard light/dark themes), so a plain empty instance is enough here when
         // the application never calls EmAppBuilder.ApplyBranding.
         app.Branding = pars.Branding ?? new BrandingInfo();
         InitLoginNavigation(app);
         app.EnableFieldAnimation = pars.EnableAnimation;
         // Setiap property PasswordPolicy sudah membawa nilai bawaannya sendiri, jadi instance kosong
         // adalah aturan yang berlaku kalau aplikasi tidak pernah memanggil UsePasswordPolicy.
         app.PasswordPolicy = pars.PasswordPolicy ?? new PasswordPolicy();

         // Didaftarkan di sini, sesudah callback module selesai, supaya isinya sudah lengkap dan
         // tidak berubah lagi. Selalu didaftarkan - juga saat tidak ada satu panel pun - supaya layar
         // approval tidak perlu tahu bedanya.
         app.Services.AddSingleton(pars.ApprovalPanels);

         pars.Navigations.EachOf(app.AddNavigation);
         InitDebugMode(app, pars);
      }

      private static void InitDebugMode(EmApp app, EmAppBuilder pars) {
         if (pars.DebugBuilder == null || pars.DebugBuilder!.Connections.Count <= 0) return;
         app.IsDebugMode = true;
         app.DebugConnections = [.. pars.DebugBuilder!.Connections!];
         app.DefaultDebugConnection = pars.DebugBuilder!.DefaultConnection;

         // Ditandatangani sekali di sini, sesudah callback debug selesai dan sebelum window pertama muncul,
         // supaya key yang salah ketik ketahuan sekarang - bukan nanti saat setiap request dijawab dengan
         // "action not found" tanpa keterangan apa-apa. Yang diteruskan ke koneksi adalah tokennya, bukan
         // key-nya, jadi jalur request tidak pernah menyentuh kriptografi.
         var debugToken = pars.DebugBuilder!.CreateDebugToken();
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

      // Claim milik layar bawaan client. Declared here rather than on the server, because these
      // screens belong to the client and no server module owns them - and read straight out of
      // EmApp.AllClaims, so a screen asks for its own claim the same way a module screen does.
      private const string UserManagerClaim = "Administrative Tools:User Manager Access";
      private const string RoleManagerClaim = "Administrative Tools:Role Manager Access";
      private const string CdnManagerClaim = "Administrative Tools:CDN Manager Access";
      private const string BusinessTaskManagerClaim = "Administrative Tools:Business Task Manager Access";
      private const string ReleaseManagerClaim = "Administrative Tools:Release Manager Access";
      private const string ContainerManagerClaim = "Administrative Tools:" + ICtnServices.CtnClaim;

      private static void InitInternalClaims(EmApp app) {
         app.AddInternalClaim(UserManagerClaim);
         app.AddInternalClaim(RoleManagerClaim);
         app.AddInternalClaim(CdnManagerClaim);
         app.AddInternalClaim(BusinessTaskManagerClaim);
         app.AddInternalClaim(ReleaseManagerClaim);
         app.AddInternalClaim(ContainerManagerClaim);
         app.AddInternalClaim("Administrative Tools:" + INuPakServices.ManagerClaim);
         app.AddInternalClaim("Administrative Tools:" + INuPakServices.SettingsClaim);
      }

      // Binds a built-in navigation to one of the claims above, the same way the claim overload of
      // AddNavigation binds a module navigation - so the home menu and NavigateTo both refuse it for a
      // user without that claim.
      private static Navigation RequireClaim(Navigation navigation, string claimKey) {
         var claim = ClaimAction.FromKey(claimKey);
         navigation.ModuleName = claim.ModuleName;
         navigation.RequiredClaim = claim;
         return navigation;
      }

      private static void InitLoginNavigation(EmApp app) {
         app.AddNavigation(new Navigation {
            Name = LogonNavigationName,
            Title = "Login",
            Subtitle = "",
            OrderIndex = -1,
            Description = "Enter your username and password to proceed.",
            BodyType = app.Branding.LoginStyle == LoginStyle.Classic
               ? BodyType.Of<LoginControlClassic>() : BodyType.Of<LoginControlMaterial>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            // Nothing on the navigation toolbar applies before anyone has signed in - there is no
            // page to go back to, and the login screen carries its own theme switch. Declared here
            // rather than toggled from the body, so this screen looks the same on either layout.
            IsToolbarVisible = false,
            IsDetachVisible = false
         });
      }

      private static void InitInternalNavigation(EmApp app, EmAppBuilder pars) {
         app.AddNavigation(new Navigation {
            Name = ApprovalManagerNavigationPayload.NavigationName,
            Title = "Approval Manager",
            OrderIndex = -1,
            RequireParameter = false,
            Subtitle = "Review requests and approval history",
            BodyType = BodyType.Of<ApprovalManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            IsMenuVisible = false
         });
         // Bound to no module and no claim on purpose: the viewer only shows what its caller hands it, so
         // the right to see a document is checked where the document is fetched - the caller's action.
         app.AddNavigation(new Navigation {
            Name = PdfViewerNavigationName,
            Title = "PDF Viewer",
            Subtitle = "View, print and save a PDF document",
            OrderIndex = -1,
            Description = "",
            BodyType = BodyType.Of<PdfViewer>(),
            Kind = NavigationKind.Editor,
            EmApp = app,
            RequireParameter = true,
            IsMenuVisible = false
         });
         app.AddNavigation(RequireClaim(new Navigation {
            Name = "admin.users",
            Title = "User Manager",
            Subtitle = "Manage users and robot identities",
            OrderIndex = -1,
            Description = "",
            BodyType = BodyType.Of<UserManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            NavigationIcon = EFontAwesomeIcon.Solid_UsersGear.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, UserManagerClaim));
         app.AddNavigation(new Navigation {
            Name = "admin.users.editor",
            Title = "User Editor",
            Subtitle = "Update User Profile",
            OrderIndex = -1,
            Description = "",
            BodyType = BodyType.Of<UserEditor>(),
            Kind = NavigationKind.Editor,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false
         });
         app.AddNavigation(RequireClaim(new Navigation {
            Name = "admin.roles",
            Title = "Role Manager",
            Subtitle = "Update User Roles",
            OrderIndex = -1,
            Description = "Manages roles for entire erp system",
            BodyType = BodyType.Of<RoleManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            NavigationIcon = EFontAwesomeIcon.Solid_IdBadge.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, RoleManagerClaim));
         app.AddNavigation(RequireClaim(new Navigation {
            Name = "admin.cdn",
            Title = "CDN Manager",
            Subtitle = "Manage public files",
            OrderIndex = -1,
            Description = "Upload, organize and remove the files the server publishes under /cdn.",
            BodyType = BodyType.Of<CdnManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            NavigationIcon = EFontAwesomeIcon.Solid_CloudArrowUp.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, CdnManagerClaim));
         app.AddNavigation(RequireClaim(new Navigation {
            Name = "admin.tasks",
            Title = "Business Task Manager",
            Subtitle = "Monitor long-running server work",
            OrderIndex = -1,
            Description = "See every business task on the server, cancel or clear them, and set how many may run at once.",
            BodyType = BodyType.Of<BusinessTaskManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            NavigationIcon = EFontAwesomeIcon.Solid_ListCheck.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, BusinessTaskManagerClaim));
         app.AddNavigation(RequireClaim(new Navigation {
            Name = "admin.release",
            Title = "Release Manager",
            Subtitle = "Publish the desktop client",
            OrderIndex = -1,
            Description = "Prepare a publish of the desktop client, compare it with the published release, then sync, sign and verify it.",
            BodyType = BodyType.Of<ReleaseManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            NavigationIcon = EFontAwesomeIcon.Solid_BoxOpen.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, ReleaseManagerClaim));
         app.AddNavigation(RequireClaim(new Navigation {
            Name = "admin.container",
            Title = "Container Manager",
            Subtitle = "Manage the container registry",
            OrderIndex = -1,
            Description = "Create roots, folders and containers, and check their total registry storage.",
            BodyType = BodyType.Of<ContainerManager>(),
            Kind = NavigationKind.Manager,
            EmApp = app,
            RequireParameter = false,
            IsMenuVisible = false,
            NavigationIcon = EFontAwesomeIcon.Solid_Cubes.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, ContainerManagerClaim));
         app.AddNavigation(RequireClaim(new Navigation {
            Name="admin.nupak", Title="NuGet Manager", Subtitle="Packages, feeds and prefixes", OrderIndex=85,
            Description="Manage NuGet feeds", BodyType=BodyType.Of<NuPakManager>(), Kind=NavigationKind.Manager, EmApp=app,
            RequireParameter=false, IsMenuVisible=true, MenuPath=MenuPath.Set("Tools/Administrative"),
            NavigationIcon=EFontAwesomeIcon.Solid_Box.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, "Administrative Tools:" + INuPakServices.ManagerClaim));
      }

      /// <summary>
      /// Skema URI <c>pack://</c> baru dikenali .NET setelah <see cref="PackUriHelper"/> pertama kali
      /// disentuh. Aplikasi menyusun pack URI (mis. <see cref="BrandingInfo.LogoSource"/>) di dalam
      /// callback <see cref="BuildApp"/>, yaitu sebelum <see cref="Application"/> WPF dibuat, jadi
      /// skemanya didaftarkan lebih dulu di sini supaya <c>new Uri("pack://...")</c> tidak gagal parse.
      /// </summary>
      static EmApp() {
         _ = PackUriHelper.UriSchemePack; // Paksa .NET mendaftarkan skema URI pack:// lebih awal.
      }
   }
}
