using System.Globalization;
using Em.Api.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Em.Api.Core.Approval.Pdf;

/// <summary>Menempelkan keputusan dan isian approval pada PDF dasar serta menyediakan PDF pengukuran slot.</summary>
/// <remarks>
/// Stream dasar tetap milik pemanggil. Slot pada halaman yang tidak tersedia dilewati.
/// Kode tepi mendapat pita tambahan di kanan halaman agar tidak menutupi dokumen.
/// </remarks>
public sealed class ApprovalPdfRenderer : IApprovalPdfRenderer
{
   private static readonly object FontLock = new();
   private static readonly XBrush ApprovedBrush = new XSolidBrush(XColor.FromArgb(20, 115, 55));
   private static readonly XBrush RejectedBrush = new XSolidBrush(XColor.FromArgb(190, 25, 35));
   private const double Padding = 2;

   /// <summary>Menggambar keputusan yang sudah diambil, isian, kode tepi, dan lembar pengesahan opsional.</summary>
   /// <param name="basePdf">PDF dasar yang dibaca mulai dari posisi stream saat ini tanpa menutupnya.</param>
   /// <param name="snapshot">Nilai siap cetak untuk keputusan dan isian.</param>
   /// <param name="cancellationToken">Token pembatalan pembacaan dan penggambaran.</param>
   /// <returns>Stream PDF hasil dengan posisi di awal; pemanggil menutup stream hasil.</returns>
   public async Task<Stream> RenderStampedAsync(Stream basePdf, ApprovalStampSnapshot snapshot,
      CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(snapshot);
      using var buffer = await ReadBaseAsync(basePdf, cancellationToken);
      using var document = PdfReader.Open(buffer, PdfDocumentOpenMode.Modify);
      EnsureFonts();
      var basePages = document.PageCount;
      foreach (var step in snapshot.Steps) {
         cancellationToken.ThrowIfCancellationRequested();
         if (step.Status is not (ApprovalStepStatus.Approved or ApprovalStepStatus.Rejected)) continue;
         if (!TrySlot(document, basePages, step.Slot, out var page, out var rect)) continue;
         using var graphics = SlotGraphics(page);
         var status = StatusText(step.Status);
         if (step.SignerRole == ApprovalSignerRole.Override) status += " / OVERRIDE";
         var rows = new List<string> { status, $"Signed by: {step.SignerName}" };
         if (step.SignerRole != ApprovalSignerRole.Assigned) rows.Add(OnBehalf(step));
         rows.Add(DateText(step.SignedDate));
         rows.Add($"Verification: {step.VerificationCode}");
         DrawRows(graphics, rect, rows, step.Status == ApprovalStepStatus.Approved ? ApprovedBrush : RejectedBrush);
      }
      foreach (var input in snapshot.Inputs) {
         cancellationToken.ThrowIfCancellationRequested();
         if (input.Kind == ApprovalInputFieldKind.Check && !input.Checked) continue;
         if (!TrySlot(document, basePages, input.Slot, out var page, out var rect)) continue;
         using var graphics = SlotGraphics(page);
         if (input.Kind == ApprovalInputFieldKind.Check) {
            var size = Math.Min(rect.Width, rect.Height) * .7;
            var x = rect.X + (rect.Width - size) / 2;
            var y = rect.Y + (rect.Height - size) / 2;
            var state = graphics.Save();
            graphics.IntersectClip(rect);
            var pen = new XPen(XColors.Black, Math.Max(.7, size / 9));
            graphics.DrawLines(pen, [new(x, y + size * .5), new(x + size * .35, y + size), new(x + size, y)]);
            graphics.Restore(state);
         }
         else if (input.Kind == ApprovalInputFieldKind.Text)
            DrawText(graphics, Inset(rect), input.Text ?? "", XBrushes.Black);
      }
      if (snapshot.IncludeApprovalSheet) DrawApprovalSheet(document, snapshot, cancellationToken);
      if (!string.IsNullOrWhiteSpace(snapshot.PageMarginCode))
         foreach (var page in document.Pages) {
            cancellationToken.ThrowIfCancellationRequested();
            DrawMarginCode(page, snapshot.PageMarginCode);
         }
      return Save(document, cancellationToken);
   }

   /// <summary>Menggambar kotak tanda tangan dan isian dengan label serta koordinat milimeter.</summary>
   /// <param name="basePdf">PDF dasar yang dibaca dari posisi stream saat ini tanpa menutupnya.</param>
   /// <param name="slots">Slot yang diukur; halaman yang tidak tersedia dilewati.</param>
   /// <param name="cancellationToken">Token pembatalan.</param>
   /// <returns>Stream PDF pengukuran dengan posisi di awal.</returns>
   public async Task<Stream> RenderCalibrationAsync(Stream basePdf, IReadOnlyList<ApprovalSlotLabel> slots,
      CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(slots);
      using var buffer = await ReadBaseAsync(basePdf, cancellationToken);
      using var document = PdfReader.Open(buffer, PdfDocumentOpenMode.Modify);
      EnsureFonts();
      foreach (var label in slots) {
         cancellationToken.ThrowIfCancellationRequested();
         if (!TrySlot(document, document.PageCount, label.Slot, out var page, out var rect)) continue;
         using var graphics = SlotGraphics(page);
         var input = label.Kind == ApprovalSlotKind.Input;
         var color = input ? XColors.DarkOrange : XColors.RoyalBlue;
         var pen = new XPen(color, .8) { DashStyle = input ? XDashStyle.Dash : XDashStyle.Solid };
         graphics.DrawRectangle(pen, rect);
         var slot = label.Slot;
         DrawText(graphics, Inset(rect), FormattableString.Invariant(
            $"{(input ? "INPUT" : "SIGNATURE")}: {label.Label}\nPage {slot.Page}; x={slot.X:0.##}, y={slot.Y:0.##} mm\nw={slot.Width:0.##}, h={slot.Height:0.##} mm"), new XSolidBrush(color), 9);
      }
      return Save(document, cancellationToken);
   }

   private static async Task<MemoryStream> ReadBaseAsync(Stream source, CancellationToken token)
   {
      ArgumentNullException.ThrowIfNull(source);
      var buffer = new MemoryStream();
      try {
         await source.CopyToAsync(buffer, token);
         buffer.Position = 0;
         return buffer;
      }
      catch { buffer.Dispose(); throw; }
   }

   private static Stream Save(PdfDocument document, CancellationToken token)
   {
      token.ThrowIfCancellationRequested();
      var result = new MemoryStream();
      try {
         document.Save(result, false);
         token.ThrowIfCancellationRequested();
         result.Position = 0;
         return result;
      }
      catch { result.Dispose(); throw; }
   }

   private static void EnsureFonts()
   {
      lock (FontLock) {
         try {
            if (GlobalFontSettings.FontResolver == null) GlobalFontSettings.FontResolver = new ApprovalFontResolver();
            _ = Font(8);
            _ = Font(8, true);
         }
         catch (Exception error) {
            throw new InvalidOperationException(
               "Approval PDF fonts could not be loaded. Install Arial regular and bold in the Windows Fonts folder, " +
               "or configure a PDFsharp font resolver for Arial before creating any PDF fonts.", error);
         }
      }
   }

   private static XFont Font(double size, bool bold = false) =>
      new("Arial", size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

   private static bool TrySlot(PdfDocument document, int basePages, ApprovalSlot slot,
      out PdfPage page, out XRect rect)
   {
      page = null!;
      rect = default;
      if (slot.Page < 1 || slot.Page > basePages) return false;
      if (!double.IsFinite(slot.X) || !double.IsFinite(slot.Y) || !double.IsFinite(slot.Width) ||
          !double.IsFinite(slot.Height) || slot.X < 0 || slot.Y < 0 || slot.Width <= 0 || slot.Height <= 0)
         throw new ArgumentException("Approval PDF slots must have finite, nonnegative coordinates and positive dimensions.");
      page = document.Pages[slot.Page - 1];
      rect = new XRect(Mm(slot.X), Mm(slot.Y), Mm(slot.Width), Mm(slot.Height));
      var rotated = Rotation(page) is 90 or 270;
      var width = rotated ? page.Height.Point : page.Width.Point;
      var height = rotated ? page.Width.Point : page.Height.Point;
      if (rect.Right > width + .01 || rect.Bottom > height + .01)
         throw new ArgumentException($"Approval PDF slot on page {slot.Page} extends beyond the page.");
      return true;
   }

   private static int Rotation(PdfPage page) => (page.Rotate % 360 + 360) % 360;

   private static XGraphics SlotGraphics(PdfPage page)
   {
      var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
      switch (Rotation(page)) {
         case 90:
            graphics.TranslateTransform(0, page.Height.Point);
            graphics.RotateTransform(-90);
            break;
         case 180:
            graphics.TranslateTransform(page.Width.Point, page.Height.Point);
            graphics.RotateTransform(180);
            break;
         case 270:
            graphics.TranslateTransform(page.Width.Point, 0);
            graphics.RotateTransform(90);
            break;
      }
      return graphics;
   }

   private static double Mm(double value) => XUnit.FromMillimeter(value).Point;
   private static XRect Inset(XRect rect) => new(rect.X + Padding, rect.Y + Padding,
      Math.Max(.1, rect.Width - Padding * 2), Math.Max(.1, rect.Height - Padding * 2));

   private static void DrawRows(XGraphics graphics, XRect rect, IReadOnlyList<string> rows, XBrush statusBrush)
   {
      rect = Inset(rect);
      var height = rect.Height / rows.Count;
      for (var i = 0; i < rows.Count; i++)
         DrawText(graphics, new XRect(rect.X, rect.Y + i * height, rect.Width, height), rows[i],
            i == 0 ? statusBrush : XBrushes.Black, Math.Min(i == 0 ? 9 : 8, height / 1.2), i == 0);
   }

   private static void DrawText(XGraphics graphics, XRect rect, string text, XBrush brush,
      double preferredSize = 10, bool bold = false)
   {
      // Wrap first, then shrink to six points; overflow is explicitly marked and clipped to the slot.
      var size = Math.Max(6, preferredSize);
      List<string> lines;
      XFont font;
      while (true) {
         font = Font(size, bold);
         lines = Wrap(graphics, text, font, rect.Width);
         if (lines.Count * size * 1.2 <= rect.Height || size <= 6) break;
         size = Math.Max(6, size - .5);
      }
      var capacity = Math.Max(1, (int)(rect.Height / (size * 1.2)));
      if (lines.Count > capacity) {
         lines = lines.Take(capacity).ToList();
         lines[^1] = FitEllipsis(graphics, lines[^1], font, rect.Width);
      }
      var state = graphics.Save();
      graphics.IntersectClip(rect);
      for (var i = 0; i < lines.Count; i++)
         graphics.DrawString(lines[i], font, brush,
            new XRect(rect.X, rect.Y + i * size * 1.2, rect.Width, size * 1.2), XStringFormats.TopLeft);
      graphics.Restore(state);
   }

   private static List<string> Wrap(XGraphics graphics, string text, XFont font, double width)
   {
      var lines = new List<string>();
      foreach (var paragraph in text.Replace("\r", "").Split('\n')) {
         var line = "";
         foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (graphics.MeasureString(candidate, font).Width <= width) { line = candidate; continue; }
            if (line.Length != 0) { lines.Add(line); line = ""; }
            var elements = StringInfo.GetTextElementEnumerator(word);
            while (elements.MoveNext()) {
               var element = elements.GetTextElement();
               if (line.Length != 0 && graphics.MeasureString(line + element, font).Width > width) {
                  lines.Add(line);
                  line = "";
               }
               line += element;
            }
         }
         lines.Add(line);
      }
      return lines;
   }

   private static string FitEllipsis(XGraphics graphics, string text, XFont font, double width)
   {
      var elements = StringInfo.ParseCombiningCharacters(text);
      for (var count = elements.Length; count >= 0; count--) {
         var candidate = (count == elements.Length ? text : text[..elements[count]]) + "...";
         if (graphics.MeasureString(candidate, font).Width <= width) return candidate;
      }
      return "";
   }

   private static void DrawApprovalSheet(PdfDocument document, ApprovalStampSnapshot snapshot, CancellationToken token)
   {
      var page = document.AddPage();
      page.Width = XUnit.FromMillimeter(297);
      page.Height = XUnit.FromMillimeter(Math.Max(210, 55 + snapshot.Steps.Count * 20));
      page.CropBox = page.MediaBox;
      using var graphics = XGraphics.FromPdfPage(page);
      DrawText(graphics, new XRect(Mm(12), Mm(10), Mm(273), Mm(9)), "APPROVAL SHEET", XBrushes.Black, 16, true);
      DrawText(graphics, new XRect(Mm(12), Mm(22), Mm(273), Mm(14)), snapshot.DocumentTitle ?? "", XBrushes.Black, 12);
      double[] widths = [10, 52, 38, 90, 43, 40];
      string[] headers = ["#", "Step", "Status", "Signed by / on behalf of", "Date and time", "Verification code"];
      var y = Mm(40);
      DrawTableRow(graphics, y, Mm(10), widths, headers, true);
      y += Mm(10);
      for (var i = 0; i < snapshot.Steps.Count; i++) {
         token.ThrowIfCancellationRequested();
         var step = snapshot.Steps[i];
         var signer = step.SignerName ?? "";
         if (step.SignerRole != ApprovalSignerRole.Assigned) signer += "\n" + OnBehalf(step);
         var status = StatusText(step.Status);
         if (step.SignerRole == ApprovalSignerRole.Override) status += "\nOVERRIDE";
         DrawTableRow(graphics, y, Mm(20), widths,
            [(i + 1).ToString(CultureInfo.InvariantCulture), step.StepName, status, signer,
             DateText(step.SignedDate), step.VerificationCode ?? ""], false);
         y += Mm(20);
      }
   }

   private static void DrawTableRow(XGraphics graphics, double y, double height, double[] widths,
      string[] values, bool header)
   {
      var x = Mm(12);
      for (var i = 0; i < widths.Length; i++) {
         var rect = new XRect(x, y, Mm(widths[i]), height);
         if (header) graphics.DrawRectangle(XBrushes.WhiteSmoke, rect);
         graphics.DrawRectangle(new XPen(XColors.Gray, .4), rect);
         DrawText(graphics, Inset(rect), values[i], XBrushes.Black, 9, header);
         x += rect.Width;
      }
   }

   private static void DrawMarginCode(PdfPage page, string code)
   {
      // A new right-hand band avoids relying on a supposedly empty margin in the supplied document.
      var originalWidth = page.Width.Point;
      var crop = page.CropBox;
      page.Width = XUnit.FromPoint(originalWidth + Mm(8));
      page.CropBox = new PdfRectangle(new XPoint(crop.X1, crop.Y1), new XPoint(page.MediaBox.X2, crop.Y2));
      using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
      var state = graphics.Save();
      graphics.TranslateTransform(originalWidth + Mm(6), Mm(8));
      graphics.RotateTransform(90);
      DrawText(graphics, new XRect(0, 0, page.Height.Point - Mm(16), Mm(5)),
         "Verification: " + code, XBrushes.Black, 8);
      graphics.Restore(state);
   }

   private static string OnBehalf(ApprovalStampStep step) => "on behalf of: " +
      (string.IsNullOrWhiteSpace(step.OnBehalfName) ? step.StepName : step.OnBehalfName);
   private static string DateText(DateTime? date) => date?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";
   private static string StatusText(ApprovalStepStatus status) => status switch {
      ApprovalStepStatus.Approved => "APPROVED",
      ApprovalStepStatus.Rejected => "REJECTED",
      ApprovalStepStatus.Waiting => "WAITING",
      ApprovalStepStatus.Skipped => "SKIPPED",
      _ => throw new ArgumentOutOfRangeException(nameof(status))
   };
}

internal sealed class ApprovalFontResolver : IFontResolver
{
   /// <summary>Memilih berkas Arial biasa atau tebal untuk penggambaran approval.</summary>
   public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
      familyName.Equals("Arial", StringComparison.OrdinalIgnoreCase)
         ? new FontResolverInfo(bold ? "approval-arial-bold" : "approval-arial", false, italic) : null;

   /// <summary>Membaca font dari folder font Windows tanpa menyertakan font berlisensi di paket.</summary>
   public byte[] GetFont(string faceName)
   {
      if (!OperatingSystem.IsWindows())
         throw new InvalidOperationException("The default approval PDF font resolver requires Windows. Configure an Arial font resolver for this platform.");
      var file = faceName == "approval-arial-bold" ? "arialbd.ttf" : "arial.ttf";
      return File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file));
   }
}
