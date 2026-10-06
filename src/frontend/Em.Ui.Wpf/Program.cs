using System.IO;
using Em.Test.Wpf;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;

namespace Em;

internal static class Program
{
   [STAThread]
   public static void Main(string[] args) {
      var emApp = EmApp.BuildApp(args, builder => {
         
         builder.ApplicationName = "Em System";
         builder.ApplyBranding(new BrandingInfo {
            LogoSource = "pack://application:,,,/Em.Ui.Wpf;component/Logo.png",
            Title = "Em System",
            Tagline = "ERP engine by Matrix Code",
            Description = "Backend API, desktop, and mobile in one platform.",
            Copyright = "© 2026 Matrix Code. All rights reserved."
         });
         builder.UsePasswordPolicy(opt => {
            opt.MinLength = 8;
            opt.SymbolRule = PasswordRuleLevel.Off;
         });
         builder.EnableAnimation = true;
         builder.AddTestModule();
#if DEBUG
         builder.AddDebug(opt => {
            opt.AddDebugConnection("Localhost", "http://localhost:5132", true);
            if (ReadDevelopmentToken() is { } privateKey) {
               opt.SetDebugKey("Development Token", privateKey);
            }
         });
#endif
      });
      emApp.Run();
   }

#if DEBUG
   /// <summary>Private key yang di-embed dari ArtefactsPath saat build, atau null bila berkasnya tidak ada.</summary>
   private static string? ReadDevelopmentToken() {
      using var stream = typeof(Program).Assembly.GetManifestResourceStream("DevelopmentToken");
      if (stream is null) return null;
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd().Trim();
   }
#endif
}
