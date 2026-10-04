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
}
