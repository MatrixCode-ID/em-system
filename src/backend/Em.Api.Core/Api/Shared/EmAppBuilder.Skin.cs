using System.Reflection;
using Em.Api.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Em.Api.Shared;

public partial class EmAppBuilder {
   private bool _cdnSkinRegistered;
   private bool _homeSkinRegistered;
   /// <summary>Host name shown by public pages. Defaults to the entry assembly name.</summary>
   public string ApplicationName { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "API";
   /// <summary>Registers the singleton CDN skin. May only be called once, independently of CDN enablement.</summary>
   public void UseCdnSkin<TSkin>() where TSkin : class, IEmCdnSkin {
      if (_cdnSkinRegistered) throw new InvalidOperationException("The CDN skin is already registered.");
      Services.AddSingleton<IEmCdnSkin, TSkin>();
      _cdnSkinRegistered = true;
   }
   /// <summary>Registers the singleton home skin. May only be called once.</summary>
   public void UseHomeSkin<TSkin>() where TSkin : class, IEmHomeSkin {
      if (_homeSkinRegistered) throw new InvalidOperationException("The home skin is already registered.");
      Services.AddSingleton<IEmHomeSkin, TSkin>();
      _homeSkinRegistered = true;
   }
   internal void RegisterDefaultSkins() {
      Services.TryAddSingleton<IEmCdnSkin, DefaultCdnSkin>();
      Services.TryAddSingleton<IEmHomeSkin, DefaultHomeSkin>();
   }
}
