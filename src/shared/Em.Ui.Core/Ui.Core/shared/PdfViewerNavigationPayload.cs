namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Parameter for opening the application's built-in PDF viewer. This viewer belongs to no module and is
   /// not guarded by a claim: it only shows the content supplied by <see cref="Loader"/>. The rights check
   /// therefore lives where the PDF content is fetched - usually the server action called by
   /// <see cref="Loader"/> - not in the viewer.
   /// </summary>
   /// <remarks>
   /// The viewer does not store the result of <see cref="Loader"/> in another form: what is saved through
   /// "Save as" is the bytes it returned as-is, so a digitally signed PDF stays valid.
   /// </remarks>
   public class PdfViewerNavigationPayload : NavigationPayloadBase
   {
      private readonly string _title;

      /// <summary>
      /// Creates the parameter of the PDF viewer.
      /// </summary>
      /// <param name="title">
      /// Title of the viewer's tab/screen, which is also its entry's unique key - see
      /// <see cref="NavigationPayloadBase.Title"/>. Include the document's unique identifier (e.g. the
      /// document number), not just its document type.
      /// </param>
      /// <param name="loader">
      /// The function that fetches the PDF content. Called when the viewer opens and every time the Refresh
      /// button is pressed, so it must be callable repeatedly. The stream it returns is closed by the viewer.
      /// </param>
      /// <param name="fileName">
      /// The default file name when the user saves the PDF, or <c>null</c> to use the title.
      /// </param>
      public PdfViewerNavigationPayload(string title, Func<CancellationToken, Task<Stream>> loader,
         string? fileName = null) : base(null) {
         ArgumentException.ThrowIfNullOrWhiteSpace(title);
         ArgumentNullException.ThrowIfNull(loader);
         _title = title;
         Loader = loader;
         FileName = fileName;
      }

      /// <summary>Title of the viewer, which is also its entry's unique key across the application.</summary>
      public override string Title => _title;

      /// <summary>
      /// The function that fetches the PDF content. Its token is cancelled if the viewer is closed before the
      /// content has been fetched.
      /// </summary>
      public Func<CancellationToken, Task<Stream>> Loader { get; }

      /// <summary>The default file name when the PDF is saved, or <c>null</c> to follow the title.</summary>
      public string? FileName { get; }
   }
}
