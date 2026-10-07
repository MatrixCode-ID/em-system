using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Base class of module services on the MAUI side: connects the service to the <see cref="ApiClient"/>
   /// of the connection that is currently active, and keeps that connection correct when the connection
   /// changes.
   /// </summary>
   public abstract class ServiceMauiBase : ServiceUiBase
   {
      protected ServiceMauiBase(EmApp app) : base(app) {
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
      /// The MAUI application object of this service. Its type is deliberately narrowed from the <c>IEmApp</c>
      /// of <see cref="ServiceUiBase"/> to <see cref="EmApp"/> (covariant return), so a service in the UI layer
      /// uses its MAUI members directly without a cast.
      /// </summary>
      public override EmApp App { get; }
   }
}
