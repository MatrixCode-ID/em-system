using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFiumCore;

namespace Em.Ui.Wpf.Shared.Pdf
{
   /// <summary>
   /// One PDF held open by PDFium, from bytes in memory. PDFium is not thread-safe - not even across
   /// documents - so every call into it, from every instance, goes through one global lock; callers may
   /// use an instance from any thread.
   /// </summary>
   internal sealed class PdfiumDocument : IDisposable
   {
      private const int BitmapFormatBgra = 4;
      private const int RenderAnnotations = 0x01;
      private const int RenderLcdText = 0x02;
      private const int RenderPrinting = 0x800;
      private const ulong FindMatchCase = 0x01;
      private const uint OpaqueWhite = 0xFFFFFFFF;

      // Page space is converted to top-left points through FPDF_PageToDevice at this many device units
      // per point, which keeps the rounding to whole device units well under a hundredth of a point.
      private const int PageToDevicePrecision = 100;

      private static readonly Lock Sync = new();
      private static bool _libraryReady;

      private readonly Size[] _pageSizes;
      private GCHandle _pin;
      private FpdfDocumentT? _document;

      private PdfiumDocument(GCHandle pin, FpdfDocumentT document, Size[] pageSizes) {
         _pin = pin;
         _document = document;
         _pageSizes = pageSizes;
      }

      /// <summary>Number of pages in the document.</summary>
      public int PageCount => _pageSizes.Length;

      /// <summary>Size of page <paramref name="index"/> in points (1/72 inch), rotation applied.</summary>
      public Size GetPageSize(int index) => _pageSizes[index];

      /// <summary>
      /// Opens <paramref name="data"/>. The array is pinned for as long as the document is open, since
      /// PDFium reads from it lazily instead of copying it.
      /// </summary>
      /// <exception cref="InvalidDataException">The bytes are not a PDF PDFium can open.</exception>
      public static PdfiumDocument Open(byte[] data) {
         ArgumentNullException.ThrowIfNull(data);

         lock (Sync) {
            if (!_libraryReady) {
               fpdfview.FPDF_InitLibrary();
               _libraryReady = true;
            }

            var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            var document = fpdfview.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (ulong)data.LongLength, null);
            if (IsNull(document)) {
               var error = fpdfview.FPDF_GetLastError();
               pin.Free();
               throw new InvalidDataException(DescribeLoadError(error));
            }

            var count = fpdfview.FPDF_GetPageCount(document);
            var sizes = new Size[count];
            for (var i = 0; i < count; i++) {
               double width = 0, height = 0;
               fpdfview.FPDF_GetPageSizeByIndex(document, i, ref width, ref height);
               sizes[i] = new Size(width, height);
            }

            return new PdfiumDocument(pin, document, sizes);
         }
      }

      /// <summary>
      /// Renders page <paramref name="index"/> at <paramref name="scale"/> device pixels per point on a
      /// white background. The result is frozen, so it can be handed to the UI thread as it is.
      /// </summary>
      /// <param name="index">Zero-based page index.</param>
      /// <param name="scale">Pixels per point: 96/72 draws the page at its real size on a 96 DPI screen.</param>
      /// <param name="forPrinting">Render for a printer: no LCD sub-pixel text, printing-only annotations shown.</param>
      public BitmapSource RenderPage(int index, double scale, bool forPrinting = false) {
         var size = _pageSizes[index];
         var width = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
         var height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
         var flags = RenderAnnotations | (forPrinting ? RenderPrinting : RenderLcdText);

         lock (Sync) {
            var document = RequireOpen();
            var page = fpdfview.FPDF_LoadPage(document, index);
            if (IsNull(page)) throw new InvalidDataException($"Page {index + 1} of the document cannot be read.");

            var bitmap = fpdfview.FPDFBitmapCreateEx(width, height, BitmapFormatBgra, IntPtr.Zero, 0);
            try {
               if (IsNull(bitmap)) throw new OutOfMemoryException($"No memory for a {width}x{height} page bitmap.");

               fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, OpaqueWhite);
               fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, flags);

               var stride = fpdfview.FPDFBitmapGetStride(bitmap);
               var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                  fpdfview.FPDFBitmapGetBuffer(bitmap), stride * height, stride);
               source.Freeze();
               return source;
            }
            finally {
               if (!IsNull(bitmap)) fpdfview.FPDFBitmapDestroy(bitmap);
               fpdfview.FPDF_ClosePage(page);
            }
         }
      }

      /// <summary>
      /// Finds every occurrence of <paramref name="text"/>, page by page. Each hit carries the boxes that
      /// cover it - more than one when it wraps onto the next line - in points from the top-left corner
      /// of its page.
      /// </summary>
      public IReadOnlyList<PdfSearchHit> Search(string text, bool matchCase, CancellationToken token) {
         ArgumentException.ThrowIfNullOrEmpty(text);

         var hits = new List<PdfSearchHit>();
         var query = new ushort[text.Length + 1];
         for (var i = 0; i < text.Length; i++) query[i] = text[i];

         for (var index = 0; index < _pageSizes.Length; index++) {
            token.ThrowIfCancellationRequested();

            // One page per lock, so a long search never holds up the pages being rendered on screen.
            lock (Sync) {
               var document = RequireOpen();
               var page = fpdfview.FPDF_LoadPage(document, index);
               if (IsNull(page)) continue;

               var textPage = fpdf_text.FPDFTextLoadPage(page);
               try {
                  if (IsNull(textPage)) continue;
                  CollectHits(page, textPage, index, query, matchCase, hits);
               }
               finally {
                  if (!IsNull(textPage)) fpdf_text.FPDFTextClosePage(textPage);
                  fpdfview.FPDF_ClosePage(page);
               }
            }
         }

         return hits;
      }

      private void CollectHits(FpdfPageT page, FpdfTextpageT textPage, int index, ushort[] query, bool matchCase,
         List<PdfSearchHit> hits) {
         var size = _pageSizes[index];
         var deviceWidth = (int)Math.Ceiling(size.Width * PageToDevicePrecision);
         var deviceHeight = (int)Math.Ceiling(size.Height * PageToDevicePrecision);

         var find = fpdf_text.FPDFTextFindStart(textPage, ref query[0], matchCase ? FindMatchCase : 0, 0);
         if (IsNull(find)) return;

         try {
            while (fpdf_text.FPDFTextFindNext(find) != 0) {
               var start = fpdf_text.FPDFTextGetSchResultIndex(find);
               var count = fpdf_text.FPDFTextGetSchCount(find);
               var rectCount = fpdf_text.FPDFTextCountRects(textPage, start, count);

               var boxes = new List<Rect>(rectCount);
               for (var r = 0; r < rectCount; r++) {
                  double left = 0, top = 0, right = 0, bottom = 0;
                  if (fpdf_text.FPDFTextGetRect(textPage, r, ref left, ref top, ref right, ref bottom) == 0) continue;

                  // Page space runs bottom-up and ignores /Rotate; going through the device mapping gives
                  // top-left coordinates on the page as it is shown.
                  var a = ToTopLeft(page, deviceWidth, deviceHeight, left, top);
                  var b = ToTopLeft(page, deviceWidth, deviceHeight, right, bottom);
                  boxes.Add(new Rect(a, b));
               }

               if (boxes.Count > 0) hits.Add(new PdfSearchHit(index, boxes));
            }
         }
         finally {
            fpdf_text.FPDFTextFindClose(find);
         }
      }

      private static Point ToTopLeft(FpdfPageT page, int deviceWidth, int deviceHeight, double x, double y) {
         int deviceX = 0, deviceY = 0;
         fpdfview.FPDF_PageToDevice(page, 0, 0, deviceWidth, deviceHeight, 0, x, y, ref deviceX, ref deviceY);
         return new Point((double)deviceX / PageToDevicePrecision, (double)deviceY / PageToDevicePrecision);
      }

      private FpdfDocumentT RequireOpen() =>
         _document ?? throw new ObjectDisposedException(nameof(PdfiumDocument));

      private static bool IsNull(FpdfDocumentT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
      private static bool IsNull(FpdfPageT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
      private static bool IsNull(FpdfBitmapT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
      private static bool IsNull(FpdfTextpageT? handle) => handle is null || handle.__Instance == IntPtr.Zero;
      private static bool IsNull(FpdfSchhandleT? handle) => handle is null || handle.__Instance == IntPtr.Zero;

      private static string DescribeLoadError(ulong error) => error switch {
         2 => "The PDF could not be read.",
         3 => "The content is not a valid PDF document.",
         4 => "The PDF is protected with a password.",
         5 => "The PDF uses a security scheme that is not supported.",
         _ => $"The PDF could not be opened (PDFium error {error})."
      };

      /// <summary>Closes the document and releases the pinned bytes. Waits for a render in progress.</summary>
      public void Dispose() {
         lock (Sync) {
            if (_document is null) return;

            fpdfview.FPDF_CloseDocument(_document);
            _document = null;
            if (_pin.IsAllocated) _pin.Free();
         }
      }
   }

   /// <summary>
   /// One occurrence of a searched text: its page, and the boxes covering it in points from the
   /// top-left corner of that page.
   /// </summary>
   internal sealed record PdfSearchHit(int PageIndex, IReadOnlyList<Rect> Boxes);
}
