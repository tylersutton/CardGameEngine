namespace Core;

/// A copy of a game's state: whole snapshots for undo and bot try-outs.
public class Snapshot
{
    readonly Dictionary<string, List<Card>> zones = [];
    readonly Dictionary<string, List<string>> sortColumns = [];
    readonly Dictionary<Card, bool> faces = [];
    readonly List<Card> revealed;
    readonly Dictionary<Card, Dictionary<string, object?>> marks = [];
    readonly Dictionary<string, object?> variables;
    readonly ChoiceState choice;
    readonly List<Frame> waitingSteps;
    readonly (int Run, int Idle, bool Waited, bool Stopped) turns;
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
        revealed = game.Cards.Where(card => card.Revealed).ToList();
        variables = (Dictionary<string, object?>)Values.DeepCopy(game.Variables)!;
        choice = game.Choice.State();
        waitingSteps = StepRunner.CopyFrames(game.Runner.Frames);
        turns = game.Runner.Turns;
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
            card.Revealed = revealed.Contains(card);
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
        game.Runner.Turns = turns;
        game.Plays.Offered = [.. offered];
        game.Over = over;
        game.Winners = [.. winners];
        // Bots restore after every move they try, so the caches built from who's out are kept unless that changed.
        if (!game.KnockedOut.SequenceEqual(knockedOut))
        {
            game.KnockedOut = [.. knockedOut];
            game.ClearPlayerCaches();
        }
        game.Random.State = randomState;
    }

    /// The open choice's title when the snapshot was taken ("" for none).
    public string ChoiceTitle => choice.Title;

    /// Whether two snapshots hold the same state (an input that changed nothing isn't worth an undo).
    public bool SameAs(Snapshot other) => Values.DeepEquals(Plain(), other.Plain());

    /// What's where (cards, faces, marks, variables) as plain data. With everyDetail, also the order cards sit in,
    /// any choice in progress and the random numbers' state; without, just enough to tell whether a move changed
    /// anything.
    public static Dictionary<string, object?> Describe(Game game, bool everyDetail = true)
    {
        Dictionary<string, object?> zones = [];
        Dictionary<string, object?> marks = [];
        Dictionary<string, object?> state = new()
        {
            ["variables"] = PlainData(game.Variables), ["zones"] = zones, ["marks"] = marks, ["over"] = game.Over,
            ["winners"] = PlainData(game.Winners),
        };
        if (everyDetail)
        {
            Snapshot snapshot = Take(game);
            state["choice"] = PlainData(snapshot.choice.Plain());
            state["flow"] = PlainData(snapshot.Plain()[6..]);
        }
        foreach ((string name, Zone zone) in game.Zones)
        {
            List<long> ids = zone.Cards.Select(card => card.FaceUp ? (long)card.Id : -1L - card.Id).ToList();
            if (!everyDetail)
            {
                ids.Sort();
            }
            zones[name] = ids;
            foreach (Card card in zone.Cards.Where(card => card.Marks.Count > 0))
            {
                marks[card.Id.ToString()] = PlainData(card.Marks);
            }
        }
        return state;
    }

    /// Everything that decides what happens next, as one number, so two copies of a match (e.g. online) can be
    /// compared.
    public static long StateHash(Game game)
    {
        byte[] text = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(Describe(game)));
        return BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(text));
    }

    List<object?> Plain() =>
    [
        zones, sortColumns, faces, marks, variables, choice.Plain(), waitingSteps.Select(frame => (object?)frame.Plain()).ToList(),
        offered, over, winners, knockedOut, randomState, revealed.Cast<object?>().ToList(),
    ];

    /// Cards and zones by name, so the state can be written out as JSON.
    static object? PlainData(object? value) => value switch
    {
        Card card => $"card {card.Id}",
        Zone zone => $"zone {zone.Name}",
        string or bool or null => value,
        System.Collections.IDictionary dictionary => dictionary.Keys.Cast<object>().ToDictionary(key => PlainData(key)!.ToString()!, key => PlainData(dictionary[key])),
        System.Collections.IEnumerable items => items.Cast<object?>().Select(PlainData).ToList(),
        Enum => value.ToString(),
        _ => value,
    };
}
