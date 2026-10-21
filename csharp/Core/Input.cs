namespace Core;

/// One thing a player does, as plain data that can be sent, recorded and replayed (Game.Apply). The type says
/// what it is, and the other fields are what that type needs:
/// drag (Card, To, Index), click and double_click (Card), click_empty (Zone), perform (Action), choose (Option),
/// finish, reorder (Card, Index) and undo. With Player, it's refused unless that player may act now; without,
/// it's someone at this screen.
public record Input(string Type)
{
    public int? Card { get; init; }
    public string? To { get; init; }
    public int? Index { get; init; }
    public string? Zone { get; init; }
    public string? Action { get; init; }
    /// A value from the choice's options, or a zone's name.
    public object? Option { get; init; }
    public int? Player { get; init; }

    public static Input Drag(Card card, Zone to) => new("drag") { Card = card.Id, To = to.Name };

    public static Input Click(Card card) => new("click") { Card = card.Id };

    public static Input DoubleClick(Card card) => new("double_click") { Card = card.Id };

    public static Input ClickEmpty(Zone zone) => new("click_empty") { Zone = zone.Name };

    public static Input Perform(string action) => new("perform") { Action = action };

    public static Input Choose(object? option) => new("choose") { Option = option };

    public static Input Finish() => new("finish");
}
