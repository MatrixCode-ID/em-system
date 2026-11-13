using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using Em.Grid;
using Em.Grid.Data;
using Em.Documents.Layout;
using Em.Grid.Export;
using Em.Grid.Query;
using Xunit;

namespace Em.Libs.Tests.Documents;

public sealed class GridExportTests
{
    [Fact]
    public async Task Selected_csv_uses_visible_order_filter_and_cancellation()
    {
        var rows = new[]
        {
            new GridRow(new RowKey("a"), Values("Blue", 4)),
            new GridRow(new RowKey("b"), Values("Orchid", 2)),
            new GridRow(new RowKey("c"), Values("Cedar", 6))
        };
        var provider = new InMemoryGridDataProvider(rows);
        var snapshot = new GridExportSnapshot([
            new("Count", "Units", GridValueKind.Number), new("Name", "Customer", GridValueKind.Text),
            new("Private", "Internal", GridValueKind.Text) { IsVisible = false }
        ], new GridQuery
        {
            Filter = new FilterCondition("Count", FilterOperator.GreaterOrEqual, GridValue.From(4)),
            Sort = [new("Count", true)]
        }, rows.Select(row => row.Key).ToImmutableHashSet());
        var document = GridDocument.Create(provider, snapshot, new GridExportOptions
        {
            SelectedOnly = true, Culture = CultureInfo.InvariantCulture, Values = ExportValueMode.Raw
        });
        var registry = GridDocument.CreateFormats(';');
        using var output = new MemoryStream();
        await registry.Get("csv").WriteAsync(document, output);
        string csv = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("Units;Customer\r\n6;Cedar\r\n4;Blue\r\n", csv);
        Assert.DoesNotContain("Orchid", csv);
        Assert.DoesNotContain("Internal", csv);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var incomplete = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.Get("xlsx").WriteAsync(document, incomplete, cancelled.Token).AsTask());
    }

    [Theory]
    [InlineData(ExportValueMode.FormattedTyped, 2)]
    [InlineData(ExportValueMode.DisplayText, 2)]
    [InlineData(ExportValueMode.Raw, 0)]
    public async Task Print_layout_draws_checked_values_without_a_font_glyph(ExportValueMode mode, int expectedStrokes)
    {
        var rows = new[]
        {
            new GridRow(new RowKey("yes"), ImmutableDictionary<string, GridValue>.Empty.Add("Flag", GridValue.From(true))),
            new GridRow(new RowKey("no"), ImmutableDictionary<string, GridValue>.Empty.Add("Flag", GridValue.From(false))),
            new GridRow(new RowKey("unset"), ImmutableDictionary<string, GridValue>.Empty.Add("Flag", default))
        };
        var snapshot = new GridExportSnapshot([new("Flag", "Priority", GridValueKind.Boolean)], GridQuery.Empty, []);
        var document = GridDocument.Create(new InMemoryGridDataProvider(rows), snapshot, new GridExportOptions { Values = mode });
        var pages = new List<DocumentPage>();
        await foreach (var page in new Paginator().PagesAsync(document, 1))
            pages.Add(page);

        var commands = Assert.Single(pages).Commands;
        Assert.Equal(expectedStrokes, commands.OfType<DrawLine>().Count(line => line.Y1 != line.Y2));
        Assert.DoesNotContain(commands.OfType<DrawGlyphs>().SelectMany(run => run.Glyphs), glyph => glyph.Unicode == '✓');
    }

    private static ImmutableDictionary<string, GridValue> Values(string customer, int units) =>
        ImmutableDictionary<string, GridValue>.Empty.Add("Name", GridValue.From(customer)).Add("Count", GridValue.From(units));
}
