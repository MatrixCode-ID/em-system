using System.IO;
using Em.Ui.Wpf.Core;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>Which built-in viewer shows a file.</summary>
   public enum DocumentViewKind
   {
      /// <summary>No built-in viewer opens the file; offer to save it instead.</summary>
      None,
      /// <summary>The PDF viewer.</summary>
      Pdf,
      /// <summary>The document viewer with <see cref="MatrixSheet"/>, for workbooks such as XLSX and CSV.</summary>
      Sheet,
      /// <summary>The document viewer with <see cref="MatrixWriter"/>, for word-processing documents such as DOCX.</summary>
      Writer
   }

   /// <summary>
   /// Opens downloaded files in the built-in read-only viewers: PDF in the PDF viewer, and every format that
   /// <see cref="MatrixSheet"/> or <see cref="MatrixWriter"/> opens in the document viewer. The document viewer is
   /// registered by <see cref="DocumentsExtensions.UseDocuments"/>.
   /// </summary>
   public static class DocumentViewers
   {
      /// <summary>
      /// The navigation name of the built-in document viewer. It belongs to no module or claim and must be opened with a
      /// <see cref="DocumentViewerNavigationPayload"/>; the usual way is <see cref="ViewDocument(NavigationEntry,string,Func{CancellationToken,Task{Stream}},string)"/>.
      /// </summary>
      public const string NavigationName = "em.viewer.document";

      /// <summary>Gets the file extensions, including the leading dot, that a built-in viewer opens.</summary>
      public static IReadOnlyList<string> SupportedExtensions { get; } =
         [".pdf", .. MatrixSheet.SupportedExtensions, .. MatrixWriter.SupportedExtensions];

      /// <summary>Tells which built-in viewer shows a file, judged by the extension of its name.</summary>
      /// <param name="fileName">The file name or path, or an extension with its leading dot.</param>
      /// <returns>The viewer, or <see cref="DocumentViewKind.None"/> when no built-in viewer opens it.</returns>
      public static DocumentViewKind KindOf(string? fileName) {
         if (string.IsNullOrWhiteSpace(fileName)) return DocumentViewKind.None;
         var extension = Path.GetExtension(fileName);
         if (extension.Length == 0 && fileName.StartsWith('.')) extension = fileName;
         if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return DocumentViewKind.Pdf;
         if (MatrixSheet.CanOpen(extension)) return DocumentViewKind.Sheet;
         if (MatrixWriter.CanOpen(extension)) return DocumentViewKind.Writer;
         return DocumentViewKind.None;
      }

      /// <summary>Checks whether a built-in viewer shows a file, judged by the extension of its name.</summary>
      /// <param name="fileName">The file name or path, or an extension with its leading dot.</param>
      /// <returns>True for PDF and for every format of <see cref="MatrixSheet"/> and <see cref="MatrixWriter"/>.</returns>
      public static bool CanView(string? fileName) => KindOf(fileName) != DocumentViewKind.None;

      /// <summary>
      /// Opens a file read-only in the built-in viewer that fits its extension, relative to this entry's stack, so the
      /// viewer opens in the same window as the calling screen. A PDF goes to the PDF viewer; spreadsheets and documents
      /// go to the document viewer, which needs <see cref="DocumentsExtensions.UseDocuments"/> in the host.
      /// </summary>
      /// <param name="entry">The navigation entry of the calling screen.</param>
      /// <param name="title">The title of the viewer, which is also its entry's unique key: include the document's unique identifier.</param>
      /// <param name="loader">The content fetcher; the rights to the document are guarded there, not by the viewer.</param>
      /// <param name="fileName">The file name; its extension selects the viewer and it is the default name for Save as.</param>
      /// <returns><c>false</c> when the viewer could not be opened, e.g. the move was refused.</returns>
      /// <exception cref="NotSupportedException">No built-in viewer opens the file; check <see cref="CanView"/> first.</exception>
      public static Task<bool> ViewDocument(this NavigationEntry entry, string title, Func<CancellationToken, Task<Stream>> loader, string fileName) {
         ArgumentNullException.ThrowIfNull(entry);
         return KindOf(fileName) == DocumentViewKind.Pdf
            ? entry.ViewPdf(title, loader, fileName)
            : entry.NavigateTo(NavigationName, new DocumentViewerNavigationPayload(title, loader, fileName));
      }

      /// <summary>
      /// Opens a file read-only in the built-in viewer that fits its extension, relative to the main stack. From inside a
      /// screen, use <see cref="ViewDocument(NavigationEntry,string,Func{CancellationToken,Task{Stream}},string)"/> so the
      /// viewer opens in that screen's window. The parameters follow that method.
      /// </summary>
      /// <param name="app">The application.</param>
      /// <param name="title">The title of the viewer, which is also its entry's unique key.</param>
      /// <param name="loader">The content fetcher; the rights to the document are guarded there, not by the viewer.</param>
      /// <param name="fileName">The file name; its extension selects the viewer.</param>
      /// <returns><c>false</c> when the viewer could not be opened.</returns>
      /// <exception cref="NotSupportedException">No built-in viewer opens the file; check <see cref="CanView"/> first.</exception>
      public static Task<bool> ViewDocument(this EmApp app, string title, Func<CancellationToken, Task<Stream>> loader, string fileName) {
         ArgumentNullException.ThrowIfNull(app);
         return KindOf(fileName) == DocumentViewKind.Pdf
            ? app.ViewPdf(title, loader, fileName)
            : app.NavigateTo(NavigationName, new DocumentViewerNavigationPayload(title, loader, fileName));
      }
   }
}
