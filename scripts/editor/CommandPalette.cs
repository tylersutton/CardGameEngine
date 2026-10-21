using Godot;

/// One thing the command palette can do: what it's called, a word or two beside it, its icon, and what it does.
public record PaletteEntry(string Text, string Detail, string Icon, Action Run);

/// Ctrl+P: a search box over every command and every part of the game. Type a few letters (in order, gaps allowed),
/// then Enter or click to do it.
public partial class CommandPalette : PopupPanel
{
    const int Width = 560;
    const int ListHeight = 320;

    internal readonly LineEdit Search = new() { PlaceholderText = "Search commands, zones, actions..." };
    readonly ItemList list = new() { CustomMinimumSize = new Vector2(Width, ListHeight), FocusMode = Control.FocusModeEnum.None };
    List<PaletteEntry> entries = [];
    internal List<PaletteEntry> Shown { get; private set; } = [];

    public override void _Ready()
    {
        VBoxContainer column = new();
        Search.RightIcon = EditorIcons.Icon("search", EditorTheme.TextDim);
        Search.TextChanged += _ => Filter();
        Search.GuiInput += OnSearchKey;
        Search.TextSubmitted += _ => RunEntry(Chosen());
        // ItemClicked also fires for the mouse wheel, which would run whatever was scrolled past.
        list.ItemClicked += (index, _, button) =>
        {
            if (button == (long)MouseButton.Left)
            {
                RunEntry((int)index);
            }
        };
        column.AddChild(Search);
        column.AddChild(list);
        AddChild(column);
    }

    public void Open(List<PaletteEntry> allEntries)
    {
        entries = allEntries;
        Search.Text = "";
        Filter();
        Vector2 area = GetParent<Control>().GetViewport().GetVisibleRect().Size;
        Popup(new Rect2I((int)((area.X - Width) / 2), 56, Width, 0));
        Search.GrabFocus();
    }

    internal void Filter()
    {
        string typed = Search.Text.ToLowerInvariant().Replace(" ", "");
        // Equal scores keep the order they were given in.
        Shown = entries.Select((entry, i) => (Score: Score(typed, (entry.Text + entry.Detail).ToLowerInvariant()), Index: i, Entry: entry))
            .Where(scored => scored.Score >= 0).OrderBy(scored => scored.Score).ThenBy(scored => scored.Index).Select(scored => scored.Entry).ToList();
        list.Clear();
        foreach (PaletteEntry entry in Shown)
        {
            int index = list.AddItem(entry.Detail.Length > 0 ? $"{entry.Text}    {entry.Detail}" : entry.Text, EditorIcons.Icon(entry.Icon, EditorTheme.TextDim));
            list.SetItemTooltip(index, entry.Text);
        }
        if (Shown.Count > 0)
        {
            list.Select(0);
        }
    }

    /// How well the typed letters match: lower is better, -1 is no match. They must all appear in order; words that
    /// start with them, or contain them together, come first.
    static int Score(string typed, string text)
    {
        if (typed.Length == 0)
        {
            return 0;
        }
        int together = text.Replace(" ", "").IndexOf(typed, StringComparison.Ordinal);
        if (together >= 0)
        {
            return together;
        }
        int at = 0;
        int gaps = 0;
        foreach (char letter in typed)
        {
            int found = text.IndexOf(letter, at);
            if (found < 0)
            {
                return -1;
            }
            gaps += found - at;
            at = found + 1;
        }
        return 100 + gaps;
    }

    void OnSearchKey(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key || Shown.Count == 0)
        {
            return;
        }
        int step = key.Keycode switch
        {
            Key.Down => 1,
            Key.Up => -1,
            _ => 0,
        };
        if (key.Keycode == Key.Escape)
        {
            Hide();
            Search.AcceptEvent();
            return;
        }
        if (step != 0)
        {
            list.Select(Mathf.PosMod(Chosen() + step, Shown.Count));
            list.EnsureCurrentIsVisible();
            Search.AcceptEvent();
        }
    }

    int Chosen()
    {
        int[] selected = list.GetSelectedItems();
        return selected.Length > 0 ? selected[0] : 0;
    }

    void RunEntry(int index)
    {
        if (index < Shown.Count)
        {
            Hide();
            Shown[index].Run();
        }
    }
}
