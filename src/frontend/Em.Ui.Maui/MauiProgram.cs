using Microsoft.Extensions.Logging;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Core;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui;

public static class MauiProgram {
   public static MauiApp CreateMauiApp() {
      var emApp = EmApp.BuildApp([], builder => {

         builder.ApplicationName = "Em System Mobile";
         builder.ApplyBranding(new BrandingInfo {
            LogoSource = "logo.png",
            Title = "Em System",
            Tagline = "Mobile Device Companion",
            Description = "",
            Copyright = "© 2026 Matrix Code. All rights reserved."
         });
         builder.UsePasswordPolicy(opt => {
            opt.MinLength = 8;
            opt.SymbolRule = PasswordRuleLevel.Off;
         });
#if DEBUG
         // builder.AddDebug(opt => {
         //    // 10.0.2.2 is the address the Android emulator uses to name the developer machine's
         //    // localhost - "localhost" inside the emulator points to the emulator itself.
         //    opt.AddDebugConnection("Localhost", "http://10.0.2.2:5132", true);
         //    if (ReadDevelopmentToken() is { } privateKey) {
         //       opt.SetDebugKey("Development Token", privateKey);
         //    }
         // });
#endif
      });

      return emApp.Run<App>(maui => {
         maui.ConfigureFonts(fonts => {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
         });

#if DEBUG
         maui.Logging.AddDebug();
#endif
      });
   }

#if DEBUG
   /// <summary>The private key embedded from ArtefactsPath at build time, or null when its file does not exist.</summary>
   private static string? ReadDevelopmentToken() {
      using var stream = typeof(MauiProgram).Assembly.GetManifestResourceStream("DevelopmentToken");
      if (stream is null) return null;
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd().Trim();
   }
#endif
}
