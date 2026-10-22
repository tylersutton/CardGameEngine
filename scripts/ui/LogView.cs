using Godot;

/// The action log beside the table, with errors in red.
public partial class LogView : RichTextLabel
{
    static readonly Color ErrorColor = new(1, 0.45f, 0.45f);

    public void Add(string text)
    {
        AddText(text + "\n");
    }

    public void AddError(string text)
    {
        PushColor(ErrorColor);
        AddText(text + "\n");
        Pop();
    }
}
