using System.IO;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Parameter for opening the built-in document viewer, which shows a spreadsheet in <see cref="MatrixSheet"/> or a
   /// word-processing document in <see cref="MatrixWriter"/>, both read-only. Like the PDF viewer it belongs to no module
   /// and is guarded by no claim: it only shows what <see cref="Loader"/> returns, so the right to see the document is
   /// checked where the content is fetched, usually the server action called by <see cref="Loader"/>.
   /// </summary>
   /// <remarks>
   /// The usual way to open the viewer is <see cref="DocumentViewers.ViewDocument(Em.Ui.Wpf.Core.NavigationEntry,string,System.Func{System.Threading.CancellationToken,System.Threading.Tasks.Task{System.IO.Stream}},string)"/>,
   /// which also sends a PDF to the PDF viewer. "Save as" writes the bytes returned by <see cref="Loader"/> unchanged.
   /// </remarks>
   public sealed class DocumentViewerNavigationPayload : NavigationPayloadBase
   {
      private readonly string _title;

      /// <summary>Creates the parameter of the document viewer.</summary>
      /// <param name="title">
      /// Title of the viewer's tab or screen, which is also its entry's unique key across the application. Include the
      /// unique identifier of the document, not only its type; opening a title that is already open only shows that viewer.
      /// </param>
      /// <param name="loader">
      /// The function that fetches the content. Called when the viewer opens and every time Reload is pressed, so it must
      /// be callable repeatedly. The stream it returns is closed by the viewer.
      /// </param>
      /// <param name="fileName">
      /// The file name of the content. Its extension selects the editor (see <see cref="DocumentViewers.CanView"/>) and it
      /// is the default name when the user saves the file.
      /// </param>
      /// <exception cref="System.NotSupportedException">The extension of <paramref name="fileName"/> is not a spreadsheet or document format the viewer opens.</exception>
      public DocumentViewerNavigationPayload(string title, Func<CancellationToken, Task<Stream>> loader, string fileName) : base(null) {
         ArgumentException.ThrowIfNullOrWhiteSpace(title);
         ArgumentNullException.ThrowIfNull(loader);
         ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
         Kind = DocumentViewers.KindOf(fileName);
         if (Kind is not (DocumentViewKind.Sheet or DocumentViewKind.Writer))
            throw new NotSupportedException($"The document viewer cannot open '{Path.GetFileName(fileName)}'.");
         _title = title;
         Loader = loader;
         FileName = fileName;
      }

      /// <summary>Title of the viewer, which is also its entry's unique key across the application.</summary>
      public override string Title => _title;

      /// <summary>The function that fetches the content; its token is cancelled when the viewer closes before the content arrives.</summary>
      public Func<CancellationToken, Task<Stream>> Loader { get; }

      /// <summary>The file name of the content, used to choose the editor and as the default name when saving.</summary>
      public string FileName { get; }

      /// <summary>The editor that shows the content, chosen from the extension of <see cref="FileName"/>.</summary>
      public DocumentViewKind Kind { get; }
   }
}
