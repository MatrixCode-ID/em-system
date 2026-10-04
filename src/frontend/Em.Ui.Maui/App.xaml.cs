using Em.Ui.Maui.Core;

namespace Em.Ui.Maui;

public partial class App : Application {
   private readonly EmApp _emApp;

   public App(EmApp emApp) {
      _emApp = emApp;
      InitializeComponent();
   }

   protected override Window CreateWindow(IActivationState? activationState) {
      return new Window(_emApp.CreateRootPage()) {
         Title = _emApp.ApplicationName
      };
   }
}
