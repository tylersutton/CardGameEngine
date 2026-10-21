namespace Core;

/// Collects what happened into readable lines: card moves and flips are grouped one line per kind
/// ("deck → hand_1: 2♦, 7♥"), and identical lines in a row are shown once with a count ("(×17)").
public class GameLog
{
    public event Action<string>? Logged;

    /// While a bot tries moves out, nothing is logged.
    public bool Quiet { get; set; }

    readonly Dictionary<string, List<string>> items = [];
    string lineText = "";
    int repeats;

    public void Line(string text)
    {
        if (text == lineText && items.Count == 0)
        {
            repeats++;
            return;
        }
        Flush();
        lineText = text;
        repeats = 1;
    }

    /// An item of a grouped line, e.g. a card under "deck → hand_1". An empty name is a card the viewer can't see.
    public void Item(string kind, string name)
    {
        FlushLine();
        if (!items.TryGetValue(kind, out List<string>? names))
        {
            items[kind] = names = [];
        }
        names.Add(name);
    }

    public void Flush()
    {
        FlushLine();
        FlushItems();
    }

    public void FlushItems()
    {
        foreach ((string kind, List<string> all) in items)
        {
            List<string> names = all.Where(name => name != "").ToList();
            int hidden = all.Count - names.Count;
            if (hidden > 0)
            {
                names.Add($"{hidden} hidden card{(hidden == 1 ? "" : "s")}");
            }
            Announce($"  {kind}: {string.Join(", ", names)}");
        }
        items.Clear();
    }

    /// Drops anything not yet shown, e.g. after a bot's silent try-out.
    public void Discard()
    {
        items.Clear();
        lineText = "";
    }

    void FlushLine()
    {
        if (lineText.Length > 0)
        {
            Announce(lineText + (repeats > 1 ? $" (×{repeats})" : ""));
        }
        lineText = "";
    }

    void Announce(string text)
    {
        if (!Quiet)
        {
            Logged?.Invoke(text);
        }
    }
}
