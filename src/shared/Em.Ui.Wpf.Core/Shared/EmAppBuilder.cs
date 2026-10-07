using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// The configuration builder of the WPF application, passed to the callback of <c>EmApp.BuildApp</c> so
   /// each module can register its own services and navigations.
   /// </summary>
   public partial class EmAppBuilder
   {
      internal EmAppBuilder() { }

      /// <summary>
      /// The name of the application, used as the main window title and the Registry subkey name. It must be
      /// set by the application (not a module) during initial configuration.
      /// </summary>
      public string? ApplicationName { get; set; }

      /// <summary>
      /// The brand display settings currently in force (logo, login screen texts, and themes). Filled through
      /// <see cref="ApplyBranding"/>; if the application never calls it, it stays an empty
      /// <see cref="BrandingInfo"/> so all its properties fall back to generic defaults.
      /// </summary>
      internal BrandingInfo? Branding { get; private set; }

      /// <summary>
      /// Sets the application's brand display: the logo, the login screen texts, and the light/dark themes.
      /// Call it once during the application's initial configuration (not a module); a property of
      /// <paramref name="info"/> left empty falls back to a generic default, see each property in
      /// <see cref="BrandingInfo"/>.
      /// </summary>
      /// <param name="info">The brand settings to use.</param>
      public void ApplyBranding(BrandingInfo info) {
         Branding = info;
      }

      /// <summary>
      /// Registers an additional theme applier for third-party control libraries that have their own theme
      /// system. The applier is called every time the active theme is applied - see
      /// <see cref="IThemeApplier"/>. Safe to call repeatedly for the same type: what is registered stays one.
      /// </summary>
      /// <typeparam name="T">The type of the theme applier.</typeparam>
      public void AddThemeApplier<T>() where T : class, IThemeApplier {
         Services.TryAddEnumerable(ServiceDescriptor.Singleton<IThemeApplier, T>());
      }

      /// <summary>
      /// Registers a logo loader for image formats that the core does not know (e.g. <c>.svg</c>) - see
      /// <see cref="ILogoImageLoader"/>. Safe to call repeatedly for the same type: what is registered stays
      /// one.
      /// </summary>
      /// <typeparam name="T">The type of the logo loader.</typeparam>
      public void AddLogoImageLoader<T>() where T : class, ILogoImageLoader {
         Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILogoImageLoader, T>());
      }

      /// <summary>
      /// The password rules set by the application through <see cref="UsePasswordPolicy"/>, or <c>null</c>
      /// when never set - in which case the default values of every property of
      /// <see cref="PasswordPolicy"/> are used.
      /// </summary>
      internal PasswordPolicy? PasswordPolicy { get; private set; }

      /// <summary>
      /// Sets the application's password rules. <paramref name="options"/> receives a rule object that
      /// already holds the default values, so the callback only needs to state what differs - including
      /// turning everything off through <see cref="PasswordPolicy.Disable"/>. Call it once during the
      /// application's initial configuration (not a module).
      /// </summary>
      /// <param name="options">A callback that changes the rules to suit the application.</param>
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
      /// The server's RSA public key for debug mode, used to verify the debug connection to the server.
      /// </summary>
      public string ServerRsaPublicKey { get; internal set; } = null!;

      /// <summary>
      /// Registers a module service into the DI container as a scoped service.
      /// </summary>
      /// <typeparam name="T1">The service interface type, which must implement <see cref="IServices"/>.</typeparam>
      /// <typeparam name="T2">The concrete implementation type of <typeparamref name="T1"/>.</typeparam>
      public void AddServices<T1, T2>() where T1 : IServices {
         Services.AddScoped(typeof(T1), typeof(T2));
      }
      
      internal DebugBuilder? DebugBuilder { get; set; }

      internal List<Navigation> Navigations { get; } = [];
      internal Navigation? CustomHomeNavigation { get; set; }

      /// <summary>
      /// Registers <paramref name="navigation"/> bound to module <typeparamref name="T"/>: a user may open it
      /// if they hold any claim in that module. <typeparamref name="T"/> must carry <c>[Module]</c> - see
      /// <see cref="ClaimAction.Create{T}"/>.
      /// </summary>
      /// <typeparam name="T">The service class that owns this navigation.</typeparam>
      /// <param name="navigation">The navigation being registered.</param>
      public void AddNavigation<T>(Navigation navigation) where T : IServices {
         navigation.ModuleName = ModuleAttribute.ResolveName(typeof(T));
         Navigations.Add(navigation);
      }

      /// <summary>
      /// Registers <paramref name="navigation"/> bound to a particular claim: a user may open it only if they
      /// hold exactly the claim <paramref name="claimName"/> on module <typeparamref name="T"/>.
      /// </summary>
      /// <typeparam name="T">The service class that owns this navigation.</typeparam>
      /// <param name="navigation">The navigation being registered.</param>
      /// <param name="claimName">The name of the required claim, without the module name in front of it.</param>
      public void AddNavigation<T>(Navigation navigation, string claimName) where T : IServices {
         var claim = ClaimAction.Create<T>(claimName);
         navigation.ModuleName = claim.ModuleName;
         navigation.RequiredClaim = claim;
         Navigations.Add(navigation);
      }

      /// <summary>
      /// Registers <paramref name="navigation"/> with no module or claim binding at all: open to anyone who
      /// has signed in.
      /// </summary>
      /// <param name="navigation">The navigation being registered.</param>
      public void AddNavigation(Navigation navigation) {
         Navigations.Add(navigation);
      }

      internal ApplicationLayout AppLayout { get; set; } = ApplicationLayout.MultiTab;

      /// <summary>
      /// Uses the single-page layout: the main window shows one screen at a time, with home, Back/Forward
      /// buttons, and detach. If never called, the application uses the multi-tab layout, where every screen
      /// that is opened becomes its own tab in the main window.
      /// </summary>
      public void UseSinglePageLayout() {
         AppLayout = ApplicationLayout.SinglePage;
      }

      /// <summary>
      /// Turns on the screen transition animation: the new screen slides in while fading, from the right for
      /// Forward and a newly opened screen, from the left for Back and Home. Only applies to the single-page
      /// layout (see <see cref="UseSinglePageLayout"/>); in the multi-tab layout this setting is ignored. The
      /// default is <c>false</c>.
      /// </summary>
      public bool EnableAnimation { get; set; }

      /// <summary>
      /// The duration of the screen transition animation when <see cref="EnableAnimation"/> is on. The default
      /// is 200 ms; keep it short (about 150-250 ms) so the frequently used Back/Forward does not feel slow.
      /// <see cref="TimeSpan.Zero"/> is the same as turning the animation off.
      /// </summary>
      /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
      public TimeSpan TransitionTime {
         get;
         set {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
         }
      } = TimeSpan.FromMilliseconds(200);

      /// <summary>
      /// Replaces the default home screen with the application's own. If never called, the default home
      /// screen of <c>Em.Ui.Wpf.Core</c> is used. The multi-tab layout has no home, so this call has no effect
      /// there.
      /// </summary>
      /// <param name="customHomeNavigation">The navigation that becomes home.</param>
      public void UseHomeNavigation(Navigation customHomeNavigation) {
         CustomHomeNavigation = customHomeNavigation;
      }
   }
}