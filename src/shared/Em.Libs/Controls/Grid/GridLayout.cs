using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Em.Controls.Grid.Query;

namespace Em.Controls.Grid;

/// <summary>Stores the persisted layout of one column.</summary>
/// <param name="Key">The column key.</param>
/// <param name="Width">The column width in device-independent pixels.</param>
/// <param name="IsVisible">Whether the column is shown.</param>
/// <param name="IsFrozen">Whether the column is pinned at the leading edge.</param>
/// <param name="Band">The optional band caption.</param>
public sealed record GridColumnLayout(string Key, double Width, bool IsVisible, bool IsFrozen = false, string? Band = null);

/// <summary>A serializable snapshot of the grid layout: column widths and visibility, the query and panel options.</summary>
public sealed record GridLayout
{
    /// <summary>Gets the layout schema version. <see cref="Load(string)"/> accepts versions 0 and 1.</summary>
    public int SchemaVersion { get; init; } = 1;
    /// <summary>Gets the per-column layout entries in display order.</summary>
    public ImmutableArray<GridColumnLayout> Columns { get; init; } = [];
    /// <summary>Gets the sort, group, filter and search query.</summary>
    public GridQuery Query { get; init; } = GridQuery.Empty;
    /// <summary>Gets whether the group panel is shown. The default is true.</summary>
    public bool ShowGroupPanel { get; init; } = true;
    /// <summary>Gets whether the auto-filter row is shown.</summary>
    public bool ShowAutoFilter { get; init; }
    /// <summary>Gets whether the search panel is shown.</summary>
    public bool ShowSearch { get; init; }
    /// <summary>Gets the finder search that was active when the layout was saved, or null when there was none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SearchCriteria? FinderSearch { get; init; }

    /// <summary>Serializes the layout to JSON.</summary>
    /// <returns>The JSON text.</returns>
    public string Save() => JsonSerializer.Serialize(this, GridJsonContext.Default.GridLayout);

    /// <summary>Restores a layout from JSON produced by <see cref="Save"/>.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The validated layout, normalized to the current schema version.</returns>
    public static GridLayout Load(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var layout = JsonSerializer.Deserialize(json, GridJsonContext.Default.GridLayout)
            ?? throw new JsonException("The layout is empty.");
        if (layout.SchemaVersion is < 0 or > 1)
            throw new NotSupportedException($"Layout version {layout.SchemaVersion} is not supported.");
        if (layout.Columns.IsDefault || layout.Query is null)
            throw new JsonException("The layout is missing required data.");
        return layout with { SchemaVersion = 1 };
    }
}
