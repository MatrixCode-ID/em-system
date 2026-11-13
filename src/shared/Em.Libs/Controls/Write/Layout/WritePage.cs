// Ported from WordCraft (layout/src/hit.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using Em.Documents.Layout;
using Em.Controls.Write.Editing;
using Em.Controls.Write.Model;

namespace Em.Controls.Write.Layout;

/// <summary>A rectangle on a page, in device-independent pixels.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct WriteRect(double X, double Y, double Width, double Height);
/// <summary>A caret on a page.</summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Top">The top of the line.</param>
/// <param name="Height">The line height.</param>
public readonly record struct WriteCaret(double X, double Top, double Height);
/// <summary>A caret stop on a laid-out line.</summary>
/// <param name="Offset">The paragraph position.</param>
/// <param name="X">The horizontal position of the stop.</param>
public readonly record struct WriteStop(int Offset, double X);

/// <summary>One laid-out line, used for hit testing and caret placement.</summary>
/// <param name="Position">The paragraph the line belongs to.</param>
/// <param name="Top">The top of the line.</param>
/// <param name="Height">The line height.</param>
/// <param name="Stops">The caret stops from left to right.</param>
public sealed record WriteLine(WritePosition Position, double Top, double Height, ImmutableArray<WriteStop> Stops)
{
    /// <summary>Finds the caret stop nearest to a horizontal position.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <returns>The position of the nearest stop.</returns>
    public WritePosition Hit(double x)
    {
        var nearest = Stops[0];
        for (int i = 1; i < Stops.Length; i++)
        {
            var next = Stops[i];
            if (x < (nearest.X + next.X) / 2)
                break;
            nearest = next;
        }
        return Position with { Offset = nearest.Offset };
    }

    /// <summary>Returns the caret at a position when it lies on this line.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The caret, or null when the position is not on this line.</returns>
    public WriteCaret? Caret(WritePosition position)
    {
        if ((position with { Offset = 0 }) != (Position with { Offset = 0 }))
            return null;
        foreach (var stop in Stops)
            if (stop.Offset == position.Offset)
                return new(stop.X, Top, Height);
        return null;
    }
}

/// <summary>An image placed on a page.</summary>
/// <param name="Image">The image.</param>
/// <param name="Bounds">The area it occupies.</param>
public sealed record WritePlacedImage(WriteImage Image, WriteRect Bounds);
/// <summary>One laid-out page of a word-processing document.</summary>
/// <param name="Number">The one-based page number.</param>
/// <param name="Section">The zero-based section index.</param>
/// <param name="Settings">The page settings.</param>
/// <param name="Commands">The drawing commands.</param>
/// <param name="Lines">The laid-out lines.</param>
/// <param name="Images">The placed images.</param>
public sealed record WritePage(int Number, int Section, WritePageSettings Settings,
    ImmutableArray<DrawCommand> Commands, ImmutableArray<WriteLine> Lines, ImmutableArray<WritePlacedImage> Images)
{
    /// <summary>Finds the document position nearest to a point.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="story">Limits the search to one story, or null for any story.</param>
    /// <returns>The nearest position, or null when the page has no lines.</returns>
    public WritePosition? HitTest(double x, double y, WriteStory? story = null)
    {
        double distance = double.PositiveInfinity;
        WriteLine? nearest = null;
        foreach (var line in Lines)
        {
            if (story.HasValue && line.Position.Story != story)
                continue;
            double vertical = y < line.Top ? line.Top - y : Math.Max(0, y - line.Top - line.Height);
            double horizontal = x < line.Stops[0].X ? line.Stops[0].X - x : Math.Max(0, x - line.Stops[^1].X);
            double d = vertical * vertical + horizontal * horizontal;
            if (d < distance)
            {
                distance = d;
                nearest = line;
            }
        }
        return nearest?.Hit(x);
    }

    /// <summary>Returns the caret for a position on this page.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The caret, or null when the position is not on this page.</returns>
    public WriteCaret? FindCaret(WritePosition position)
    {
        foreach (var line in Lines)
            if (line.Caret(position) is { } caret)
                return caret;
        return null;
    }

    /// <summary>Returns the rectangles that highlight a selection on this page.</summary>
    /// <param name="selection">The selection.</param>
    /// <returns>One rectangle per selected line part.</returns>
    public IEnumerable<WriteRect> SelectionBounds(WriteSelection selection)
    {
        if (selection.IsEmpty)
            yield break;
        foreach (var line in Lines)
        {
            if (line.Position.Story != selection.Start.Story)
                continue;
            var beginning = line.Position with { Offset = line.Stops[0].Offset };
            var end = line.Position with { Offset = line.Stops[^1].Offset };
            if (end.CompareTo(selection.Start) <= 0 || beginning.CompareTo(selection.End) >= 0)
                continue;
            var first = line.Stops.FirstOrDefault(stop => (line.Position with { Offset = stop.Offset }).CompareTo(selection.Start) >= 0, line.Stops[0]);
            var last = line.Stops.LastOrDefault(stop => (line.Position with { Offset = stop.Offset }).CompareTo(selection.End) <= 0, line.Stops[^1]);
            if (last.X > first.X)
                yield return new(first.X, line.Top, last.X - first.X, line.Height);
        }
    }
}
