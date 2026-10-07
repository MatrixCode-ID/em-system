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
   /// The main application object for the MAUI side (analogous to <c>EmApp</c> in the backend and in the
   /// desktop client), holding the DI container, the registered navigations and modules, the saved API
   /// connections, and the application theme. Created once through <see cref="BuildApp"/> when the
   /// application starts.
   /// </summary>
   /// <remarks>
   /// It only knows one screen at a time. There is no tabbed layout here - its navigation path is a single
   /// straight line, exactly like the single-page layout of the desktop client.
   /// </remarks>
   public partial class EmApp
   {
      #region Fields and Constants


      /// <summary>
      /// The navigation name of the login screen, registered by the application itself through
      /// <see cref="InitInternalNavigation"/>. It exists as a constant because it is not only its registrar
      /// that names it: <see cref="ShowLoginScreen"/> uses it to open the login screen, and the account menu uses it when
      /// the user signs out - all three must point to exactly the same navigation.
      /// </summary>
      public const string LogonNavigationName = "admin.logon";

      /// <summary>The navigation name of the change password screen of the signed-in user.</summary>
      public const string ChangePasswordNavigationName = "admin.changepassword";

      /// <summary>The navigation name of the default home screen.</summary>
      public const string HomeNavigationName = "Home";

      #endregion

      /// <summary>
      /// Builds the <see cref="EmApp"/> instance: registers the internal services (hashing, the session
      /// store, etc.), runs the <paramref name="builder"/> callback so modules can register their own
      /// services and navigations, then builds the <see cref="ServiceProvider"/> from the DI container.
      /// </summary>
      /// <param name="args">The application's command-line arguments.</param>
      /// <param name="builder">The configuration callback, used by modules to call <c>AddServices</c>/<c>AddNavigation</c>.</param>
      /// <returns>The <see cref="EmApp"/> instance, ready to be run through <see cref="Run{TApp}"/>.</returns>
      public static EmApp BuildApp(string[] args, Action<EmAppBuilder> builder) {
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
      /// Prepares the MAUI application: registers <typeparamref name="TApp"/> as the application class,
      /// hands this object to the MAUI container so it can be asked for through a constructor, then returns
      /// the <see cref="MauiApp"/> that the platform only needs to run.
      /// </summary>
      /// <typeparam name="TApp">The application's <c>Application</c> class.</typeparam>
      /// <param name="configure">
      /// A chance for the application to add its own MAUI configuration - fonts, logging, handlers - before it
      /// is built. May be left empty.
      /// </param>
      /// <remarks>
      /// This is the MAUI counterpart of <c>Run</c> in the desktop client. The difference is that there it
      /// blocks until the application is closed, while here its lifecycle is held by the platform - the first
      /// screen is only installed later through <see cref="CreateRootPage"/>.
      /// </remarks>
      public MauiApp Run<TApp>(Action<MauiAppBuilder>? configure = null) where TApp : class, IApplication {
         var mauiBuilder = MauiApp.CreateBuilder();
         mauiBuilder.UseMauiApp<TApp>();

         // The icon font is registered here, not in the application that uses it: its font file is carried by this
         // library, so this library is also the one responsible for introducing it. If registration were left to
         // the host, one forgotten line would make all icons appear as empty boxes without any error message.
         mauiBuilder.ConfigureFonts(fonts =>
            fonts.AddFont("Font Awesome 7 Free-Solid-900.otf", FontIcons.FontFamily));

         // The MAUI container only needs to know one thing: this application object. Registration of module
         // services lives entirely in EmApp's own container (see BuildApp), just like in the desktop client - so
         // the Application class only needs to ask for EmApp through its constructor.
         mauiBuilder.Services.AddSingleton(this);
         mauiBuilder.Services.AddSingleton<IEmApp>(this);
         mauiBuilder.Services.AddSingleton<IEmAppUi>(this);

         configure?.Invoke(mauiBuilder);
         return mauiBuilder.Build();
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
         app.Services.AddSingleton<IBusinessTaskServices, BusinessTaskService>();
         app.Services.AddSingleton<IApprovalServices, ApprovalService>();
         app.Services.AddSingleton<ISessionStorage, SecureStorageSessionStorage>();
      }

      /// <summary>
      /// Runs the <paramref name="builder"/> callback for configuration from modules, then prepares the main
      /// stack according to the chosen layout and registers all the navigations that were collected.
      /// </summary>
      private static void InitBuilder(EmApp app, Action<EmAppBuilder> builder) {
         var pars = new EmAppBuilder {
            Services = app.Services
         };

         InitInternalNavigation(app);
         InitInternalClaims(app);
         builder(pars);

         app.ApplicationName = pars.ApplicationName ?? "Set ApplicationName to change!";
         // Created after the application name is known, because that name is the prefix of every setting key -
         // creating it earlier would mean writing to the wrong prefix.
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
         // Every PasswordPolicy property already carries its own default value, so an empty instance is the rule
         // in force when the application never calls UsePasswordPolicy.
         app.PasswordPolicy = pars.PasswordPolicy ?? new PasswordPolicy();

         pars.Navigations.EachOf(app.AddNavigation);
         InitDebugMode(app, pars);
      }

      private static void InitDebugMode(EmApp app, EmAppBuilder pars) {
         if (pars.DebugBuilder == null || pars.DebugBuilder.Connections.Count <= 0) return;
         app.IsDebugMode = true;
         app.DebugConnections = [.. pars.DebugBuilder.Connections];
         app.DefaultDebugConnection = pars.DebugBuilder.DefaultConnection;

         // Signed once here, after the debug callback has finished and before the first screen appears, so a
         // mistyped key is found now - not later when every request is answered with "action not found" without
         // any explanation. What is passed to the connection is the token, not the key, so the request path never
         // touches cryptography.
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

      // The built-in administrator account has no user row anywhere - just like the debugger account above,
      // and it is created here for the same reason: it stands in for a user without ever being stored as one.
      // The difference is that this account really signs in through the login screen, so it exists in any
      // build, not only in debug mode.
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

      // The claims of the client's built-in screens. Still empty on the MAUI side: the user manager and role
      // manager screens do not exist here yet, and a claim without a screen that uses it would only appear in
      // the grant list as a row that does nothing.
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
