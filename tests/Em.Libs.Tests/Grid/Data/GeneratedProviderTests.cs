using Em.Grid;
using Em.Grid.Data;
using Em.Grid.Query;
using Xunit;

namespace Em.Libs.Tests.Grid.Data;

public sealed class GeneratedProviderTests
{
    [Fact]
    public async Task Deep_window_keeps_row_keys_and_returns_only_requested_rows()
    {
        var provider = new GeneratedOrderProvider(1_000_000);
        var page = await provider.FetchAsync(GridQuery.Empty, new RowRange(999_990, 5));
        Assert.Equal(1_000_000, page.TotalCount);
        Assert.Equal(5, page.Rows.Length);
        Assert.Equal("SO-0000999991", page.Rows[0].Key.Value);

        var repeated = await provider.FetchAsync(GridQuery.Empty, new RowRange(999_990, 1));
        Assert.Equal(page.Rows[0].Key, repeated.Rows[0].Key);
    }

    [Fact]
    public async Task Sorted_filtered_windows_match_materialized_reference()
    {
        var provider = new GeneratedOrderProvider(1253);
        var source = await provider.FetchAsync(GridQuery.Empty, new RowRange(0, 1253));
        var local = new InMemoryGridDataProvider(source.Rows);
        var query = new GridQuery
        {
            Filter = new FilterCondition("Units", FilterOperator.Greater, GridValue.From(16)),
            Search = new SearchCriteria("Customer", ["Customer"]),
            Sort = [new SortDescriptor("Date", Descending: true), new SortDescriptor("Order", Descending: true)]
        };
        var range = new RowRange(47, 21);
        var expected = await local.FetchAsync(query, range);
        var actual = await provider.FetchAsync(query, range);
        Assert.Equal(expected.TotalCount, actual.TotalCount);
        Assert.Equal(expected.Rows.Select(row => row.Key), actual.Rows.Select(row => row.Key));

        var values = await provider.GetDistinctValuesAsync(query, "Units", new RowRange(0, 20));
        Assert.Equal([GridValue.From(17), GridValue.From(18), GridValue.From(19), GridValue.From(20)], values.AsEnumerable());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await provider.FetchAsync(query, range, new CancellationToken(canceled: true)));
    }
}
