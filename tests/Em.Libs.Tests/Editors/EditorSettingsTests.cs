using System.Globalization;
using Em.Editors;
using Xunit;

namespace Em.Libs.Tests.Editors;

public sealed class EditorSettingsTests
{
    [Fact]
    public void Numeric_parse_honors_culture_range_and_target_type()
    {
        var settings = new NumericSettings
        {
            Culture = CultureInfo.GetCultureInfo("id-ID"),
            Minimum = 0,
            Maximum = 10,
            DisplayFormat = "0.0"
        };

        var result = settings.Parse("7,5", typeof(decimal));
        Assert.Equal(7.5m, Assert.IsType<decimal>(result.Value));
        Assert.Equal("7,5", settings.Format(result.Value));
        Assert.False(settings.Parse("7,5", typeof(int)).IsValid);
        Assert.False(settings.Parse("11", typeof(decimal)).IsValid);
    }

    [Fact]
    public void Date_parse_handles_bounds_and_nullable_target()
    {
        var settings = new DateSettings { AllowNull = true, EditFormat = "yyyy-MM-dd", Minimum = new(2026, 1, 1) };
        Assert.Equal(new DateOnly(2026, 6, 1), settings.Parse("2026-06-01", typeof(DateOnly)).Value);
        Assert.False(settings.Parse("2025-06-01", typeof(DateOnly)).IsValid);
        Assert.True(settings.Parse("", typeof(DateOnly?)).IsValid);
        Assert.False(settings.Parse("", typeof(DateOnly)).IsValid);
    }

    [Fact]
    public async Task Lookup_searches_secondary_fields_and_pages_results()
    {
        var settings = new LookupSettings
        {
            Options = [new(1, "Pear", ["Green"]), new(2, "Apple", ["Green"]), new(3, "Plum", ["Purple"])]
        };
        var page = await settings.SearchAsync("GREEN", 1, 1);
        Assert.Equal("Apple", Assert.Single(page).Label);
        Assert.Equal(2, settings.Parse("apple", typeof(int)).Value);
    }
}
