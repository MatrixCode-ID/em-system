using System.IO;
using System.Windows.Threading;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.IO;
using Em.Controls.Sheet.Model;
using Em.Controls.Write.IO;
using Em.Controls.Write.Model;
using Em.Editing;

namespace Em.Ui.Wpf.Controls.Tests
{
   /// <summary>Checks how the built-in viewers choose a format and how the document viewer shows downloaded content read-only.</summary>
   public class DocumentViewerTests
   {
      [Theory]
      [InlineData("quote.pdf", DocumentViewKind.Pdf)]
      [InlineData("Book.XLSX", DocumentViewKind.Sheet)]
      [InlineData(@"C:\exports\items.csv", DocumentViewKind.Sheet)]
      [InlineData("letter.docx", DocumentViewKind.Writer)]
      [InlineData(".docx", DocumentViewKind.Writer)]
      [InlineData("legacy.doc", DocumentViewKind.None)]
      [InlineData("photo.png", DocumentViewKind.None)]
      [InlineData("", DocumentViewKind.None)]
      [InlineData(null, DocumentViewKind.None)]
      public void Kind_follows_the_formats_of_the_editors(string? fileName, DocumentViewKind expected) {
         Assert.Equal(expected, DocumentViewers.KindOf(fileName));
         Assert.Equal(expected != DocumentViewKind.None, DocumentViewers.CanView(fileName));
      }

      [Fact]
      public void Supported_extensions_cover_pdf_and_both_editors() {
         Assert.Contains(".pdf", DocumentViewers.SupportedExtensions);
         Assert.All(MatrixSheet.SupportedExtensions, extension => Assert.Contains(extension, DocumentViewers.SupportedExtensions));
         Assert.All(MatrixWriter.SupportedExtensions, extension => Assert.Contains(extension, DocumentViewers.SupportedExtensions));
      }

      [Fact]
      public void Payload_accepts_only_editor_formats() {
         static Task<Stream> Empty(CancellationToken _) => Task.FromResult<Stream>(new MemoryStream());
         Assert.Equal(DocumentViewKind.Writer, new DocumentViewerNavigationPayload("SO 1: letter.docx", Empty, "letter.docx").Kind);
         Assert.Throws<NotSupportedException>(() => new DocumentViewerNavigationPayload("PDF", Empty, "quote.pdf"));
         Assert.Throws<NotSupportedException>(() => new DocumentViewerNavigationPayload("Image", Empty, "photo.png"));
      }

      [Fact]
      public void Viewer_shows_a_workbook_read_only_and_keeps_the_original_bytes() {
         DocumentsBoundaryTests.RunOnSta(() => {
            EnsurePackScheme();
            var book = new SheetWorkbook();
            var cell = new CellAddress(0, 0);
            book.Sheets[0].Set(cell, SheetCell.FromInput("Size 42"));
            using var buffer = new MemoryStream();
            SheetFiles.SaveXlsx(book, buffer);
            var bytes = buffer.ToArray();
            int loads = 0;

            var viewer = new DocumentViewer();
            viewer.Vm.Payload = new DocumentViewerNavigationPayload("SO 1: book.xlsx", _ => {
               loads++;
               return Task.FromResult<Stream>(new MemoryStream(bytes));
            }, "book.xlsx");
            PumpUntil(() => viewer.Vm.HasDocument || viewer.Vm.HasError);

            var sheet = Assert.IsType<MatrixSheet>(viewer.Editor);
            Assert.True(viewer.Vm.HasDocument, viewer.Vm.ErrorMessage);
            Assert.Equal(EditorMode.Viewer, sheet.Mode);
            Assert.False(sheet.AllowOpen);
            Assert.Equal("Size 42", sheet.Session.Workbook.Sheets[0][cell].Value.ToString());
            Assert.False(sheet.Session.SetCell(cell, "x").Accepted);
            Assert.True(viewer.Vm.SaveAsCommandAllowed());
            Assert.Equal("book.xlsx", viewer.Vm.FileName);
            Assert.True(viewer.Vm.IsSheet);

            viewer.Vm.RefreshCommand();
            PumpUntil(() => loads == 2 && !viewer.Vm.InWaiting);
            Assert.Same(sheet, viewer.Editor);
            viewer.OnRelease(null!);
            Assert.Null(viewer.Editor);
         });
      }

      [Fact]
      public void Viewer_shows_a_document_and_reports_broken_files() {
         DocumentsBoundaryTests.RunOnSta(() => {
            EnsurePackScheme();
            using var buffer = new MemoryStream();
            WriteFiles.SaveDocx(WriteDocument.Create("Delivery terms"), buffer);
            var good = buffer.ToArray();

            var viewer = new DocumentViewer();
            viewer.Vm.Payload = new DocumentViewerNavigationPayload("SO 1: terms.docx",
               _ => Task.FromResult<Stream>(new MemoryStream(good)), "terms.docx");
            PumpUntil(() => viewer.Vm.HasDocument || viewer.Vm.HasError);
            var writer = Assert.IsType<MatrixWriter>(viewer.Editor);
            Assert.Equal(EditorMode.Viewer, writer.Mode);
            Assert.True(viewer.Vm.IsWriter);

            var broken = new DocumentViewer();
            broken.Vm.ShowErrorDetails = false;
            broken.Vm.Payload = new DocumentViewerNavigationPayload("SO 1: broken.docx",
               _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])), "broken.docx");
            PumpUntil(() => broken.Vm.HasDocument || broken.Vm.HasError);
            Assert.True(broken.Vm.HasError);
            Assert.False(string.IsNullOrEmpty(broken.Vm.ErrorMessage));
         });
      }

      private static void PumpUntil(Func<bool> done) {
         var limit = DateTime.UtcNow.AddSeconds(20);
         while (!done()) {
            Assert.True(DateTime.UtcNow < limit, "The viewer did not finish in time.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
         }
      }

      private static void EnsurePackScheme() {
         // Continuations after awaits must return to this STA thread, as they do on the UI thread of an application.
         SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
         _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
         if (System.Windows.Application.ResourceAssembly is null) {
            System.Windows.Application.ResourceAssembly = typeof(DocumentViewerTests).Assembly;
         }
      }
   }
}
