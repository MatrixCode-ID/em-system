// Ported from GridCraft (core/src/addr.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
// Upstream sync: GridCraft crates/core/src/addr.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926.
using System.Globalization;

namespace Em.Controls.Sheet.Cells;

/// <summary>A zero-based sheet cell position.</summary>
/// <param name="Row">The zero-based row index.</param>
/// <param name="Column">The zero-based column index.</param>
public readonly record struct CellAddress(int Row, int Column)
{
    /// <summary>The number of rows in a sheet.</summary>
    public const int MaxRows = 1_048_576;
    /// <summary>The number of columns in a sheet.</summary>
    public const int MaxColumns = 16_384;
    /// <summary>Gets whether the address lies within the sheet.</summary>
    public bool IsValid => (uint)Row < MaxRows && (uint)Column < MaxColumns;

    /// <summary>Returns the letter name of a column, such as A, Z or AA.</summary>
    /// <param name="column">The zero-based column index.</param>
    /// <returns>The column name.</returns>
    public static string ColumnName(int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, MaxColumns);
        Span<char> text = stackalloc char[3];
        int start = text.Length;
        for (int index = column + 1; index != 0; index = (index - 1) / 26)
            text[--start] = (char)('A' + (index - 1) % 26);
        return new string(text[start..]);
    }

    /// <summary>Returns the A1-style reference, such as B3.</summary>
    /// <returns>The reference text.</returns>
    public override string ToString() => ColumnName(Column) + (Row + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses an A1-style reference, with optional dollar signs.</summary>
    /// <param name="text">The reference text.</param>
    /// <param name="address">The parsed address.</param>
    /// <returns>True when the text is a valid reference within the sheet.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out CellAddress address)
    {
        address = default;
        if (text.IsEmpty)
            return false;

        int cursor = text[0] == '$' ? 1 : 0;
        int start = cursor;
        int column = 0;
        while (cursor < text.Length && char.IsAsciiLetter(text[cursor]))
        {
            if (cursor - start == 3)
                return false;
            column = column * 26 + char.ToUpperInvariant(text[cursor++]) - 'A' + 1;
        }

        if (cursor < text.Length && text[cursor] == '$')
            cursor++;

        var rowText = text[cursor..];
        if (column is < 1 or > MaxColumns || rowText.Length is < 1 or > 7 ||
            !int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
            row is < 1 or > MaxRows)
            return false;

        address = new(row - 1, column - 1);
        return true;
    }
}

/// <summary>A rectangular block of cells, normalized so <see cref="Start"/> is the top-left corner.</summary>
public readonly record struct CellRange
{
    /// <summary>Creates a range from two corner cells in any order; both must lie within the sheet.</summary>
    /// <param name="first">One corner.</param>
    /// <param name="last">The opposite corner.</param>
    public CellRange(CellAddress first, CellAddress last)
    {
        if (!first.IsValid || !last.IsValid)
            throw new ArgumentOutOfRangeException(nameof(first), "The range must be within the sheet.");
        Start = new(Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column));
        End = new(Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column));
    }

    /// <summary>Gets the top-left cell.</summary>
    public CellAddress Start { get; }
    /// <summary>Gets the bottom-right cell.</summary>
    public CellAddress End { get; }
    /// <summary>Gets the number of rows.</summary>
    public int Height => End.Row - Start.Row + 1;
    /// <summary>Gets the number of columns.</summary>
    public int Width => End.Column - Start.Column + 1;
    /// <summary>Gets the number of cells.</summary>
    public long Count => (long)Height * Width;

    /// <summary>Checks whether a cell lies inside the range.</summary>
    /// <param name="cell">The cell to test.</param>
    /// <returns>True when the cell is inside.</returns>
    public bool Contains(CellAddress cell) => cell.Row >= Start.Row && cell.Row <= End.Row &&
        cell.Column >= Start.Column && cell.Column <= End.Column;

    /// <summary>Checks whether two ranges share at least one cell.</summary>
    /// <param name="other">The other range.</param>
    /// <returns>True when they overlap.</returns>
    public bool Intersects(CellRange other) => Start.Row <= other.End.Row && End.Row >= other.Start.Row &&
        Start.Column <= other.End.Column && End.Column >= other.Start.Column;

    /// <summary>Enumerates the cells row by row.</summary>
    /// <returns>The cells in row-major order.</returns>
    public IEnumerable<CellAddress> Cells()
    {
        for (int row = Start.Row; row <= End.Row; row++)
            for (int column = Start.Column; column <= End.Column; column++)
                yield return new(row, column);
    }

    /// <summary>Returns the A1-style range, such as A1:C3, or a single reference for one cell.</summary>
    /// <returns>The range text.</returns>
    public override string ToString() => Start == End ? Start.ToString() : $"{Start}:{End}";

    /// <summary>Parses an A1-style range or a single reference.</summary>
    /// <param name="text">The range text.</param>
    /// <param name="range">The parsed range.</param>
    /// <returns>True when the text is valid.</returns>
    public static bool TryParse(string text, out CellRange range)
    {
        range = default;
        var span = text.AsSpan();
        int separator = span.IndexOf(':');
        if (separator < 0)
        {
            if (!CellAddress.TryParse(span, out var cell))
                return false;
            range = new(cell, cell);
            return true;
        }
        if (!CellAddress.TryParse(span[..separator], out var first) ||
            !CellAddress.TryParse(span[(separator + 1)..], out var last))
            return false;
        range = new(first, last);
        return true;
    }
}
