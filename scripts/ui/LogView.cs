using Core;
using Godot;

/// The action log beside the table, with errors in red. Without designer info, only what players need to follow
/// the game (GameLog.ForPlayers).
public partial class LogView : RichTextLabel
{
    static readonly Color ErrorColor = new(1, 0.45f, 0.45f);

    /// Every line so far, kept to show again when designer info is turned on or off.
    readonly List<(string Text, bool IsError)> lines = [];
    bool designerInfo;

    public void ShowDesignerInfo(bool shown)
    {
        designerInfo = shown;
        Clear();
        foreach ((string text, bool isError) in lines)
        {
            Show(text, isError);
        }
    }

    public void Add(string text)
    {
        lines.Add((text, false));
        Show(text, false);
    }

    public void AddError(string text)
    {
        lines.Add((text, true));
        Show(text, true);
    }

    public void ClearLines()
    {
        lines.Clear();
        Clear();
    }

    void Show(string text, bool isError)
    {
        if (isError)
        {
            PushColor(ErrorColor);
            AddText(text + "\n");
            Pop();
        }
        else if (designerInfo || GameLog.ForPlayers(text))
        {
            AddText(text + "\n");
        }
    }
}
