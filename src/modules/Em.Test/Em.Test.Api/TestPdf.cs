using System.Globalization;
using System.Text;
using Em.Test.Models;

namespace Em.Test.Api
{
   /// <summary>
   /// Sample PDF builder. Written directly as PDF text with the standard Helvetica font, so it does not
   /// depend on fonts installed on the server and does not collide with the approval engine's font setup.
   /// </summary>
   internal static class TestPdf
   {
      private const double PageWidth = 595.28;
      private const double PageHeight = 841.89;
      private const double Pt = 72.0 / 25.4;

      /// <summary>A sample PDF with <paramref name="pages"/> pages, each with a title and a frame.</summary>
      public static Stream Sample(int pages) {
         var writer = new PdfWriter();
         for (var i = 1; i <= pages; i++) {
            var page = writer.NewPage();
            Text(page, 20, 25, 20, true, "Em Test - Sample PDF");
            Text(page, 20, 38, 11, false, $"Page {i} of {pages}");
            Text(page, 20, 48, 10, false, $"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC by the Em.Test module.");
            Rect(page, 15, 15, 180, 267);
            for (var line = 0; line < 20; line++) {
               Text(page, 20, 70 + line * 8, 9, false,
                  $"Line {line + 1,2}: the quick brown fox jumps over the lazy dog 0123456789");
            }
         }

         return writer.Build();
      }

      /// <summary>Module-owned printable document, independent of the approval engine.</summary>
      public static Stream Document(ta_TestDoc doc) {
         var writer = new PdfWriter(); var page = writer.NewPage();
         Text(page, 20, 25, 20, true, "Test document");
         Text(page, 20, 40, 11, false, doc.cTestDocNo);
         Text(page, 20, 50, 11, false, doc.cTestDocTitle);
         Text(page, 20, 60, 11, false, doc.cTestDocAmount.ToString("N2", CultureInfo.InvariantCulture));
         return writer.Build();
      }

      private static void Rect(StringBuilder page, double x, double y, double width, double height) {
         page.Append(CultureInfo.InvariantCulture,
            $"0.5 w {x * Pt:0.##} {PageHeight - (y + height) * Pt:0.##} {width * Pt:0.##} {height * Pt:0.##} re S\n");
      }

      private static void Text(StringBuilder page, double x, double y, double size, bool bold, string text) {
         page.Append(CultureInfo.InvariantCulture,
            $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x * Pt:0.##} {PageHeight - y * Pt:0.##} Td ({Escape(text)}) Tj ET\n");
      }

      // Plain ASCII only: anything else becomes '?', which keeps the stream length equal to its character
      // count and the file readable without declaring an encoding.
      private static string Escape(string text) {
         var sb = new StringBuilder(text.Length);
         foreach (var c in text) {
            if (c is '(' or ')' or '\\') sb.Append('\\').Append(c);
            else if (c is >= ' ' and <= '~') sb.Append(c);
            else sb.Append('?');
         }

         return sb.ToString();
      }

      private sealed class PdfWriter
      {
         private readonly List<StringBuilder> _pages = [];

         public StringBuilder NewPage() {
            var page = new StringBuilder();
            _pages.Add(page);
            return page;
         }

         public MemoryStream Build() {
            var stream = new MemoryStream();
            var offsets = new List<long>();

            void Raw(string text) => stream.Write(Encoding.Latin1.GetBytes(text));

            void Obj(string body) {
               offsets.Add(stream.Position);
               Raw($"{offsets.Count} 0 obj\n{body}\nendobj\n");
            }

            Raw("%PDF-1.4\n");
            Obj("<< /Type /Catalog /Pages 2 0 R >>");
            var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(i => $"{5 + 2 * i} 0 R"));
            Obj($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
            Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

            for (var i = 0; i < _pages.Count; i++) {
               Obj(string.Create(CultureInfo.InvariantCulture,
                  $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth} {PageHeight}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {6 + 2 * i} 0 R >>"));
               var content = _pages[i].ToString();
               Obj($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
            }

            var xref = stream.Position;
            Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
            foreach (var offset in offsets) Raw($"{offset:D10} 00000 n \n");
            Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

            stream.Position = 0;
            return stream;
         }
      }
   }
}
