namespace Core;

/// A condition from the game file: "points(cards)": "sum(cards, 'points')" has the parameter cards.
public record Condition(IReadOnlyList<string> Parameters, StepNode Tree);

/// What StepEvaluator needs from the game. Game will implement it; tests use a small stand-in.
public interface IStepContext
{
    BuildingBlocks Blocks { get; }

    /// Whether a choose(...) is waiting for its answer, so the rest of the step isn't worked out.
    bool Waiting { get; }

    /// A zone or variable, or null.
    object? ValueOf(string name);

    void Assign(string name, object? value);

    /// Per-player and per-team names: score[2] is player 2's score.
    bool IsPerPlayer(string name);

    bool IsPerTeam(string name);

    /// score[2] is score_2; null if there's no such player or team.
    string? NumberedName(string name, int number);

    bool TryGetCondition(string name, out Condition condition);

    /// A name set with = in some list of steps, but not in the one running now.
    bool IsUnsetLocal(string name);

    /// The object whose methods are the building blocks, for a step about this card.
    object CreateActions(Card? card);
}
