using System.Collections.Immutable;
using Em.Grid;
using Em.Grid.Data;
using Em.Grid.Query;
using Xunit;

namespace Em.Libs.Tests.Grid.Data;

public sealed class EnterpriseQueryTests
{
    [Fact]
    public async Task Generated_and_local_group_pages_match_with_footer_totals_and_collapse()
    {
        var server = new GeneratedOrderProvider(1300);
        var rows = await server.FetchAsync(GridQuery.Empty, new RowRange(0, 1300));
        var local = new InMemoryGridDataProvider(rows.Rows);
        var total = new SummaryDescriptor("Units", SummaryOperation.Sum, SummaryScope.Total);
        var footer = new SummaryDescriptor("Units", SummaryOperation.Average, SummaryScope.Group);
        var query = new GridQuery
        {
            Groups = [new("Customer"), new("Priority", Descending: true)],
            Sort = [new("Units", Descending: true)],
            Filter = new FilterCondition("Units", FilterOperator.Between, GridValue.From(7)) { Operands = [GridValue.From(12)] },
            Summaries = [total, footer]
        };

        var first = await server.FetchAsync(query, new RowRange(0, 12));
        var group = Assert.IsType<GroupRowInfo>(first.Rows[0].Group);
        Assert.Equal(0, group.Level);
        Assert.True(group.Count > 0);
        Assert.Equal(390, first.DataCount);
        Assert.Equal(GridValue.From(3705), first.Totals[total.Key]);

        var collapsed = query with { CollapsedGroups = ImmutableHashSet.Create(group.Key) };
        var expected = await local.FetchAsync(collapsed, new RowRange(3, 100));
        var actual = await server.FetchAsync(collapsed, new RowRange(3, 100));
        Assert.Equal(expected.TotalCount, actual.TotalCount);
        Assert.True(actual.TotalCount < first.TotalCount);
        Assert.Equal(expected.Rows.Select(row => row.Key), actual.Rows.Select(row => row.Key));
        Assert.Contains(actual.Rows, row => row.Group is { IsFooter: true } && row.Summaries.ContainsKey(footer.Key));
        Assert.Equal(expected.Totals[total.Key], actual.Totals[total.Key]);
    }

    [Fact]
    public void Layout_round_trips_advanced_filters_search_and_legacy_version()
    {
        var date = new DateOnly(2026, 11, 12);
        var layout = new GridLayout
        {
            SchemaVersion = 0,
            Query = new GridQuery
            {
                Filter = new AllFilters([
                    new DatePeriodFilter("Date", RelativeDatePeriod.ThisWeek, date),
                    new NegatedFilter(new FilterCondition("Units", FilterOperator.In)
                    { Operands = [GridValue.From(0)] })
                ]),
                Search = new SearchCriteria("Customer:\"Blue Harbor\" Units:7", ["Customer", "Units"])
                {
                    DisplayLabels = ImmutableDictionary<string, ImmutableArray<GridValueLabel>>.Empty
                        .Add("Customer", [new(GridValue.From("BH"), "Blue Harbor")])
                }
            },
            Columns = [new("Units", 90, false)],
            ShowSearch = true
        };

        var restored = GridLayout.Load(layout.Save());
        Assert.Equal(1, restored.SchemaVersion);
        Assert.True(restored.ShowSearch);
        Assert.False(Assert.Single(restored.Columns).IsVisible);
        var row = new Dictionary<string, GridValue>
        {
            ["Date"] = GridValue.From(date.AddDays(-1)), ["Customer"] = GridValue.From("BH"), ["Units"] = GridValue.From(7)
        };
        var matches = restored.Query.CompilePredicate();
        Assert.True(matches(row));
        row["Date"] = GridValue.From(date.AddDays(-7));
        Assert.False(matches(row));
        Assert.True((restored.Query with { FilterEnabled = false }).Matches(row));
    }
}
