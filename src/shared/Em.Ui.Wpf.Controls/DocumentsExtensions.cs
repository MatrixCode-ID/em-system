using System.IO;
using System.Runtime.CompilerServices;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Core;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Turns on the document controls (grid, sheet, writer, value editors and print preview) in an Em WPF host.
   /// </summary>
   public static class DocumentsExtensions
   {
      private static readonly ConditionalWeakTable<EmAppBuilder, object> Registered = new();

      /// <summary>
      /// Registers <see cref="DocumentsThemeApplier"/>, so the document controls follow the active Em theme instead of
      /// their built-in Light palette, and the built-in document viewer (<see cref="DocumentViewers.NavigationName"/>),
      /// which shows spreadsheets and word-processing documents read-only through
      /// <see cref="DocumentViewers.ViewDocument(NavigationEntry,string,Func{CancellationToken,Task{Stream}},string)"/>.
      /// Safe to call more than once. The controls also work without this call; they then keep their built-in palette,
      /// and the document viewer cannot be opened.
      /// </summary>
      /// <param name="builder">The application builder.</param>
      /// <returns>The same builder, for chaining.</returns>
      public static EmAppBuilder UseDocuments(this EmAppBuilder builder) {
         ArgumentNullException.ThrowIfNull(builder);
         builder.AddThemeApplier<DocumentsThemeApplier>();
         if (Registered.TryAdd(builder, new object())) {
            // Bound to no module and no claim on purpose, like the PDF viewer: the viewer only shows what its caller
            // hands it, so the right to see a document is checked where the document is fetched.
            builder.AddNavigation(new Navigation {
               Name = DocumentViewers.NavigationName,
               Title = "Document Viewer",
               Subtitle = "View and save a spreadsheet or document",
               OrderIndex = -1,
               Description = "",
               BodyType = BodyType.Of<DocumentViewer>(),
               Kind = NavigationKind.Editor,
               RequireParameter = true,
               IsMenuVisible = false
            });
         }
         return builder;
      }
   }
}
