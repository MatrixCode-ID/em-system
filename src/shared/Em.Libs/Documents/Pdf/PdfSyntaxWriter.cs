// Ported full-save object, compressed stream and classic xref structure from PdfCraft
// (crates/cos/src/writer.rs @ 059dbd6e00507cdfedd755bf6b712f883507fce0), MIT OR Apache-2.0.
// Copyright (c) 2026 ArtCraft Team and the PdfCraft contributors.
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Em.Documents.Pdf;

internal sealed class PdfSyntaxWriter
{
    private readonly Stream _destination;
    private readonly List<long> _offsets;
    private long _position;

    public PdfSyntaxWriter(Stream destination, int objectCount)
    {
        if (destination.CanSeek && destination.Position != 0)
            throw new ArgumentException("The destination must begin at position zero.", nameof(destination));
        _destination = destination;
        // The table grows when a higher object number is written, so a single-pass writer need not know the count.
        _offsets = new List<long>(objectCount + 1) { 0 };
        Write(Encoding.Latin1.GetBytes("%PDF-1.7\n%âãÏÓ\n"));
    }

    public void Object(int id, string body)
    {
        Begin(id);
        WriteAscii(body);
        WriteAscii("\nendobj\n");
    }

    public void Stream(int id, ReadOnlySpan<byte> data, string dictionary = "") =>
        CompressedStream(id, Deflate(data), dictionary);

    /// <summary>Compresses stream data; safe to call on any thread, so pages can be compressed in parallel.</summary>
    public static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var memory = new MemoryStream(Math.Max(256, data.Length / 4));
        using (var deflate = new ZLibStream(memory, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(data);
        return memory.ToArray();
    }

    /// <summary>Writes a stream object whose data was already compressed with <see cref="Deflate"/>.</summary>
    public void CompressedStream(int id, ReadOnlySpan<byte> deflated, string dictionary = "")
    {
        Begin(id);
        WriteAscii(FormattableString.Invariant($"<< /Length {deflated.Length} /Filter /FlateDecode {dictionary} >>\nstream\n"));
        Write(deflated);
        WriteAscii("\nendstream\nendobj\n");
    }

    public void Finish(int catalog, int info)
    {
        long start = _position;
        WriteAscii(FormattableString.Invariant($"xref\n0 {_offsets.Count}\n0000000000 65535 f \n"));
        foreach (long offset in _offsets.Skip(1))
        {
            if (offset is <= 0 or > 9_999_999_999)
                throw new InvalidOperationException("The PDF cross-reference offset is outside the supported range.");
            WriteAscii(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }
        WriteAscii(FormattableString.Invariant(
            $"trailer\n<< /Size {_offsets.Count} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{start}\n%%EOF\n"));
    }

    private void Begin(int id)
    {
        while (id > 0 && _offsets.Count <= id)
            _offsets.Add(0);
        if (id <= 0 || _offsets[id] != 0)
            throw new InvalidOperationException($"PDF object {id} is invalid or already written.");
        _offsets[id] = _position;
        WriteAscii(id.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
    }

    private void WriteAscii(string text) => Write(Encoding.ASCII.GetBytes(text));

    private void Write(ReadOnlySpan<byte> bytes)
    {
        _destination.Write(bytes);
        _position += bytes.Length;
    }
}
