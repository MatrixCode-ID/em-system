using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Em.Documents.Model;

/// <summary>
/// Reads a table's rows on a background task in batches, so producing rows (data access and value formatting)
/// overlaps with laying them out or serializing them. A consumer that stops early cancels the producer.
/// </summary>
internal static class RowPrefetch
{
    private const int BatchSize = 256;

    public static async IAsyncEnumerable<DocumentRow> ReadAsync(IAsyncEnumerable<DocumentRow> source,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        var channel = Channel.CreateBounded<DocumentRow[]>(
            new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var producer = Task.Run(async () =>
        {
            try
            {
                var batch = new List<DocumentRow>(BatchSize);
                await foreach (var row in source.WithCancellation(stop.Token).ConfigureAwait(false))
                {
                    batch.Add(row);
                    if (batch.Count == BatchSize)
                    {
                        await channel.Writer.WriteAsync(batch.ToArray(), stop.Token).ConfigureAwait(false);
                        batch.Clear();
                    }
                }
                if (batch.Count > 0)
                    await channel.Writer.WriteAsync(batch.ToArray(), stop.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex) { channel.Writer.TryComplete(ex); }
        }, CancellationToken.None);

        try
        {
            while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                while (channel.Reader.TryRead(out var batch))
                    foreach (var row in batch)
                        yield return row;
        }
        finally
        {
            stop.Cancel();
            await producer.ConfigureAwait(false);
        }
    }

    public static IAsyncEnumerable<DocumentRow> OpenRowsPrefetched(this DocumentTable table, CancellationToken token) =>
        ReadAsync(table.OpenRows(token), token);
}
