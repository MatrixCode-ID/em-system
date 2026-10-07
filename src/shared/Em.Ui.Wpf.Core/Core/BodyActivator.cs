using Microsoft.Extensions.DependencyInjection;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Shared;
using UserControl = System.Windows.Controls.UserControl;

namespace Em.Ui.Wpf.Core
{
   /// <summary>The default implementation of <see cref="IBodyType"/>, holding the type of a navigation body.</summary>
   public class BodyType : IBodyType
   {
      private BodyType(Type bodyType) {
         Type = bodyType;
      }
      /// <summary>The type.</summary>
      public Type Type { get; }

      internal INavigationBody Create(NavigationEntry entry) {
         // ActivatorUtilities gives the body the same constructor injection the container would,
         // but leaves ownership with the caller. A body resolved through GetRequiredService is
         // tracked by the root provider the moment it implements IDisposable, which would keep
         // every released body alive for the lifetime of the application and defeat its release.
         var app = entry.EmApp;
         var ctl = (UserControl)ActivatorUtilities.CreateInstance(app.ServiceProvider, Type);
         if (ctl.DataContext is MvvmModelBase model) {
            model.EmApp = app;
            model.NavigationEntry = entry;
         }

         return (INavigationBody)ctl;
      }

      #region Statics

      /// Without any lambda at all for a control with an empty constructor.
      public static BodyType Of<T>() where T : UserControl, INavigationBody => new(typeof(T));

      #endregion
   }
}
