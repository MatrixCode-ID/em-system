using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Em.Ui.Core.Shared;
using Em.Ui.Maui.Shared;

namespace Em.Ui.Maui.Core
{
   /// <summary>
   /// Tipe control yang jadi body sebuah <see cref="Navigation"/>, berikut cara membangunnya. Dipakai
   /// lewat <see cref="Of{T}"/> saat mendaftarkan navigasi.
   /// </summary>
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
      /// Mendeklarasikan <typeparamref name="T"/> sebagai body sebuah navigasi. Control-nya baru
      /// dibangun saat navigasinya benar-benar dibuka.
      /// </summary>
      /// <typeparam name="T">Control yang jadi body, harus mengimplementasikan <see cref="INavigationBody"/>.</typeparam>
      public static BodyType Of<T>() where T : View, INavigationBody => new(typeof(T));

      #endregion
   }
}
