using System.Runtime.CompilerServices;
using Em.Grid.Query;

namespace Em.Grid.Data;

/// <summary>A data provider that can stream all display rows of a query directly, for exports and other full reads.</summary>
public interface IStreamingGridDataProvider : IGridDataProvider
{
    /// <summary>Streams every display row of the query in display order.</summary>
    /// <param name="query">The query to apply.</param>
    /// <param name="cancellationToken">Cancels the stream.</param>
    /// <returns>The rows in display order.</returns>
    IAsyncEnumerable<GridRow> StreamAsync(GridQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Reads all display rows of a query from any data provider.</summary>
public static class GridRowStreaming
{
    /// <summary>Reads all display rows, streaming when the provider supports <see cref="IStreamingGridDataProvider"/> and paging otherwise.</summary>
    /// <param name="provider">The data provider.</param>
    /// <param name="query">The query to apply.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows in display order.</returns>
    public static IAsyncEnumerable<GridRow> ReadAsync(IGridDataProvider provider, GridQuery query,
        CancellationToken cancellationToken = default) => provider is IStreamingGridDataProvider streaming
        ? streaming.StreamAsync(query, cancellationToken)
        : ReadPagesAsync(provider, query, cancellationToken);

    /// <summary>Reads all display rows page by page from the first row.</summary>
    /// <param name="provider">The data provider.</param>
    /// <param name="query">The query to apply.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The rows in display order.</returns>
    public static IAsyncEnumerable<GridRow> ReadPagesAsync(IGridDataProvider provider, GridQuery query,
        CancellationToken cancellationToken = default) => ReadPagesAsync(provider, query, 0, cancellationToken);

    /// <summary>Reads the display rows of <paramref name="query"/> page by page, starting at a zero-based row offset.</summary>
    public static async IAsyncEnumerable<GridRow> ReadPagesAsync(IGridDataProvider provider, GridQuery query, int start,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        const int pageSize = 512;
        int offset = start;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await provider.FetchAsync(query, new RowRange(offset, pageSize), cancellationToken).ConfigureAwait(false);
            if (page.Rows.IsDefaultOrEmpty)
                yield break;
            foreach (var row in page.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return row;
            }
            offset = checked(offset + page.Rows.Length);
            if (page.TotalCount is int total && offset >= total)
                yield break;
            if (page.TotalCount is null && page.Rows.Length < pageSize)
                yield break;
        }
    }
}
