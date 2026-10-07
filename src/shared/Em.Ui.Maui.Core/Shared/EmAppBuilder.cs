using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Em.Shared;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// The configuration builder of the MAUI application, passed to the callback of <c>EmApp.BuildApp</c>
   /// so each module can register its own services and navigations.
   /// </summary>
   /// <remarks>
   /// The difference from the WPF-side builder: there is no layout choice at all here. A MAUI application
   /// only knows one screen at a time (SPA), so there is no <c>UseSinglePageLayout</c> to call and no
   /// registration of a tabbed main control.
   /// </remarks>
   public class EmAppBuilder
   {
      internal EmAppBuilder() { }

      /// <summary>
      /// The name of the application, used as the title of the main page and as the prefix of the keys of
      /// the application settings storage. It must be set by the application (not a module) during initial
      /// configuration.
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

      /// <summary>
      /// The list of navigations registered by modules through <see cref="AddNavigation{T}(Navigation)"/> and
      /// its companions. Each entry becomes one screen that can be opened by its name, and those whose
      /// <c>IsMenuVisible</c> is on also appear in the home menu.
      /// </summary>
      internal List<Navigation> Navigations { get; } = [];

      internal Navigation? CustomHomeNavigation { get; private set; }

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

      /// <summary>
      /// Replaces the default home screen with the application's own. If never called, the default home
      /// screen of <c>Em.Ui.Maui.Core</c> is used.
      /// </summary>
      /// <param name="customHomeNavigation">The navigation that becomes home.</param>
      public void UseHomeNavigation(Navigation customHomeNavigation) {
         CustomHomeNavigation = customHomeNavigation;
      }
   }
}
