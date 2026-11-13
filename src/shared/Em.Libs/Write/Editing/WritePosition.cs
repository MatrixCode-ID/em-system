// Ported from WordCraft (doc/src/position.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
namespace Em.Write.Editing;

using Em.Write.Model;

/// <summary>A position in a word-processing document.</summary>
/// <param name="Section">The zero-based section index.</param>
/// <param name="Block">The zero-based block index in the story.</param>
/// <param name="Offset">The position inside the paragraph.</param>
/// <param name="Story">The story.</param>
/// <param name="Row">The table row for a position inside a table, or -1.</param>
/// <param name="Cell">The table cell for a position inside a table, or -1.</param>
/// <param name="CellBlock">The block index inside the table cell.</param>
public readonly record struct WritePosition(int Section, int Block, int Offset, WriteStory Story = WriteStory.Body,
    int Row = -1, int Cell = -1, int CellBlock = 0) : IComparable<WritePosition>
{
    /// <summary>Compares positions in document order.</summary>
    /// <param name="other">The position to compare with.</param>
    /// <returns>A negative number, zero or a positive number.</returns>
    public int CompareTo(WritePosition other)
    {
        int comparison = Section.CompareTo(other.Section);
        if (comparison != 0) return comparison;
        comparison = Story.CompareTo(other.Story);
        if (comparison != 0) return comparison;
        comparison = Block.CompareTo(other.Block);
        if (comparison != 0) return comparison;
        comparison = Row.CompareTo(other.Row);
        if (comparison != 0) return comparison;
        comparison = Cell.CompareTo(other.Cell);
        if (comparison != 0) return comparison;
        comparison = CellBlock.CompareTo(other.CellBlock);
        return comparison != 0 ? comparison : Offset.CompareTo(other.Offset);
    }
}

/// <summary>A selection between an anchor and a caret, in either order.</summary>
/// <param name="Anchor">The fixed end.</param>
/// <param name="Caret">The moving end.</param>
public readonly record struct WriteSelection(WritePosition Anchor, WritePosition Caret)
{
    /// <summary>Gets whether the selection is a caret without a range.</summary>
    public bool IsEmpty => Anchor == Caret;
    /// <summary>Gets the earlier end.</summary>
    public WritePosition Start => Anchor.CompareTo(Caret) <= 0 ? Anchor : Caret;
    /// <summary>Gets the later end.</summary>
    public WritePosition End => Anchor.CompareTo(Caret) >= 0 ? Anchor : Caret;
}
