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
   /// The main application object for the WPF side (analogous to <c>EmApp</c> in the backend), holding the
   /// DI container, the registered navigations and modules, the saved API connections (in the Registry), and
   /// the application theme. Created once through <see cref="BuildApp"/> when the application starts.
   /// </summary>
   public partial class EmApp
   {
      #region Fields and Constants
      /// <summary>
      /// The navigation name of the login screen, registered by the application itself through
      /// <see cref="InitInternalNavigation"/>. It exists as a constant because it is not only its registrar
      /// that names it: the main window uses it to open the login screen, and the account menu uses it when
      /// the user signs out - all three must point to exactly the same navigation.
      /// </summary>
      public const string LogonNavigationName = "admin.logon";

      /// <summary>
      /// The navigation name of the application's built-in PDF viewer. This viewer is not guarded by a claim
      /// and must be opened with a <see cref="PdfViewerNavigationPayload"/>; the usual way is
      /// <see cref="ViewPdf"/> or <see cref="NavigationEntry.ViewPdf"/>, not <c>NavigateTo</c> with this name.
      /// </summary>
      public const string PdfViewerNavigationName = "em.viewer.pdf";

      #endregion

       /// <summary>
      /// Builds the <see cref="EmApp"/> instance: registers the internal services (hashing, sessions, etc.),
      /// runs the <paramref name="builder"/> callback so modules can register their own services and
      /// navigations, then builds the <see cref="ServiceProvider"/> from the DI container.
      /// </summary>
      /// <param name="args">The application's command-line arguments.</param>
      /// <param name="builder">The configuration callback, used by modules to call <c>AddServices</c>/<c>AddNavigation</c>.</param>
      /// <returns>The <see cref="EmApp"/> instance, ready to be run through <see cref="Run"/>.</returns>
      public static EmApp BuildApp(string[] args, Action<EmAppBuilder> builder) {
         // Before anything else: it may hand the process over to the launcher and exit.
         LauncherIntegration.Initialize(args);
         var app = new EmApp(args);

         InitInternalServices(app);
         InitBuilder(app, builder);

         // Closed here, not at the end of InitBuilder: from outside, "finished being built" means all of
         // BuildApp has finished, and the only place that may decide that moment is this line.
         app.SealInternalClaims();

         app._serviceProvider = app.Services.BuildServiceProvider();
         return app;
      }

      /// <summary>
      /// Registers the application's built-in core services into the DI container: hashing, the
      /// <see cref="EmApp"/> instance itself (through its three contracts), and the core data services used
      /// across modules - currently contact data and user credential data.
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
      /// Runs the <paramref name="builder"/> callback for configuration from modules, then prepares the main
      /// stack according to the chosen layout and registers all the navigations that were collected.
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
         // Every PasswordPolicy property already carries its own default value, so an empty instance is the rule
         // in force when the application never calls UsePasswordPolicy.
         app.PasswordPolicy = pars.PasswordPolicy ?? new PasswordPolicy();

         // Registered here, after the module callbacks have finished, so its content is complete and no longer
         // changes. Always registered - even when there is no panel at all - so the approval screen does not
         // need to know the difference.
         app.Services.AddSingleton(pars.ApprovalPanels);

         pars.Navigations.EachOf(app.AddNavigation);
         InitDebugMode(app, pars);
      }

      private static void InitDebugMode(EmApp app, EmAppBuilder pars) {
         if (pars.DebugBuilder == null || pars.DebugBuilder!.Connections.Count <= 0) return;
         app.IsDebugMode = true;
         app.DebugConnections = [.. pars.DebugBuilder!.Connections!];
         app.DefaultDebugConnection = pars.DebugBuilder!.DefaultConnection;

         // Signed once here, after the debug callback has finished and before the first window appears, so a
         // mistyped key is found now - not later when every request is answered with "action not found" and no
         // explanation. What is passed on to the connection is the token, not the key, so the request path never
         // touches cryptography.
         var debugToken = pars.DebugBuilder!.CreateDebugToken();
         app.DebugConnections.EachOf(r => r.DebugToken = debugToken);

         app.SetActiveUser(CreateDebuggerUser(app));
      }

      // Built here rather than alongside the application object, so that an application started
      // without a debug connection never has this account at hand at all: it is an administrator
      // that stands in for signing in, and the only place it is allowed to come from is debug mode.
      // Its row exists nowhere on the server - Defaults.DebuggerUserId is what tells it apart.
      internal static User CreateDebuggerUser(EmApp app) =>
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

      // The built-in administrator account has no user row anywhere - just like the debugger account above,
      // and it is created here for the same reason: it stands in for a user without ever being stored as one.
      // The difference is that this account really signs in through the login screen, so it exists in any
      // build, not only in debug mode.
      internal static User CreateAdminUser(EmApp app) =>
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

      // The claims of the client's built-in screens. Declared here rather than on the server, because these
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
            RequireParameter=false, IsMenuVisible=false,
            NavigationIcon=EFontAwesomeIcon.Solid_Box.CreateImageSource(System.Windows.Media.Brushes.Gray)
         }, "Administrative Tools:" + INuPakServices.ManagerClaim));
      }

      /// <summary>
      /// The <c>pack://</c> URI scheme is only known to .NET after <see cref="PackUriHelper"/> has first been
      /// touched. The application composes pack URIs (e.g. <see cref="BrandingInfo.LogoSource"/>) inside the
      /// <see cref="BuildApp"/> callback, which is before the WPF <see cref="Application"/> is created, so the
      /// scheme is registered here first so <c>new Uri("pack://...")</c> does not fail to parse.
      /// </summary>
      static EmApp() {
         _ = PackUriHelper.UriSchemePack; // Force .NET to register the pack:// URI scheme early.
      }
   }
}
