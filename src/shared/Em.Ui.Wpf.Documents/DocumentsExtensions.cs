using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Documents
{
   /// <summary>
   /// Turns on the document controls (grid, sheet, writer, value editors and print preview) in an Em WPF host.
   /// </summary>
   public static class DocumentsExtensions
   {
      /// <summary>
      /// Registers <see cref="DocumentsThemeApplier"/>, so the document controls follow the active Em theme instead of
      /// their built-in Light palette. Safe to call more than once. The controls also work without this call; they then
      /// keep their built-in palette.
      /// </summary>
      /// <param name="builder">The application builder.</param>
      /// <returns>The same builder, for chaining.</returns>
      public static EmAppBuilder UseDocuments(this EmAppBuilder builder) {
         ArgumentNullException.ThrowIfNull(builder);
         builder.AddThemeApplier<DocumentsThemeApplier>();
         return builder;
      }
   }
}
