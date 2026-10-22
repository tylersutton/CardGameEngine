using System.Text.RegularExpressions;

namespace Editing;

/// What each building block does, read from the building-block tables in docs/reference.md, so the editor can show
/// it beside steps without saying it a second time. (An exported build without docs/ simply has no help.)
public partial class BlockHelp
{
    public const string Reference = "res://docs/reference.md";

    /// One row of a building-block table. Title is its first cell, e.g. "mark(cards, 'key', value), unmark(cards,
    /// 'key')"; Names are the building blocks it covers.
    public record Entry(string Category, string Title, string Text, List<string> Names);

    /// In the reference's order.
    public List<Entry> Entries { get; } = [];
    /// Building block name -> what it does.
    public Dictionary<string, string> ByName { get; } = [];

    public BlockHelp(string referenceText)
    {
        string category = "";
        bool inBlocks = false;
        foreach (string line in referenceText.Split('\n'))
        {
            if (line.StartsWith("## "))
            {
                inBlocks = line.Trim() == "## Building blocks";
            }
            else if (inBlocks && line.StartsWith("**"))
            {
                category = line.Trim().Trim('*');
            }
            else if (inBlocks && line.StartsWith("| `"))
            {
                string[] cells = line.Split('|');
                string title = cells[1].Trim().Replace("`", "");
                string text = Link().Replace(cells[2].Trim(), "$1").Replace("`", "").Replace("**", "");
                List<string> names = BlockName().Matches(title).Select(found => found.Groups[1].Value).ToList();
                Entries.Add(new(category, title, text, names));
            }
        }
        foreach (Entry entry in Entries)
        {
            foreach (string block in entry.Names)
            {
                ByName[block] = entry.Text;
            }
        }
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"([A-Za-z_][A-Za-z0-9_]*)\(")]
    private static partial Regex BlockName();
}
