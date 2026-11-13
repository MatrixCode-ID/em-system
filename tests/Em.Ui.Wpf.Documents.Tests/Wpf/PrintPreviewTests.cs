using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Em.Documents.Layout;
using Em.Documents.Model;
using Em.Grid.Export;
using Em.Ui.Wpf.Documents.Printing;
using Xunit;

namespace Em.Ui.Wpf.Documents.Tests.Wpf;

[Collection("WPF desktop")]
public sealed class PrintPreviewTests
{
    [Fact]
    public async Task Preview_uses_the_same_page_count_and_glyph_positions_as_neutral_layout()
    {
        await StandaloneEditorsTests.OnDesktop(async () =>
        {
            var setup = new PageSetup(280, 320, 20, 20, 20, 20);
            Document Build(PageSetup page, bool fit, IProgress<ExportProgress>? progress) =>
                new("Café Ω", [new DocumentSection(page,
                    [new DocumentTable([new DocumentColumn("Order", 180)], Rows)])]) { FitToWidth = fit };
            var document = Build(setup, true, null);
            var plan = await new Paginator().PlanAsync(document);
            Assert.True(plan.PageCount > 1);
            var window = new PrintPreviewWindow(Build, GridDocument.CreateFormats(), setup, dark: true)
            { ShowInTaskbar = false };
            window.Show();
            try
            {
                await window.RefreshAsync(setup, true);
                Assert.Equal(plan.PageCount, window.PageCount);
                Assert.Equal(1, window.CurrentPage);
                var first = Assert.IsType<DocumentPage>(window.CurrentPageModel);
                var drawing = PagePresenter.CreateDrawing(first);
                var glyphs = drawing.Children.OfType<GlyphRunDrawing>().Select(draw => draw.GlyphRun).ToArray();
                var neutral = first.Commands.OfType<DrawGlyphs>().ToArray();
                Assert.Equal(neutral.Length, glyphs.Length);
                for (int index = 0; index < glyphs.Length; index++)
                {
                    Assert.Equal(new Point(neutral[index].X, neutral[index].Baseline), glyphs[index].BaselineOrigin);
                    Assert.Equal(neutral[index].Glyphs.Select(glyph => glyph.Id), glyphs[index].GlyphIndices);
                    Assert.Equal(neutral[index].Glyphs.Select(glyph => glyph.Advance), glyphs[index].AdvanceWidths);
                }
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(
                    Assert.IsType<GeometryDrawing>(drawing.Children[0]).Brush).Color);
                await window.ShowPageAsync(plan.PageCount);
                Assert.Equal(plan.PageCount, window.CurrentPage);
                Assert.Equal(["pdf", "xlsx", "docx", "csv"], window.ExportFormats.Select(format => format.Id));
                var print = new WpfDocumentPaginator(document, plan.PageCount, 2, plan.PageCount, new Size(400, 500));
                try
                {
                    Assert.Equal(plan.PageCount - 1, print.PageCount);
                    Assert.NotNull(print.GetPage(0).Visual);
                }
                finally { print.Close(); }
            }
            finally { window.Close(); }
        });
    }

    private static async IAsyncEnumerable<DocumentRow> Rows([EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        for (int index = 1; index <= 30; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return new DocumentRow([DocumentCell.Text($"Order {index:00} — café")]);
        }
    }
}
