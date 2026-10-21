namespace Core;

/// One thing a player does, as plain data that can be sent, recorded and replayed (Game.Apply). The type says
/// what it is, and the other fields are what that type needs:
/// drag (Card, To, Index), click and double_click (Card), click_empty (Zone), perform (Action), choose (Option),
/// finish, reorder (Card, Index) and undo. With Player, it's refused unless that player may act now; without,
/// it's someone at this screen.
public record PlayerInput(string Type)
{
    public int? Card { get; init; }
    public string? To { get; init; }
    public int? Index { get; init; }
    public string? Zone { get; init; }
    public string? Action { get; init; }
    /// A value from the choice's options, or a zone's name.
    public object? Option { get; init; }
    public int? Player { get; init; }

    public static PlayerInput Drag(Card card, Zone to) => new("drag") { Card = card.Id, To = to.Name };

    public static PlayerInput Click(Card card) => new("click") { Card = card.Id };

    public static PlayerInput DoubleClick(Card card) => new("double_click") { Card = card.Id };

    public static PlayerInput ClickEmpty(Zone zone) => new("click_empty") { Zone = zone.Name };

    public static PlayerInput Perform(string action) => new("perform") { Action = action };

    public static PlayerInput Choose(object? option) => new("choose") { Option = option };

    public static PlayerInput Finish() => new("finish");

    /// The input as plain data ({"type": "drag", "card": 12, "to": "pile"}), e.g. to send it to another machine.
    public Dictionary<string, object?> ToPlain()
    {
        Dictionary<string, object?> plain = new() { ["type"] = Type };
        AddIfSet(plain, "card", Card);
        AddIfSet(plain, "to", To);
        AddIfSet(plain, "index", Index);
        AddIfSet(plain, "zone", Zone);
        AddIfSet(plain, "action", Action);
        AddIfSet(plain, "option", Option);
        AddIfSet(plain, "player", Player);
        return plain;
    }

    /// An input from plain data, as ToPlain gives it or JSON reads it (numbers may be decimals: 12.0).
    public static PlayerInput FromPlain(Dictionary<string, object?> plain) => new((string)plain["type"]!)
    {
        Card = WholeNumber(plain.GetValueOrDefault("card")),
        To = plain.GetValueOrDefault("to") as string,
        Index = WholeNumber(plain.GetValueOrDefault("index")),
        Zone = plain.GetValueOrDefault("zone") as string,
        Action = plain.GetValueOrDefault("action") as string,
        Option = plain.GetValueOrDefault("option"),
        Player = WholeNumber(plain.GetValueOrDefault("player")),
    };

    static void AddIfSet(Dictionary<string, object?> plain, string key, object? value)
    {
        if (value != null)
        {
            plain[key] = value is int whole ? (long)whole : value;
        }
    }

    static int? WholeNumber(object? value) => Values.IsNumber(value) ? (int)Values.ToDouble(value) : null;
}
