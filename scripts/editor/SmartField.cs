using System.Text.RegularExpressions;
using Core;
using Godot;

/// What a SmartField knows about the game: the names steps can use, the deck's columns, the names each building
/// block takes in quotes ({"offer": ["Draw", "Pass"]}), conditions with parameters as they're called
/// ({"points": "points(cards)"}), and how to check a step (its problems; none when it's fine).
public record StepNames(List<string> Names, List<string> Columns, Func<string, List<string>> Check, Dictionary<string, List<string>> QuotedNames, Dictionary<string, string> Signatures);

/// A box for one step or condition: suggests names as you type (zones, variables, conditions, building blocks, card
/// columns after "card.", and inside quotes the names a building block takes there, like the actions in
/// offer('...')), shows a building block's (or a condition's) arguments inside its brackets, and says what's wrong
/// with the text. Building blocks, names, numbers and quoted text are colored. Sends Committed(text) when you leave
/// the box or press Enter.
public partial class SmartField : VBoxContainer
{
    static readonly Color NameColor = new("9cdcfe");

    public event Action<string>? Committed;

    internal readonly CodeEdit Code = new()
    {
        ScrollFitContentHeight = true, WrapMode = TextEdit.LineWrappingMode.Boundary, CodeCompletionEnabled = true,
        AutoBraceCompletionEnabled = true, CustomMinimumSize = new Vector2(120, 0),
    };
    readonly Label hint = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, ThemeTypeVariation = "DimLabel", Visible = false };
    readonly StepNames known;
    string lastSent;

    public SmartField(string text, StepNames names)
    {
        known = names;
        lastSent = text;
        Code.Text = text;
        Code.SyntaxHighlighter = Highlighter();
        Code.TextChanged += OnTextChanged;
        Code.CaretChanged += ShowHint;
        Code.FocusExited += Send;
        Code.GuiInput += @event =>
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Enter } && Code.GetCodeCompletionOptions().Count == 0)
            {
                Code.AcceptEvent();
                Send();
            }
        };
        Code.CodeCompletionRequested += Suggest;
        AddChild(Code);
        AddChild(hint);
        ShowHint();
    }

    void Send()
    {
        string text = Code.Text.Trim();
        if (text != lastSent)
        {
            lastSent = text;
            Committed?.Invoke(text);
        }
    }

    void OnTextChanged()
    {
        string before = TextBeforeCaret();
        if (before.Length > 0 && (before[^1] == '.' || char.IsAsciiLetter(before[^1]) || before[^1] == '_' || QuotedBlock(before).Length > 0))
        {
            Code.RequestCodeCompletion();
        }
        ShowHint();
    }

    string TextBeforeCaret() => Code.GetLine(Code.GetCaretLine())[..Code.GetCaretColumn()];

    /// Suggestions for the word being typed: card columns after "card.", otherwise names and building blocks.
    internal void Suggest()
    {
        string before = TextBeforeCaret();
        string quotedBlock = QuotedBlock(before);
        if (quotedBlock.Length > 0)
        {
            string quoted = before[(before.LastIndexOf('\'') + 1)..].ToLowerInvariant();
            foreach (string name in known.QuotedNames[quotedBlock].Where(name => name.ToLowerInvariant().StartsWith(quoted)))
            {
                Code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.PlainText, name, name);
            }
            Code.UpdateCodeCompletionOptions(true);
            return;
        }
        string typed = Regex.Match(before, "[A-Za-z_][A-Za-z0-9_]*$").Value;
        string start = before[..^typed.Length];
        if (start.EndsWith("card."))
        {
            foreach (string column in known.Columns.Where(column => column.ToLowerInvariant().StartsWith(typed.ToLowerInvariant())))
            {
                Code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Member, column, column);
            }
        }
        else if (!start.EndsWith('.'))
        {
            foreach (string name in known.Names.Concat(StepParser.Keywords).Where(name => name.StartsWith(typed)))
            {
                Code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Variable, name, name);
            }
            foreach (string block in Game.BuildingBlocks.Names.Where(block => block.StartsWith(typed)))
            {
                Code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Function, Game.BuildingBlocks.Signature(block), block + "(");
            }
        }
        Code.UpdateCodeCompletionOptions(true);
    }

    /// Under the box: the arguments of the building block the caret is inside, then anything wrong with the text.
    void ShowHint()
    {
        List<string> lines = [];
        string block = CallAround(TextBeforeCaret());
        if (Game.BuildingBlocks.Has(block))
        {
            string help = EditorScreen.Help.ByName.GetValueOrDefault(block, "");
            lines.Add(Game.BuildingBlocks.Signature(block) + (help.Length > 0 ? ": " + help : ""));
        }
        else if (known.Signatures.TryGetValue(block, out string? signature))
        {
            lines.Add(signature);
        }
        List<string> problems = Code.Text.Trim().Length > 0 ? known.Check(Code.Text) : [];
        lines.AddRange(problems);
        hint.Text = string.Join("\n", lines);
        hint.Visible = lines.Count > 0;
        hint.AddThemeColorOverride("font_color", problems.Count > 0 ? EditorTheme.Error : EditorTheme.TextDim);
    }

    /// The name whose brackets the text ends inside, e.g. "draw" in "draw(deck, |", or "".
    static string CallAround(string before)
    {
        int depth = 0;
        for (int i = before.Length - 1; i >= 0; i--)
        {
            if (before[i] == ')')
            {
                depth++;
            }
            else if (before[i] == '(')
            {
                if (depth == 0)
                {
                    return Regex.Match(before[..i], @"([A-Za-z_][A-Za-z0-9_]*)\s*$").Groups[1].Value;
                }
                depth--;
            }
        }
        return "";
    }

    /// When the text ends inside quotes given to a building block that takes names there (offer('Dr|), that block.
    string QuotedBlock(string before)
    {
        if (before.Count(character => character == '\'') % 2 == 0)
        {
            return "";
        }
        string block = CallAround(before[..before.LastIndexOf('\'')]);
        return known.QuotedNames.ContainsKey(block) ? block : "";
    }

    CodeHighlighter Highlighter()
    {
        CodeHighlighter colors = new()
        {
            NumberColor = EditorTheme.NumberColor, SymbolColor = EditorTheme.Text, FunctionColor = EditorTheme.Function, MemberVariableColor = NameColor,
        };
        colors.AddColorRegion("'", "'", EditorTheme.StringColor);
        colors.AddColorRegion("\"", "\"", EditorTheme.StringColor);
        foreach (string name in known.Names.Where(name => !StepParser.Keywords.Contains(name)).Distinct())
        {
            colors.AddKeywordColor(name, NameColor);
        }
        foreach (string keyword in StepParser.Keywords)
        {
            colors.AddKeywordColor(keyword, EditorTheme.Keyword);
        }
        return colors;
    }
}
