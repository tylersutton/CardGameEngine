using Core;
using Godot;

/// The How to play window: the game's description and its "how_to_play" lines ("# " a heading, "- " a bullet), then a
/// reminder of how the table works, the same for every game.
public partial class HowToPlayWindow : AcceptDialog
{
    static readonly Vector2I WindowSize = new(700, 580);
    const int HeadingFontSize = 18;
    const string HeadingColor = "#ffd966";
    const string HeadingMark = "# ";
    const string BulletMark = "- ";

    static readonly string[] TableHelp =
    [
        "# On the table",
        "- The bar under the table says what you can do now, or who the game is waiting for. Gold buttons can be pressed now.",
        "- Drag cards and pieces to move them; the outline turns green where a move is allowed. Click a card, piece or space to do what the game does there.",
        "- When you're choosing, what you can pick is outlined in yellow. Anything else to pick appears as buttons.",
        "- The log on the right says what everyone did. Ctrl+Z undoes your last move.",
    ];

    readonly RichTextLabel text = new() { BbcodeEnabled = true, ScrollActive = true, SelectionEnabled = true };

    public HowToPlayWindow()
    {
        OkButtonText = "Close";
        AddChild(text);
    }

    public void ShowRules(GameRules rules)
    {
        Title = "How to play " + rules.Title;
        List<string> lines = rules.HowToPlay.Count > 0 ? [.. rules.HowToPlay] : ["This game doesn't explain itself yet: add \"how_to_play\" to its game file."];
        text.Text = string.Join("\n", new[] { Escaped(rules.Description), "" }.Concat(lines.Concat(TableHelp).Select(Line)));
        text.ScrollToLine(0);
        PopupCentered(WindowSize);
    }

    static string Line(string line)
    {
        if (line.StartsWith(HeadingMark))
        {
            return $"\n[font_size={HeadingFontSize}][color={HeadingColor}][b]{Escaped(line[HeadingMark.Length..])}[/b][/color][/font_size]";
        }
        if (line.StartsWith(BulletMark))
        {
            return "[indent]• " + Escaped(line[BulletMark.Length..]) + "[/indent]";
        }
        return Escaped(line);
    }

    /// Square brackets in the game's own text are shown as they are, not read as formatting.
    static string Escaped(string line) => line.Replace("[", "[lb]");
}
