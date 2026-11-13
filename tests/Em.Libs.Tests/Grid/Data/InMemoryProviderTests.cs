using System.Collections.Immutable;
using System.Text.Json;
using Em.Grid;
using Em.Grid.Data;
using Em.Grid.Query;
using Xunit;

namespace Em.Libs.Tests.Grid.Data;

public sealed class InMemoryProviderTests
{
    [Fact]
    public async Task Query_round_trip_preserves_filter_stable_sort_and_count_before_paging()
    {
        var provider = new InMemoryGridDataProvider([Row("c", 4), Row("a", 2), Row("b", 4), Row("d", 5)]);
        var query = new GridQuery
        {
            Filter = new AllFilters([new FilterCondition("units", FilterOperator.GreaterOrEqual, GridValue.From(4))]),
            Sort = [new("units")]
        };

        var restored = JsonSerializer.Deserialize<GridQuery>(JsonSerializer.Serialize(query))!;
        var page = await provider.FetchAsync(restored, new RowRange(0, 2));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(["c", "b"], page.Rows.Select(row => row.Key.Value));
        Assert.Equal<GridValue>([GridValue.From(4), GridValue.From(5)],
            await provider.GetDistinctValuesAsync(restored, "units", new RowRange(0, 4)));
    }

    [Fact]
    public async Task Stale_edit_cannot_overwrite_newer_value()
    {
        var provider = new InMemoryGridDataProvider([Row("a", 2)]);
        var address = new CellAddress(new("a"), "units");
        var edit = new GridEditSession(address, GridValue.From(2));
        Assert.Equal(CellUpdateResult.Updated, await provider.UpdateAsync(address, GridValue.From(2), GridValue.From(3)));
        Assert.Equal(CellUpdateResult.Conflict, await edit.CommitAsync(provider, GridValue.From(8)));
        Assert.Equal(EditStatus.Active, edit.Status);
        edit.Cancel();
        Assert.Equal(EditStatus.Canceled, edit.Status);
        Assert.Equal(GridValue.From(3), (await provider.FetchAsync(GridQuery.Empty, new RowRange(0, 1))).Rows[0].Values["units"]);
    }

    [Fact]
    public async Task Provider_observes_cancellation_even_when_empty()
    {
        var provider = new InMemoryGridDataProvider([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.FetchAsync(GridQuery.Empty, new RowRange(0, 5), new CancellationToken(canceled: true)));
    }

    private static GridRow Row(string key, int units) =>
        new(new RowKey(key), ImmutableDictionary<string, GridValue>.Empty.Add("units", GridValue.From(units)));
}
