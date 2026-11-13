// Ported from GridCraft (crates/ui-egui/src/editor.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using Em.Sheet.Functions;

namespace Em.Sheet.Formulas;

/// <summary>A span of formula text, as start and end character indexes.</summary>
public readonly record struct TextSpan(int Start, int End);

/// <summary>
/// Formula text while it is typed in a cell: point mode (an arrow or click inserts a reference), function autocomplete and
/// the argument hint. Text positions are character indexes, as a text box reports its caret.
/// </summary>
public static class FormulaInput
{
    private const char ListSeparator = ',';

    /// <summary>Checks whether typed text is a formula: it starts with an equals sign, or with a plus or a minus that is not followed by a digit.</summary>
    /// <param name="text">The typed text.</param>
    /// <returns>True for formula input.</returns>
    public static bool IsFormula(string text) =>
        text.StartsWith('=') || text.Length > 1 && text[0] == '+' ||
        text.Length > 1 && text[0] == '-' && !char.IsAsciiDigit(text[1]);

    /// <summary>Whether the pointed reference ends at the caret, so the next arrow or click replaces it.</summary>
    public static bool PointAtCaret(string text, int caret, TextSpan? point) =>
        point is { } span && span.End == caret && span.Start >= 0 && span.End <= text.Length && span.Start <= span.End;

    /// <summary>Point mode: the caret follows an operator, a parenthesis, a separator or the equals sign of a formula.</summary>
    public static bool CanPoint(string text, int caret, TextSpan? point)
    {
        if (!IsFormula(text))
            return false;
        if (PointAtCaret(text, caret, point))
            return true;
        var before = text.AsSpan(0, Math.Clamp(caret, 0, text.Length)).TrimEnd();
        return before.Length > 0 && before[^1] is '=' or '(' or ',' or '+' or '-' or '*' or '/' or '^' or '&' or '<' or '>' or ':' or ';' or '{';
    }

    /// <summary>Inserts a reference at the caret, or replaces the reference pointed last when it ends there.</summary>
    public static (string Text, int Caret, TextSpan Point) InsertReference(string text, int caret, TextSpan? point, string reference)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var (start, end) = PointAtCaret(text, caret, point) ? (point!.Value.Start, point.Value.End) : (caret, caret);
        string result = string.Concat(text.AsSpan(0, start), reference, text.AsSpan(end));
        int after = start + reference.Length;
        return (result, after, new(start, after));
    }

    /// <summary>The function names that start with the word before the caret, or none outside a formula.</summary>
    public static IReadOnlyList<SheetFunctionInfo> Complete(string text, int caret)
    {
        if (!IsFormula(text))
            return [];
        string word = WordBefore(text, caret);
        if (word.Length == 0 || char.IsAsciiDigit(word[0]))
            return [];
        var matches = SheetFunctionCatalog.All.Where(item => item.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Take(12).ToList();
        return matches.Count == 1 && string.Equals(matches[0].Name, word, StringComparison.OrdinalIgnoreCase) ? [] : matches;
    }

    /// <summary>Replaces the word before the caret with the function name and its opening parenthesis.</summary>
    public static (string Text, int Caret) Accept(string text, int caret, string name)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int start = caret - WordBefore(text, caret).Length;
        string result = string.Concat(text.AsSpan(0, start), name, "(", text.AsSpan(caret));
        return (result, start + name.Length + 1);
    }

    /// <summary>The function call the caret is inside and the index of the argument it is on.</summary>
    public static (string Name, int Argument)? CurrentFunction(string text, int caret)
    {
        if (!IsFormula(text))
            return null;
        int depth = 0, argument = 0;
        bool quoted = false;
        for (int i = Math.Clamp(caret, 0, text.Length) - 1; i >= 0; i--)
        {
            char c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
                continue;
            switch (c)
            {
                case ')' or '}' or ']': depth++; break;
                case '{' or '[': depth--; break;
                case '(' when depth == 0:
                    string name = WordBefore(text, i);
                    return name.Length == 0 ? null : (name.ToUpperInvariant(), argument);
                case '(': depth--; break;
                case ListSeparator when depth == 0: argument++; break;
            }
        }
        return null;
    }

    private static string WordBefore(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int start = caret;
        while (start > 0 && (char.IsAsciiLetterOrDigit(text[start - 1]) || text[start - 1] is '.' or '_'))
            start--;
        return text[start..caret];
    }
}
