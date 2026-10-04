using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Em.Ui.Wpf.Shared.Pdf
{
   /// <summary>
   /// Prints a <see cref="PdfiumDocument"/> to the printer chosen in a <see cref="PrintDialog"/>, one page
   /// rendered at a time at the printer's resolution.
   /// </summary>
   internal static class PdfPrinting
   {
      private const double MinDpi = 150;
      private const double MaxDpi = 600;

      /// <summary>
      /// Sends pages <paramref name="first"/> to <paramref name="last"/> (zero-based, inclusive) to the
      /// printer. Writing is asynchronous: pages are rendered on the UI thread one by one as the spooler
      /// asks for them, so the screen keeps responding between pages.
      /// </summary>
      public static Task PrintAsync(PdfiumDocument document, int first, int last, PrintDialog dialog, string jobName) {
         var queue = dialog.PrintQueue;
         var ticket = dialog.PrintTicket;
         var landscape = ticket.PageOrientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape;

         var mediaWidth = ticket.PageMediaSize?.Width ?? dialog.PrintableAreaWidth;
         var mediaHeight = ticket.PageMediaSize?.Height ?? dialog.PrintableAreaHeight;
         var paper = landscape ? new Size(mediaHeight, mediaWidth) : new Size(mediaWidth, mediaHeight);

         var area = queue.GetPrintCapabilities(ticket).PageImageableArea;
         var printable = area is null
            ? new Rect(paper)
            : new Rect(area.OriginWidth, area.OriginHeight, area.ExtentWidth, area.ExtentHeight);
         if (landscape && area != null && printable.Width < printable.Height)
            printable = new Rect(printable.Y, printable.X, printable.Height, printable.Width);

         var dpi = Math.Clamp((double)(ticket.PageResolution?.X ?? 300), MinDpi, MaxDpi);
         var paginator = new PdfPrintPaginator(document, first, last, paper, printable, dpi);

         queue.CurrentJobSettings.Description = jobName;
         var writer = PrintQueue.CreateXpsDocumentWriter(queue);
         var done = new TaskCompletionSource();
         writer.WritingCompleted += (_, e) => {
            if (e.Error != null) done.TrySetException(e.Error);
            else if (e.Cancelled) done.TrySetCanceled();
            else done.TrySetResult();
         };
         writer.WriteAsync(paginator, ticket);
         return done.Task;
      }

      private sealed class PdfPrintPaginator(
         PdfiumDocument document, int first, int last, Size paper, Rect printable, double dpi) : DocumentPaginator
      {
         public override bool IsPageCountValid => true;
         public override int PageCount => last - first + 1;
         public override Size PageSize { get; set; } = paper;
         public override IDocumentPaginatorSource? Source => null;

         public override DocumentPage GetPage(int pageNumber) {
            var index = first + pageNumber;
            var points = document.GetPageSize(index);
            var bitmap = document.RenderPage(index, dpi / 72, forPrinting: true);

            // A page whose orientation differs from the paper's is turned a quarter, so it is printed as
            // large as the paper allows instead of shrunk into it.
            var rotate = points.Width > points.Height != printable.Width > printable.Height;
            var natural = new Size(points.Width * 96 / 72, points.Height * 96 / 72);
            var turned = rotate ? new Size(natural.Height, natural.Width) : natural;

            // Real size when it fits, otherwise shrunk to the printable area - never enlarged.
            var scale = Math.Min(1, Math.Min(printable.Width / turned.Width, printable.Height / turned.Height));
            var drawn = new Size(turned.Width * scale, turned.Height * scale);
            var centre = new Point(printable.X + printable.Width / 2, printable.Y + printable.Height / 2);

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) {
               if (rotate) context.PushTransform(new RotateTransform(90, centre.X, centre.Y));

               var width = rotate ? drawn.Height : drawn.Width;
               var height = rotate ? drawn.Width : drawn.Height;
               context.DrawImage(bitmap, new Rect(centre.X - width / 2, centre.Y - height / 2, width, height));

               if (rotate) context.Pop();
            }

            return new DocumentPage(visual, PageSize, new Rect(PageSize), printable);
         }
      }
   }
}
