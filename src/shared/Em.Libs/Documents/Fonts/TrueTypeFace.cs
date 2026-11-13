// Ported Unicode embedding and advance-metric contracts from PdfCraft (crates/fonts/src/embedding.rs
// @ 059dbd6e00507cdfedd755bf6b712f883507fce0), MIT OR Apache-2.0.
// Copyright (c) 2026 ArtCraft Team and the PdfCraft contributors. The sfnt subset builder is original.
using System.IO;
using System.Buffers.Binary;
using System.Text;

namespace Em.Documents.Fonts;

/// <summary>A parsed TrueType font used to measure, shape and embed text in documents without platform font services.</summary>
public sealed class TrueTypeFace
{
    /// <summary>The manifest resource name of the bundled Noto Sans font.</summary>
    public const string DefaultResource = "Em.Documents.Assets.NotoSans-Regular.ttf";

    private static readonly Lazy<TrueTypeFace> DefaultFace = new(() =>
    {
        using var resource = typeof(TrueTypeFace).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidOperationException("The embedded document font is missing.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        return new TrueTypeFace(memory.ToArray(), "NotoSans-Regular");
    });

    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly int _cmapOffset;
    private readonly int _longMetrics;
    private readonly bool _longLocations;
    private readonly bool _noSubset;

    /// <summary>Parses a TrueType font.</summary>
    /// <param name="data">The font file bytes.</param>
    /// <param name="postScriptName">The PDF-safe PostScript name, made of ASCII letters, digits and hyphens.</param>
    public TrueTypeFace(ReadOnlySpan<byte> data, string postScriptName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postScriptName);
        if (!postScriptName.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Use a PDF-safe PostScript font name.", nameof(postScriptName));
        if (data.Length < 12 || U32(data, 0) != 0x00010000)
            throw new InvalidDataException("Select a standalone TrueType font with glyf outlines.");
        _data = data.ToArray();
        PostScriptName = postScriptName;
        int tableCount = U16(data, 4);
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            var tag = Encoding.ASCII.GetString(data.Slice(record, 4));
            int offset = checked((int)U32(data, record + 8));
            int length = checked((int)U32(data, record + 12));
            _ = data.Slice(offset, length);
            _tables.Add(tag, (offset, length));
        }

        var head = Table("head");
        var hhea = Table("hhea");
        var os2 = Table("OS/2");
        int embedding = U16(os2, 8);
        if ((embedding & 0x0002) != 0 || (embedding & 0x0200) != 0)
            throw new InvalidDataException("This font does not permit embedding outlines.");
        _noSubset = (embedding & 0x0100) != 0;
        UnitsPerEm = U16(head, 18);
        if (UnitsPerEm == 0)
            throw new InvalidDataException("The font has invalid units per em.");
        Bounds = (I16(head, 36), I16(head, 38), I16(head, 40), I16(head, 42));
        GlyphCount = U16(Table("maxp"), 4);
        Ascent = I16(hhea, 4);
        Descent = I16(hhea, 6);
        _longMetrics = U16(hhea, 34);
        _longLocations = I16(head, 50) == 1;
        if (GlyphCount == 0 || _longMetrics < 1 || _longMetrics > GlyphCount)
            throw new InvalidDataException("The font has invalid horizontal metrics.");
        _ = Table("glyf");
        _ = Table("loca");
        _cmapOffset = FindUnicodeMap(Table("cmap"));
    }

    /// <summary>Gets the bundled Noto Sans face, loaded on first use.</summary>
    public static TrueTypeFace Default => DefaultFace.Value;
    /// <summary>Gets the PostScript name written to PDF font dictionaries.</summary>
    public string PostScriptName { get; }
    /// <summary>Gets the number of font design units per em.</summary>
    public int UnitsPerEm { get; }
    /// <summary>Gets the number of glyphs in the font.</summary>
    public int GlyphCount { get; }
    /// <summary>Gets the ascender in font units.</summary>
    public int Ascent { get; }
    /// <summary>Gets the descender in font units, normally negative.</summary>
    public int Descent { get; }
    /// <summary>Gets the font bounding box in font units.</summary>
    public (int Left, int Bottom, int Right, int Top) Bounds { get; }
    /// <summary>Gets whether the font licence allows embedding a subset; otherwise the full font is embedded.</summary>
    public bool CanSubset => !_noSubset;

    /// <summary>Returns the horizontal advance of a glyph.</summary>
    /// <param name="glyph">The glyph id.</param>
    /// <returns>The advance in font units.</returns>
    public int Advance(ushort glyph)
    {
        int index = Math.Min(glyph, _longMetrics - 1);
        return U16(Table("hmtx"), index * 4);
    }

    /// <summary>Maps a code point to a glyph.</summary>
    /// <param name="rune">The code point.</param>
    /// <returns>The glyph id, or 0 when the font has no glyph for it.</returns>
    public ushort Glyph(Rune rune)
    {
        var map = Table("cmap")[_cmapOffset..];
        if (U16(map, 0) == 12)
        {
            int low = 0, high = checked((int)U32(map, 12)) - 1;
            uint scalar = (uint)rune.Value;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                int group = 16 + middle * 12;
                uint first = U32(map, group);
                uint last = U32(map, group + 4);
                if (scalar < first)
                    high = middle - 1;
                else if (scalar > last)
                    low = middle + 1;
                else
                {
                    uint id = U32(map, group + 8) + scalar - first;
                    return id < GlyphCount ? (ushort)id : (ushort)0;
                }
            }
            return 0;
        }

        if (!rune.IsBmp)
            return 0;
        int count = U16(map, 6) / 2;
        for (int i = 0; i < count; i++)
        {
            int last = U16(map, 14 + i * 2);
            if (rune.Value > last)
                continue;
            int first = U16(map, 16 + count * 2 + i * 2);
            if (rune.Value < first)
                return 0;
            int delta = I16(map, 16 + count * 4 + i * 2);
            int at = 16 + count * 6 + i * 2;
            int range = U16(map, at);
            int glyph = range == 0 ? rune.Value : U16(map, at + range + 2 * (rune.Value - first));
            if (range != 0 && glyph == 0)
                return 0;
            int index = (glyph + delta) & 0xffff;
            return index < GlyphCount ? (ushort)index : (ushort)0;
        }
        return 0;
    }

    /// <summary>Builds a subset font that keeps the requested glyphs and the glyphs they reference.</summary>
    /// <param name="requested">The glyph ids to keep; glyph 0 is always kept.</param>
    /// <returns>The subset font bytes, or a copy of the whole font when subsetting is not allowed.</returns>
    public byte[] Subset(IEnumerable<ushort> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (_noSubset)
            return (byte[])_data.Clone();
        var included = new HashSet<ushort> { 0 };
        var work = new Stack<ushort>(requested.Append((ushort)0));
        var visited = new HashSet<ushort>();
        while (work.TryPop(out ushort id))
        {
            if (id >= GlyphCount || !visited.Add(id))
                continue;
            included.Add(id);
            var glyph = GlyphData(id);
            if (glyph.Length < 10 || I16(glyph, 0) >= 0)
                continue;
            int at = 10;
            int flags;
            do
            {
                flags = U16(glyph, at);
                work.Push(U16(glyph, at + 2));
                at += 4 + ((flags & 1) != 0 ? 4 : 2);
                if ((flags & 8) != 0) at += 2;
                else if ((flags & 64) != 0) at += 4;
                else if ((flags & 128) != 0) at += 8;
            } while ((flags & 32) != 0);
        }

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in new[] { "head", "hhea", "maxp", "OS/2", "hmtx", "cmap", "name", "post", "cvt ", "fpgm", "prep", "gasp" })
            if (_tables.ContainsKey(tag))
                tables[tag] = Table(tag).ToArray();
        Put32(tables["head"], 8, 0);
        Put16(tables["head"], 50, 1);

        using var outlines = new MemoryStream();
        var locations = new byte[4 * (GlyphCount + 1)];
        for (int id = 0; id < GlyphCount; id++)
        {
            Put32(locations, id * 4, (uint)outlines.Length);
            if (included.Contains((ushort)id))
            {
                outlines.Write(GlyphData((ushort)id));
                while ((outlines.Length & 3) != 0)
                    outlines.WriteByte(0);
            }
        }
        Put32(locations, GlyphCount * 4, (uint)outlines.Length);
        tables.Add("glyf", outlines.ToArray());
        tables.Add("loca", locations);
        return BuildSfnt(tables);
    }

    private ReadOnlySpan<byte> GlyphData(ushort glyph)
    {
        var loca = Table("loca");
        int start = _longLocations ? checked((int)U32(loca, glyph * 4)) : U16(loca, glyph * 2) * 2;
        int end = _longLocations ? checked((int)U32(loca, (glyph + 1) * 4)) : U16(loca, (glyph + 1) * 2) * 2;
        return Table("glyf").Slice(start, end - start);
    }

    private ReadOnlySpan<byte> Table(string name)
    {
        if (!_tables.TryGetValue(name, out var entry))
            throw new InvalidDataException($"The font lacks the {name} table.");
        return _data.AsSpan(entry.Offset, entry.Length);
    }

    private static int FindUnicodeMap(ReadOnlySpan<byte> cmap)
    {
        int count = U16(cmap, 2);
        int format4 = -1;
        for (int index = 0; index < count; index++)
        {
            int record = 4 + index * 8;
            int platform = U16(cmap, record);
            int encoding = U16(cmap, record + 2);
            if (platform != 0 && (platform != 3 || encoding is not (1 or 10)))
                continue;
            int offset = checked((int)U32(cmap, record + 4));
            int format = U16(cmap, offset);
            if (format == 12)
                return offset;
            if (format == 4)
                format4 = offset;
        }
        return format4 >= 0 ? format4 : throw new InvalidDataException("The font has no Unicode cmap.");
    }

    private static byte[] BuildSfnt(SortedDictionary<string, byte[]> tables)
    {
        int size = 12 + tables.Count * 16 + tables.Values.Sum(table => (table.Length + 3) & ~3);
        var output = new byte[size];
        Put32(output, 0, 0x00010000);
        Put16(output, 4, (ushort)tables.Count);
        int power = System.Numerics.BitOperations.Log2((uint)tables.Count);
        int search = (1 << power) * 16;
        Put16(output, 6, (ushort)search);
        Put16(output, 8, (ushort)power);
        Put16(output, 10, (ushort)(tables.Count * 16 - search));
        int directory = 12;
        int offset = 12 + tables.Count * 16;
        int headOffset = 0;
        foreach (var (tag, bytes) in tables)
        {
            Encoding.ASCII.GetBytes(tag).CopyTo(output, directory);
            bytes.CopyTo(output, offset);
            int padded = (bytes.Length + 3) & ~3;
            Put32(output, directory + 4, Checksum(output.AsSpan(offset, padded)));
            Put32(output, directory + 8, (uint)offset);
            Put32(output, directory + 12, (uint)bytes.Length);
            if (tag == "head")
                headOffset = offset;
            directory += 16;
            offset += padded;
        }
        Put32(output, headOffset + 8, unchecked(0xB1B0AFBAu - Checksum(output)));
        return output;
    }

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint result = 0;
        for (int index = 0; index < bytes.Length; index += 4)
            result = unchecked(result + U32(bytes, index));
        return result;
    }

    private static ushort U16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
    private static short I16(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadInt16BigEndian(data[at..]);
    private static uint U32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
    private static void Put16(Span<byte> data, int at, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data[at..], value);
    private static void Put32(Span<byte> data, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data[at..], value);
}
