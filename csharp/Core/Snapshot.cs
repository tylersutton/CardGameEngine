namespace Core;

/// A copy of a game's state: whole snapshots for undo and bot try-outs.
public class Snapshot
{
    readonly Dictionary<string, List<Card>> zones = [];
    readonly Dictionary<string, List<string>> sortColumns = [];
    readonly Dictionary<Card, bool> faces = [];
    readonly Dictionary<Card, Dictionary<string, object?>> marks = [];
    readonly Dictionary<string, object?> variables;
    readonly ChoiceState choice;
    readonly List<Frame> waitingSteps;
    readonly List<string> offered;
    readonly bool over;
    readonly List<int> winners;
    readonly List<int> knockedOut;
    readonly ulong randomState;

    Snapshot(Game game)
    {
        foreach ((string name, Zone zone) in game.Zones)
        {
            zones[name] = [.. zone.Cards];
            sortColumns[name] = [.. zone.SortColumns];
            foreach (Card card in zone.Cards)
            {
                faces[card] = card.FaceUp;
                if (card.Marks.Count > 0)
                {
                    marks[card] = new(card.Marks);
                }
            }
        }
        variables = (Dictionary<string, object?>)Values.DeepCopy(game.Variables)!;
        choice = game.Choice.State();
        waitingSteps = StepRunner.CopyFrames(game.Runner.Frames);
        offered = [.. game.Plays.Offered];
        over = game.Over;
        winners = [.. game.Winners];
        knockedOut = [.. game.KnockedOut];
        randomState = game.Random.State;
    }

    public static Snapshot Take(Game game) => new(game);

    public void Restore(Game game)
    {
        foreach ((string name, Zone zone) in game.Zones)
        {
            zone.Restore(zones[name], sortColumns[name]);
        }
        foreach ((Card card, bool faceUp) in faces)
        {
            card.FaceUp = faceUp;
            card.Marks.Clear();
            foreach ((string key, object? value) in marks.GetValueOrDefault(card) ?? [])
            {
                card.Marks[key] = value;
            }
        }
        game.Variables.Clear();
        foreach ((string name, object? value) in variables)
        {
            game.Variables[name] = Values.DeepCopy(value);
        }
        game.Choice.Restore(choice);
        game.Runner.Frames = StepRunner.CopyFrames(waitingSteps);
        game.Runner.Waiting = game.Runner.Frames.Count > 0;
        game.Plays.Offered = [.. offered];
        game.Over = over;
        game.Winners = [.. winners];
        game.KnockedOut = [.. knockedOut];
        game.ClearPlayerCaches();
        game.Random.State = randomState;
    }

    /// Whether two snapshots hold the same state (an input that changed nothing isn't worth an undo).
    public bool SameAs(Snapshot other) => Values.DeepEquals(Plain(), other.Plain());

    List<object?> Plain() =>
    [
        zones, sortColumns, faces, marks, variables, choice.Plain(), waitingSteps.Select(frame => (object?)frame.Plain()).ToList(),
        offered, over, winners, knockedOut, randomState,
    ];
}
