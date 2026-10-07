using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Core
{
   /// <summary>The default implementation of <see cref="IBodyType"/>, holding the type of a navigation body.</summary>
   public class BodyType : IBodyType
   {
      private BodyType(Type bodyType) {
         Type = bodyType;
      }

      /// <inheritdoc />
      public Type Type { get; }

      internal INavigationBody Create(NavigationEntry entry) {
         // ActivatorUtilities gives the body the same constructor injection the container would,
         // but leaves ownership with the caller. A body resolved through GetRequiredService is
         // tracked by the root provider the moment it implements IDisposable, which would keep
         // every released body alive for the lifetime of the application and defeat its release.
         var app = entry.EmApp;
         var view = (View)ActivatorUtilities.CreateInstance(app.ServiceProvider, Type);
         if (view.BindingContext is MvvmModelBase model) {
            model.EmApp = app;
            model.NavigationEntry = entry;
         }

         return (INavigationBody)view;
      }

      #region Statics

      /// <summary>
      /// Declares <typeparamref name="T"/> as the body of a navigation. The control is only built when the
      /// navigation is really opened.
      /// </summary>
      /// <typeparam name="T">The control that becomes the body; it must implement <see cref="INavigationBody"/>.</typeparam>
      public static BodyType Of<T>() where T : View, INavigationBody => new(typeof(T));

      #endregion
   }
}
