using System.Reflection;
using Em.Ui.Wpf.Shared;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core;

/// <summary>Reads host identity generated from MSBuild properties.</summary>
public static class HostIdentity {
   /// <summary>Gets a host metadata value.</summary>
   public static string? Metadata(Assembly assembly, string name) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == name)?.Value;
   /// <summary>Registry path for new hosts, with the legacy fallback.</summary>
   public static string RegistryPath(string? appId, string applicationName) => string.IsNullOrWhiteSpace(appId) ? applicationName : "Software\\" + appId;
   internal static void Apply(EmAppBuilder builder, Assembly? assembly) {
      if (assembly is null) return;
      builder.ApplicationName ??= assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
      builder.AppId ??= Metadata(assembly, "EmAppId");
      var branding = builder.Branding ?? new BrandingInfo();
      string? Resource(string name) => Metadata(assembly, name) is { Length: > 0 } path ? $"pack://application:,,,/{assembly.GetName().Name};component/{path}" : null;
      branding.IconSource ??= Resource("EmAppIcon"); branding.LogoSource ??= Resource("EmAppLogo");
      builder.ApplyBranding(branding);
   }
}
