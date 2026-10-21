using Core;

/// The step language test (tests/step_language_test.gd), with the same game file: what steps work out to,
/// changing variables, and the messages for mistakes, both when the game loads and while it runs.
public class StepLanguageTests
{
    readonly Game game = StepLanguageGame.Create();
    readonly List<string> failures = [];

    public StepLanguageTests()
    {
        game.Failed += failures.Add;
        game.Start();
    }

    Card Ace => game.Cards.First(card => card.Title == "A♠");

    static List<object?> List(params object?[] items) => [.. items];

    [Theory]
    [InlineData("1 + 2 * 3", 7L)]
    [InlineData("(1 + 2) * 3", 9L)]
    [InlineData("7 / 2", 3L)]
    [InlineData("7.0 / 2", 3.5)]
    [InlineData("-2 - -3", 1L)]
    [InlineData("10 % 4", 2L)]
    [InlineData("not 1 == 2", true)]
    [InlineData("not deck_full or 1 == 1", true)]
    [InlineData("'big' if count(deck) > 10 else 'small'", "big")]
    [InlineData("'a' if false else 'b' if false else 'c'", "c")]
    [InlineData("count(pile) > 0 and top(pile).rank == 'A'", false)]
    [InlineData("count(pile) == 0 or top(pile).rank == 'A'", true)]
    [InlineData("'%s has %d' % ['deck', count(deck)]", "deck has 52")]
    [InlineData("['a', 'b', 'c'][1]", "b")]
    [InlineData("'King\\'s Court'", "King's Court")]
    [InlineData("min(3, 1, 2) + max(4, 5)", 6L)]
    [InlineData("int(true) + abs(-2)", 3L)]
    [InlineData("phase != null", true)]
    [InlineData("phase == 'draw' and total == 0", true)]
    [InlineData("score == opponent_score", true)]
    [InlineData("'b' in ['a', 'b']", true)]
    [InlineData("3 not in [1, 2]", true)]
    [InlineData("top(deck) in deck", true)]
    [InlineData("'ell' in 'hello'", true)]
    [InlineData("lookup(table, 3)", 4L)]
    [InlineData("lookup(table, 99)", 4L)]
    [InlineData("lookup(table, -1)", 0L)]
    [InlineData("suited(deck, 'Hearts')", 13L)]
    [InlineData("hearts_and_spades(deck)", 26L)]
    [InlineData("sum(first(group_sizes(deck, 'color'), 1))", 26.0)]
    [InlineData("max([3, 9, 4]) + min([3, 9, 4])", 12L)]
    [InlineData("sum([1, 2, 3])", 6.0)]
    [InlineData("count(where(deck, 'SUIT', 'Clubs'))", 13L)]
    public void WorksOut(string step, object wanted) => Expect(step, wanted);

    [Fact]
    public void Lists()
    {
        Expect("[1, 2] + [3]", List(1L, 2L, 3L));
        Expect("group_sizes(deck, 'suit')", List(13L, 13L, 13L, 13L));
        Expect("first([5, 6, 7], 2) + last([5, 6, 7], 1)", List(5L, 6L, 7L));
    }

    [Fact]
    public void ConditionsAndColumnsSeeTheStepsCard()
    {
        Assert.Equal(true, game.Evaluate("is_ace", Ace));
        Assert.Equal(2L, game.Evaluate("card.value + 1", Ace));
    }

    [Fact]
    public void ChangingVariables()
    {
        game.Run(["total = 5", "total += 2", "total -= 1", "names += ['x']", "score += 3", "player = 2", "score += 10"]);
        Assert.Empty(failures);
        Assert.Equal(6L, game.Variables["total"]);
        Assert.Equal(List("x"), game.Variables["names"]);
        Assert.Equal(3L, game.Variables["score_1"]);
        Assert.Equal(10L, game.Variables["score_2"]);
    }

    [Theory]
    [InlineData("card.rank == 1", "put the number in quotes")]
    [InlineData("card.colour", "no column \"colour\"")]
    [InlineData("top(pile).rank", "can't read .rank of nothing")]
    [InlineData("draw('deck', pile)", "\"from\" should be a zone, not the text 'deck'")]
    [InlineData("total / 0", "can't divide by zero")]
    [InlineData("phase - 1", "can't work out the text 'draw' - the number 1")]
    [InlineData("[1, 2][5]", "outside the list")]
    public void RunningMistakes(string step, string message)
    {
        game.Evaluate(step, Ace);
        Assert.True(game.EvaluationFailed);
        Assert.Contains(message, Assert.Single(failures));
    }

    [Theory]
    [InlineData("{\"actions\": {\"Play\": {\"drag\": {\"from\": \"deck\", \"to\": \"pile\"}, \"when\": \"total = 1\"}}}", "only works as a step of its own")]
    [InlineData("{\"triggers\": {\"start\": [\"pile = 1\"]}}", "is a zone")]
    [InlineData("{\"triggers\": {\"start\": [\"totl = 1\"]}}", "\"totl\" is set but never used")]
    [InlineData("{\"triggers\": {\"start\": [\"move(top(deck), \\\"pile\\\")\"]}}", "single quotes")]
    [InlineData("{\"triggers\": {\"start\": [\"!deck_full\"]}}", "use not instead of !")]
    [InlineData("{\"triggers\": {\"start\": [\"draw(deck, pile\"]}}", "expected \")\"")]
    [InlineData("{\"triggers\": {\"start\": [\"shufle(deck)\"]}}", "unknown building block \"shufle\"")]
    [InlineData("{\"triggers\": {\"start\": [\"shuffle(deck, pile)\"]}}", "shuffle takes 1 argument, got 2")]
    [InlineData("{\"conditions\": {\"a\": \"b\", \"b\": \"not a\"}}", "refers to itself")]
    [InlineData("{\"triggers\": {\"start\": [\"total = viewer()\"]}}", "viewer() only works in \"status\"")]
    [InlineData("{\"triggers\": {\"start\": [\"table = []\"]}}", "is a constant")]
    [InlineData("{\"triggers\": {\"start\": [\"total = suited(deck)\"]}}", "suited takes 2 (cards, suit), got 1")]
    [InlineData("{\"triggers\": {\"start\": [\"total = suited\"]}}", "suited needs its cards, suit")]
    [InlineData("{\"triggers\": {\"start\": [\"total = deck_full()\"]}}", "is a condition without parameters")]
    [InlineData("{\"conditions\": {\"points(cards\": \"1\"}}", "write a name, or a name with parameters")]
    public void LoadMistakes(string changes, string message)
    {
        Game broken = StepLanguageGame.Create(changes);
        Assert.Contains(broken.Errors, error => error.Contains(message));
    }

    void Expect(string step, object wanted)
    {
        object? result = game.Evaluate(step);
        Assert.Empty(failures);
        Assert.True(SameKindAndValue(wanted, result), $"{step} gave {Values.Format(result)} ({result?.GetType().Name}), wanted {Values.Format(wanted)} ({wanted.GetType().Name})");
    }

    /// Like the GDScript test, 3 isn't 3.0: whole numbers must stay whole.
    static bool SameKindAndValue(object? wanted, object? result) => (wanted, result) switch
    {
        (List<object?> wantedItems, List<object?> items) => wantedItems.Count == items.Count && wantedItems.Zip(items).All(pair => SameKindAndValue(pair.First, pair.Second)),
        _ => wanted?.GetType() == result?.GetType() && Equals(wanted, result),
    };
}
