using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   /// <summary>Base class of module services on the WPF side, bound to the WPF <see cref="EmApp"/>.</summary>
   public abstract class ServiceWpfBase : ServiceUiBase
   {
      /// <summary>Creates a new instance of <see cref="ServiceWpfBase"/>.</summary>
      public ServiceWpfBase(EmApp app) : base(app) {
         App = app;
         // A service is only built the first time something resolves it from the container, which is
         // usually long after the active connection was picked. Subscribing alone would then wait for
         // a change that never comes, so the client for the connection already in place is taken here.
         ApiClient = App.GetActiveApiClient();
         App.ActiveConnectionChanged += AppOnActiveConnectionChanged;
      }

      private void AppOnActiveConnectionChanged(object? sender, EventArgs e) {
         if (App.ActiveConnection == null) {
            return;
         }
         ApiClient = App.GetActiveApiClient();
      }

      /// <summary>
      /// The WPF application object of this service. Its type is deliberately narrowed from the <c>IEmApp</c>
      /// of <see cref="ServiceUiBase"/> to <see cref="EmApp"/> (covariant return), so a service in the UI
      /// layer uses its WPF members directly without a cast.
      /// </summary>
      public override EmApp App { get; }
   }
}
