using Godot;

/// The Game file tab: the game file's own text, colored, with line numbers and folding. Typing here sends
/// Edited(text) after a short pause; the editor then updates the forms and the table from it, and shows changes made
/// in the forms back here.
public partial class GameFileView : CodeEdit
{
    const double EditDelay = 0.4;

    public event Action<string>? Edited;

    public int ErrorLine { get; private set; } = -1;

    readonly Timer pause = new() { OneShot = true };

    public override void _Ready()
    {
        GuttersDrawLineNumbers = true;
        GuttersDrawFoldGutter = true;
        LineFolding = true;
        HighlightCurrentLine = true;
        HighlightAllOccurrences = true;
        AutoBraceCompletionEnabled = true;
        IndentAutomatic = true;
        MinimapDraw = true;
        MinimapWidth = 70;
        DrawTabs = false;
        CaretBlink = true;
        ClearCommentDelimiters();
        ClearStringDelimiters();
        AddCommentDelimiter("//", "", true);
        AddCommentDelimiter("/*", "*/");
        AddStringDelimiter("\"", "\"");
        SyntaxHighlighter = Highlighter();
        AddThemeStyleboxOverride("normal", EditorTheme.Box(EditorTheme.Background, margins: new Vector2(4, 4)));
        AddThemeStyleboxOverride("focus", EditorTheme.Box(EditorTheme.Background, margins: new Vector2(4, 4)));
        pause.Timeout += () => Edited?.Invoke(Text);
        AddChild(pause);
        TextChanged += () => pause.Start(EditDelay);
    }

    /// Shows the text, keeping the caret and scroll where they were (unless it's the same text already).
    public void ShowText(string newText)
    {
        if (Text == newText)
        {
            return;
        }
        int line = GetCaretLine();
        int column = GetCaretColumn();
        double scrolled = ScrollVertical;
        Text = newText;
        ClearUndoHistory();
        SetCaretLine(Math.Min(line, GetLineCount() - 1));
        SetCaretColumn(column);
        ScrollVertical = scrolled;
        pause.Stop();
    }

    /// Puts the caret at a place in the text (a character offset) and scrolls it into view.
    public void GoTo(int offset)
    {
        int line = Text[..Math.Min(offset, Text.Length)].Count(character => character == '\n');
        SetCaretLine(line);
        SetCaretColumn(GetLine(line).Length - GetLine(line).TrimStart().Length);
        CenterViewportToCaret();
    }

    /// Marks the line the file can't be read at (0-based), or none with -1.
    public void ShowErrorLine(int line)
    {
        if (ErrorLine >= 0 && ErrorLine < GetLineCount())
        {
            SetLineBackgroundColor(ErrorLine, Colors.Transparent);
        }
        ErrorLine = line;
        if (line >= 0 && line < GetLineCount())
        {
            SetLineBackgroundColor(line, new Color(EditorTheme.Error, 0.22f));
        }
    }

    static CodeHighlighter Highlighter()
    {
        CodeHighlighter colors = new()
        {
            NumberColor = EditorTheme.NumberColor, SymbolColor = EditorTheme.Text, FunctionColor = EditorTheme.Text, MemberVariableColor = EditorTheme.Text,
        };
        colors.AddColorRegion("\"", "\"", EditorTheme.StringColor);
        colors.AddColorRegion("//", "", EditorTheme.Comment, true);
        colors.AddColorRegion("/*", "*/", EditorTheme.Comment);
        foreach (string keyword in new[] { "true", "false", "null" })
        {
            colors.AddKeywordColor(keyword, EditorTheme.Keyword);
        }
        return colors;
    }
}
