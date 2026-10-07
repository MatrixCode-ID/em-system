using System.Windows.Media;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// An additional logo loader for image formats that WPF itself does not know (e.g. <c>.svg</c>). The
   /// core only loads bitmaps (<c>.png</c>, <c>.jpg</c>, <c>.ico</c>, <c>.bmp</c>); an application that
   /// uses a logo in another format installs this loader through
   /// <see cref="EmAppBuilder.AddLogoImageLoader{T}"/>.
   /// </summary>
   /// <remarks>
   /// Every registered loader is asked in turn before the core tries to load it as a bitmap. A loader that
   /// does not recognize the format simply returns <c>null</c>.
   /// </remarks>
   public interface ILogoImageLoader
   {
      /// <summary>
      /// Tries to load the logo from <paramref name="source"/>.
      /// </summary>
      /// <param name="source">The location of the logo, usually a pack URI to a resource of the application project.</param>
      /// <returns>
      /// The logo image, or <c>null</c> when this format is not this loader's business. A failure to load a
      /// file whose format is recognized may be thrown as an exception; the core then uses the default logo.
      /// </returns>
      ImageSource? TryLoad(Uri source);
   }
}
