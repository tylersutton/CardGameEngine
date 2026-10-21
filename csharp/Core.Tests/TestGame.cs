using System.Globalization;
using Core;

/// A small stand-in for Game, enough to work steps out: the step language test's game file, set up by hand.
/// Two players, a standard deck, a pile and a hand each, variables, a player variable, a constant and conditions.
public class TestGame : IStepContext
{
    static readonly BuildingBlocks blocks = new(typeof(TestActions));
    static readonly string[] PerPlayerNames = ["score", "hand"];
    const int Players = 2;

    public readonly Dictionary<string, Zone> Zones = [];
    public readonly Dictionary<string, object?> Variables = new()
    {
        ["phase"] = "draw", ["total"] = 0L, ["names"] = new List<object?>(), ["player"] = 1L, ["score_1"] = 0L, ["score_2"] = 0L,
    };
    readonly Dictionary<string, object?> constants = new() { ["table"] = new List<object?> { 0L, 0L, 2L, 4L } };
    readonly Dictionary<string, Condition> conditions = [];
    readonly Dictionary<string, StepNode> trees = [];

    public TestGame()
    {
        foreach (string name in new[] { "deck", "pile", "hand_1", "hand_2" })
        {
            Zones[name] = new Zone(name);
        }
        Zones["deck"].Cards.AddRange(StandardDeck());
        AddCondition("is_ace", "card.rank == 'A'");
        AddCondition("deck_full", "count(deck) == 52");
        AddCondition("suited(cards, suit)", "count(where(cards, 'SUIT', suit))");
        AddCondition("hearts_and_spades(cards)", "suited(cards, 'Hearts') + suited(cards, 'Spades')");
    }

    public List<Card> Cards => Zones["deck"].Cards;

    public BuildingBlocks Blocks => blocks;

    public bool Waiting => false;

    /// Works a step out, parsing it the first time it's seen (as Game.tree_of does).
    public (object? Result, string Failure) Evaluate(string step, Card? card = null)
    {
        StepEvaluator evaluator = new(this, card);
        object? result = evaluator.Evaluate(TreeOf(step));
        return (result, evaluator.Failure);
    }

    public StepNode TreeOf(string step)
    {
        if (!trees.TryGetValue(step, out StepNode? tree))
        {
            trees[step] = tree = StepParser.Parse(step);
        }
        return tree;
    }

    public object? ValueOf(string name)
    {
        string resolved = Resolve(name);
        if (Zones.TryGetValue(resolved, out Zone? zone))
        {
            return zone;
        }
        if (Variables.TryGetValue(resolved, out object? value))
        {
            return value;
        }
        return constants.GetValueOrDefault(name);
    }

    public void Assign(string name, object? value) => Variables[Resolve(name)] = value;

    public bool IsPerPlayer(string name) => PerPlayerNames.Contains(name);

    public bool IsPerTeam(string name) => false;

    public string? NumberedName(string name, int number) => number is >= 1 and <= Players ? $"{name}_{number}" : null;

    public bool TryGetCondition(string name, out Condition condition) => conditions.TryGetValue(name, out condition!);

    public bool IsUnsetLocal(string name) => false;

    public object CreateActions(Card? card) => new TestActions();

    /// score is the current player's score_n, and opponent_score the other player's.
    string Resolve(string name)
    {
        long player = (long)Variables["player"]!;
        if (IsPerPlayer(name))
        {
            return $"{name}_{player}";
        }
        if (name.StartsWith("opponent_") && IsPerPlayer(name["opponent_".Length..]))
        {
            return $"{name["opponent_".Length..]}_{player % Players + 1}";
        }
        return name;
    }

    void AddCondition(string heading, string expression)
    {
        string name = heading.Split('(')[0];
        string[] parameters = heading.Contains('(') ? heading[(name.Length + 1)..^1].Split(", ") : [];
        conditions[name] = new Condition(parameters, StepParser.Parse(expression));
    }

    static IEnumerable<Card> StandardDeck()
    {
        string[] lines = File.ReadAllLines(RepositoryFiles.PathOf("decks/standard_deck.csv"));
        string[] columns = lines[0].Split(',');
        HashSet<string> numberColumns = ["value", "count"];
        return lines.Skip(1).Select((line, id) => new Card(id, columns.Zip(line.Split(',')).ToDictionary(pair => pair.First, pair => pair.Second), numberColumns));
    }
}

/// The building blocks the tests use, written as Actions' would be in C#.
public class TestActions
{
    public void Draw(Zone from, Zone to, long amount = 1)
    {
        for (int i = 0; i < amount && from.Cards.Count > 0; i++)
        {
            Card card = from.Cards[^1];
            from.Cards.RemoveAt(from.Cards.Count - 1);
            to.Cards.Add(card);
        }
    }

    public void Shuffle(Zone zone) => zone.Cards.Reverse();

    public long Count(object? cards) => CardsIn(cards).Count;

    public Card? Top(object? cards) => CardsIn(cards).LastOrDefault();

    public List<object?> Where(object? cards, string column, object? value) =>
        CardsIn(cards).Where(card => card.Column(column) == Values.Format(value)).Cast<object?>().ToList();

    /// A list's item at that position (from 0), or its first or last item when the position is outside it.
    public object? Lookup(List<object?> list, long position) =>
        list.Count == 0 ? null : list[(int)Math.Clamp(position, 0, list.Count - 1)];

    public List<object?> First(object? items, long amount) => ListOf(items).Take((int)Math.Max(amount, 0)).ToList();

    public List<object?> Last(object? items, long amount) => ListOf(items).TakeLast((int)Math.Max(amount, 0)).ToList();

    /// The total of a column over cards, or of a list of numbers: sum(hand, 'points'), sum([1, 2, 3]).
    public double Sum(object? items, string column = "")
    {
        if (column.Length == 0 && items is List<object?> numbers)
        {
            return numbers.Where(Values.IsNumber).Sum(Values.ToDouble);
        }
        return CardsIn(items).Sum(card => Number(card, column));
    }

    public double Number(Card? card, string column) =>
        card != null && double.TryParse(card.Column(column, "0"), CultureInfo.InvariantCulture, out double number) ? number : 0;

    /// How many cards share each value of a column, biggest first.
    public List<object?> GroupSizes(object? cards, string column) =>
        CardsIn(cards).GroupBy(card => card.Column(column)).Select(group => (object?)(long)group.Count()).OrderByDescending(size => (long)size!).ToList();

    static List<Card> CardsIn(object? source) => source switch
    {
        null => [],
        Zone zone => [.. zone.Cards],
        Card card => [card],
        List<object?> items => items.SelectMany(CardsIn).ToList(),
        _ => [],
    };

    /// Cards stay cards (a zone gives its cards); any other list is kept as it is.
    static List<object?> ListOf(object? items) =>
        items is List<object?> list && list.Any(item => item is not (Card or Zone or List<object?>)) ? list : CardsIn(items).Cast<object?>().ToList();
}
