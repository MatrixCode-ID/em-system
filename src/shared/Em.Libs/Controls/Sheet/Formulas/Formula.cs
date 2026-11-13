// Ported from GridCraft (formula/src/parser.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
// Upstream sync: GridCraft crates/formula/src/parser.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926.
using System.Globalization;
using System.Text.RegularExpressions;
using Em.Controls.Sheet.Cells;

namespace Em.Controls.Sheet.Formulas;

internal abstract record Formula
{
    internal sealed record Literal(CellValue Value) : Formula;
    internal sealed record Reference(string? Sheet, CellRange Area, bool Range) : Formula;
    internal sealed record Unary(string Operator, Formula Operand) : Formula;
    internal sealed record Binary(string Operator, Formula Left, Formula Right) : Formula;
    internal sealed record Call(string Name, Formula[] Arguments) : Formula;
    internal sealed record NamedReference(string Text) : Formula;
}

internal sealed partial class FormulaParser
{
    private readonly MatchCollection tokens;
    private int index;
    private int depth;

    private FormulaParser(string text) => tokens = Tokens().Matches(text);

    internal static Formula Parse(string text)
    {
        if (text.Length > 8192)
            throw new FormatException("The formula is too long.");
        var parser = new FormulaParser(text);
        var result = parser.Expression(0);
        if (parser.Peek.Length != 0)
            throw new FormatException("Unexpected formula token.");
        ValidateTreeDepth(result);
        return result;
    }

    private static void ValidateTreeDepth(Formula root)
    {
        var pending = new Stack<(Formula Node, int Depth)>();
        pending.Push((root, 1));
        while (pending.TryPop(out var entry))
        {
            if (entry.Depth > 128)
                throw new FormatException("The formula is too deeply nested.");
            int next = entry.Depth + 1;
            switch (entry.Node)
            {
                case Formula.Unary unary:
                    pending.Push((unary.Operand, next));
                    break;
                case Formula.Binary binary:
                    pending.Push((binary.Left, next));
                    pending.Push((binary.Right, next));
                    break;
                case Formula.Call call:
                    foreach (var argument in call.Arguments)
                        pending.Push((argument, next));
                    break;
            }
        }
    }

    private string Peek => index < tokens.Count ? tokens[index].Value : "";
    private string Take() => index < tokens.Count ? tokens[index++].Value : throw new FormatException("Incomplete formula.");

    private bool Eat(string token)
    {
        if (Peek != token)
            return false;
        index++;
        return true;
    }

    private void Expect(string token)
    {
        if (!Eat(token))
            throw new FormatException("Expected " + token);
    }

    private Formula Expression(int minimum)
    {
        if (++depth > 128)
            throw new FormatException("The formula is too deeply nested.");
        var left = Atom();
        while (true)
        {
            if (Peek == "%" && minimum <= 7)
            {
                left = new Formula.Unary(Take(), left);
                continue;
            }
            int precedence = Peek switch
            {
                "=" or "<>" or "<" or ">" or "<=" or ">=" => 1,
                "&" => 2, "+" or "-" => 3, "*" or "/" => 4, "^" => 5, _ => 0
            };
            if (precedence == 0 || precedence < minimum)
                break;

            string operation = Take();
            left = new Formula.Binary(operation, left, Expression(precedence + 1));
        }
        depth--;
        return left;
    }

    private Formula Atom()
    {
        string token = Take();
        if (token == "(")
        {
            var inner = Expression(0);
            Expect(")");
            return inner;
        }
        if (token is "+" or "-")
            return new Formula.Unary(token, Expression(6));
        if (token[0] == '"')
        {
            if (token.Length < 2)
                throw new FormatException("Unterminated text.");
            return new Formula.Literal(CellValue.From(token[1..^1].Replace("\"\"", "\"")));
        }
        if (token[0] == '#')
            return new Formula.Literal(CellValue.From(token.ToUpperInvariant() switch
            {
                "#NULL!" => CellError.Null, "#DIV/0!" => CellError.DivideByZero, "#VALUE!" => CellError.Value,
                "#REF!" => CellError.Reference, "#NAME?" => CellError.Name, "#NUM!" => CellError.Number,
                "#N/A" => CellError.NotAvailable, _ => CellError.Value
            }));
        if (Eat("("))
        {
            var arguments = new List<Formula>();
            if (!Eat(")"))
            {
                do
                    arguments.Add(Peek is "," or ")" ? new Formula.Literal(default) : Expression(0));
                while (Eat(","));
                Expect(")");
            }
            return new Formula.Call(token.ToUpperInvariant().Replace("_XLFN.", "").Replace("_XLWS.", ""), arguments.ToArray());
        }

        string? sheet = null;
        if (Eat("!"))
        {
            sheet = token[0] == '\'' && token.Length >= 2 ? token[1..^1].Replace("''", "'") : token;
            token = Take();
        }
        if (CellAddress.TryParse(token, out var address))
        {
            bool range = Eat(":");
            var end = address;
            if (range && !CellAddress.TryParse(Take(), out end))
                throw new FormatException("Invalid range.");
            return new Formula.Reference(sheet, new(address, end), range);
        }
        if (sheet is not null)
            return new Formula.Literal(CellValue.From(CellError.Reference));
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return new Formula.Literal(CellValue.From(number));
        if (bool.TryParse(token, out bool boolean))
            return new Formula.Literal(CellValue.From(boolean));
        return new Formula.NamedReference(token);
    }

    [GeneratedRegex(""" "(?:""|[^"])*"|'(?:''|[^'])*'|\#(?:DIV/0!|NULL!|VALUE!|REF!|NAME\?|NUM!|N/A|CIRC!)|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?|\$?[\p{L}_][\p{L}\p{N}_.]*(?:\$\d+)?|<=|>=|<>|\S """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();
}
