using System.Globalization;
using System.Text;

namespace Core;

/// Turns a step's text into a tree of StepNodes for StepEvaluator. A step that doesn't parse becomes an
/// UnreadableNode holding the first problem found.
public class StepParser
{
    public static readonly string[] Comparisons = ["==", "!=", "<", ">", "<=", ">="];

    static readonly string[] Keywords = ["and", "or", "not", "in", "if", "else", "true", "false", "null"];
    static readonly Dictionary<string, object?> Literals = new() { ["true"] = true, ["false"] = false, ["null"] = null };
    static readonly string[] Assignments = ["=", "+=", "-="];
    // Longest first, so "==" isn't read as "=" twice.
    static readonly string[] Symbols = ["==", "!=", "<=", ">=", "+=", "-=", "<", ">", "=", "+", "-", "*", "/", "%", "(", ")", "[", "]", ",", "."];
    const string End = "";

    enum TokenKind { Value, Name, Keyword, Symbol }

    record Token(TokenKind Kind, string Text = "", object? Value = null);

    class ParseError(string message) : Exception(message);

    readonly List<Token> tokens = [];
    int position;

    public static StepNode Parse(string text)
    {
        StepParser parser = new();
        try
        {
            parser.ReadTokens(text);
            StepNode tree = parser.AssignmentOrExpression();
            if (parser.Peek() != End)
            {
                throw new ParseError($"unexpected {parser.Describe(parser.Peek())}");
            }
            return tree;
        }
        catch (ParseError error)
        {
            return new UnreadableNode(error.Message);
        }
    }

    void ReadTokens(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char character = text[i];
            if (char.IsWhiteSpace(character))
            {
                i++;
            }
            else if (char.IsAsciiDigit(character))
            {
                int start = i;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
                {
                    i++;
                }
                string digits = text[start..i];
                object number = digits.Contains('.') ? (object)double.Parse(digits, CultureInfo.InvariantCulture) : long.Parse(digits);
                tokens.Add(new Token(TokenKind.Value, Value: number));
            }
            else if (IsNameStart(character))
            {
                int start = i;
                while (i < text.Length && (IsNameStart(text[i]) || char.IsAsciiDigit(text[i])))
                {
                    i++;
                }
                string word = text[start..i];
                if (Literals.TryGetValue(word, out object? literal))
                {
                    tokens.Add(new Token(TokenKind.Value, Value: literal));
                }
                else
                {
                    tokens.Add(new Token(Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Name, word));
                }
            }
            else if (character == '\'')
            {
                i = ReadText(text, i);
            }
            else if (character == '"')
            {
                throw new ParseError("text goes in single quotes, e.g. 'discard'");
            }
            else if (character == '!' && !text[i..].StartsWith("!="))
            {
                throw new ParseError("use not instead of !, e.g. not covered(card)");
            }
            else
            {
                string symbol = Symbols.FirstOrDefault(each => string.CompareOrdinal(text, i, each, 0, each.Length) == 0)
                    ?? throw new ParseError($"unexpected \"{character}\"");
                tokens.Add(new Token(TokenKind.Symbol, symbol));
                i += symbol.Length;
            }
        }
    }

    /// Reads 'text' (a \ before a quote or backslash keeps it, as in 'King\'s Court') and returns where it ends.
    int ReadText(string text, int start)
    {
        StringBuilder value = new();
        int i = start + 1;
        while (i < text.Length && text[i] != '\'')
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i++;
            }
            value.Append(text[i]);
            i++;
        }
        if (i >= text.Length)
        {
            throw new ParseError($"the text starting {text.Substring(start, Math.Min(12, text.Length - start))} has no closing '");
        }
        tokens.Add(new Token(TokenKind.Value, Value: value.ToString()));
        return i + 1;
    }

    /// A whole step may change a variable: score = 0, score += 2, passes -= 1.
    StepNode AssignmentOrExpression()
    {
        StepNode target = Expression();
        if (!Assignments.Contains(Peek()))
        {
            return target;
        }
        string op = Take();
        return target switch
        {
            IndexNode { Target: NameNode name } index => new AssignNode(name.Name, index.Index, op, Expression()),
            NameNode name => new AssignNode(name.Name, null, op, Expression()),
            _ => throw new ParseError($"only a variable can be changed with {op}"),
        };
    }

    // Lowest precedence first: x if condition else y, or, and, not, comparisons, + -, * / %, unary -, then calls,
    // .members, [indexes] and single values.
    StepNode Expression()
    {
        StepNode result = Or();
        if (Peek() != "if")
        {
            return result;
        }
        Take();
        StepNode condition = Or();
        Expect("else", "after the condition of \"x if condition else y\"");
        return new ChooseNode(condition, result, Expression());
    }

    StepNode Or()
    {
        StepNode result = And();
        while (Peek() == "or")
        {
            Take();
            result = new BinaryNode("or", result, And());
        }
        return result;
    }

    StepNode And()
    {
        StepNode result = Not();
        while (Peek() == "and")
        {
            Take();
            result = new BinaryNode("and", result, Not());
        }
        return result;
    }

    StepNode Not()
    {
        if (Peek() != "not")
        {
            return Comparison();
        }
        Take();
        return new UnaryNode("not", Not());
    }

    /// Comparisons, including "x in list" and "x not in list".
    StepNode Comparison()
    {
        StepNode result = Sum();
        while (Comparisons.Contains(Peek()) || Peek() == "in" || Peek() == "not" && PeekAfter() == "in")
        {
            string op = Take();
            if (op == "not")
            {
                Take();
                op = "not in";
            }
            result = new BinaryNode(op, result, Sum());
        }
        return result;
    }

    StepNode Sum()
    {
        StepNode result = Product();
        while (Peek() is "+" or "-")
        {
            result = new BinaryNode(Take(), result, Product());
        }
        return result;
    }

    StepNode Product()
    {
        StepNode result = Negation();
        while (Peek() is "*" or "/" or "%")
        {
            result = new BinaryNode(Take(), result, Negation());
        }
        return result;
    }

    StepNode Negation()
    {
        if (Peek() != "-")
        {
            return Postfix();
        }
        Take();
        return new UnaryNode("-", Negation());
    }

    StepNode Postfix()
    {
        StepNode result = Single();
        while (true)
        {
            if (Peek() == "(")
            {
                if (result is not NameNode name)
                {
                    throw new ParseError("only a building block can be called with ( )");
                }
                Take();
                result = new CallNode(name.Name, Items(")"));
            }
            else if (Peek() == ".")
            {
                Take();
                Token? member = position < tokens.Count ? tokens[position] : null;
                if (member is not { Kind: TokenKind.Name or TokenKind.Keyword })
                {
                    throw new ParseError("expected a column name after \".\"");
                }
                position++;
                result = new MemberNode(result, member.Text);
            }
            else if (Peek() == "[")
            {
                Take();
                StepNode index = Expression();
                Expect("]", "after the position in [ ]");
                result = new IndexNode(result, index);
            }
            else
            {
                return result;
            }
        }
    }

    StepNode Single()
    {
        if (position >= tokens.Count)
        {
            throw new ParseError("the step ends too soon");
        }
        Token token = tokens[position];
        position++;
        switch (token)
        {
            case { Kind: TokenKind.Value }:
                return new ValueNode(token.Value);
            case { Kind: TokenKind.Name }:
                return new NameNode(token.Text);
            case { Kind: TokenKind.Symbol, Text: "(" }:
                StepNode inner = Expression();
                Expect(")", "to close the (");
                return inner;
            case { Kind: TokenKind.Symbol, Text: "[" }:
                return new ListNode(Items("]"));
        }
        position--;
        throw new ParseError($"unexpected {Describe(Peek())}");
    }

    /// Comma-separated expressions up to the closing bracket, which is taken too.
    List<StepNode> Items(string closing)
    {
        List<StepNode> items = [];
        if (Peek() == closing)
        {
            Take();
            return items;
        }
        while (true)
        {
            items.Add(Expression());
            if (Peek() == ",")
            {
                Take();
                continue;
            }
            Expect(closing, "or a comma between items");
            return items;
        }
    }

    /// The next token's text ("+", "and", ...), "value" or "name" for those, or End.
    string Peek()
    {
        if (position >= tokens.Count)
        {
            return End;
        }
        Token token = tokens[position];
        return token.Kind switch
        {
            TokenKind.Value => "value",
            TokenKind.Name => "name",
            _ => token.Text,
        };
    }

    string PeekAfter()
    {
        position++;
        string after = Peek();
        position--;
        return after;
    }

    string Take()
    {
        string taken = Peek();
        position++;
        return taken;
    }

    void Expect(string wanted, string context)
    {
        if (Peek() != wanted)
        {
            throw new ParseError($"expected \"{wanted}\" {context}, but found {Describe(Peek())}");
        }
        Take();
    }

    string Describe(string token)
    {
        if (token == End)
        {
            return "the end of the step";
        }
        if (token is "value" or "name")
        {
            Token found = tokens[position];
            return $"\"{(token == "name" ? found.Text : Values.Format(found.Value))}\"";
        }
        return $"\"{token}\"";
    }

    static bool IsNameStart(char character) => character == '_' || char.IsAsciiLetter(character);
}
