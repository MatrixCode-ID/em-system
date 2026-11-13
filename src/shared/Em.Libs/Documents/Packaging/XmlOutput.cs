using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;

namespace Em.Documents.Packaging;

/// <summary>
/// Writes XML as UTF-8 straight into a buffer for large parts such as worksheet and table bodies, where a general
/// <see cref="System.Xml.XmlWriter"/> spends most of its time per element. Full buffers are handed to a background
/// task that writes (and so compresses) them, so building XML and deflating it run on separate cores.
/// Callers write well-formed markup themselves; only text and attribute values are escaped here.
/// </summary>
internal sealed class XmlOutput : IAsyncDisposable
{
    private const int ChunkSize = 1 << 16;
    private static readonly SearchValues<char> Special = SearchValues.Create(
        "&<>\"\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000B\u000C\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F￾￿");

    private readonly Stream _target;
    private readonly Channel<(byte[] Buffer, int Length)> _chunks =
        Channel.CreateBounded<(byte[], int)>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true });
    private readonly Task _drain;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(ChunkSize * 2);
    private int _length;
    private bool _completed;

    public XmlOutput(Stream target)
    {
        _target = target;
        _drain = Task.Run(DrainAsync);
    }

    public void Raw(ReadOnlySpan<byte> utf8)
    {
        Ensure(utf8.Length);
        utf8.CopyTo(_buffer.AsSpan(_length));
        _length += utf8.Length;
    }

    public void Raw(byte value)
    {
        Ensure(1);
        _buffer[_length++] = value;
    }

    /// <summary>Writes escaped text, valid for element content and double-quoted attribute values.</summary>
    public void Text(ReadOnlySpan<char> text)
    {
        while (!text.IsEmpty)
        {
            int special = text.IndexOfAny(Special);
            var plain = special < 0 ? text : text[..special];
            if (!plain.IsEmpty)
            {
                Ensure(Encoding.UTF8.GetMaxByteCount(plain.Length));
                _length += Encoding.UTF8.GetBytes(plain, _buffer.AsSpan(_length));
            }
            if (special < 0)
                return;
            switch (text[special])
            {
                case '&': Raw("&amp;"u8); break;
                case '<': Raw("&lt;"u8); break;
                case '>': Raw("&gt;"u8); break;
                case '"': Raw("&quot;"u8); break;
                // Characters XML 1.0 cannot carry are dropped.
            }
            text = text[(special + 1)..];
        }
    }

    public void Number(long value)
    {
        Ensure(24);
        value.TryFormat(_buffer.AsSpan(_length), out int written, default, CultureInfo.InvariantCulture);
        _length += written;
    }

    public void Number(decimal value)
    {
        Ensure(40);
        value.TryFormat(_buffer.AsSpan(_length), out int written, default, CultureInfo.InvariantCulture);
        _length += written;
    }

    /// <summary>Hands a full buffer to the background writer; call once per row or similar unit.</summary>
    public ValueTask CommitAsync(CancellationToken token = default) =>
        _length >= ChunkSize ? SendAsync(token) : ValueTask.CompletedTask;

    /// <summary>Writes the remaining output and waits until the background writer has finished.</summary>
    public async ValueTask CompleteAsync(CancellationToken token = default)
    {
        if (_completed)
            return;
        _completed = true;
        if (_length > 0)
            await SendAsync(token).ConfigureAwait(false);
        _chunks.Writer.TryComplete();
        await _drain.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            _completed = true;
            _chunks.Writer.TryComplete();
            try { await _drain.ConfigureAwait(false); }
            catch (Exception) { }
        }
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private async ValueTask SendAsync(CancellationToken token)
    {
        var full = _buffer;
        int length = _length;
        _buffer = ArrayPool<byte>.Shared.Rent(ChunkSize * 2);
        _length = 0;
        await _chunks.Writer.WriteAsync((full, length), token).ConfigureAwait(false);
    }

    private void Ensure(int extra)
    {
        if (_length + extra <= _buffer.Length)
            return;
        var larger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + extra));
        _buffer.AsSpan(0, _length).CopyTo(larger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = larger;
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (var (buffer, length) in _chunks.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try { _target.Write(buffer, 0, length); }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
        }
        catch (Exception ex)
        {
            // Stop the producer: its next hand-off fails with this error instead of waiting on a full queue.
            _chunks.Writer.TryComplete(ex);
            while (_chunks.Reader.TryRead(out var pending))
                ArrayPool<byte>.Shared.Return(pending.Buffer);
            throw;
        }
    }
}
