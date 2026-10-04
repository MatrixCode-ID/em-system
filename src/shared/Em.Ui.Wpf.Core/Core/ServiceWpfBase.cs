using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Core
{
   public abstract class ServiceWpfBase : ServiceUiBase
   {
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
      /// Objek aplikasi WPF milik service ini. Tipenya sengaja dipersempit dari <c>IEmApp</c> milik
      /// <see cref="ServiceUiBase"/> jadi <see cref="EmApp"/> (covariant return), sehingga service di
      /// layer UI langsung memakai anggota WPF-nya tanpa perlu cast.
      /// </summary>
      public override EmApp App { get; }
   }
}
