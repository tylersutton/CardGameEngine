using Core;

/// The step language test (tests/step_language_test.gd) for the C# parser and evaluator: what steps work out to,
/// changing variables, and the messages for mistakes. Load checks (StepCompiler) aren't ported yet.
public class CoreTests
{
    readonly TestGame game = new();

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
        Assert.Equal(true, game.Evaluate("is_ace", Ace).Result);
        Assert.Equal(2L, game.Evaluate("card.value + 1", Ace).Result);
    }

    [Fact]
    public void ChangingVariables()
    {
        foreach (string step in new[] { "total = 5", "total += 2", "total -= 1", "names += ['x']", "score += 3", "player = 2", "score += 10" })
        {
            Assert.Equal("", game.Evaluate(step).Failure);
        }
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
    [InlineData("shufle(deck)", "unknown building block \"shufle\"")]
    [InlineData("shuffle(deck, pile)", "shuffle takes 1 argument, got 2")]
    [InlineData("total = suited(deck)", "suited takes 2 (cards, suit), got 1")]
    [InlineData("total = suited", "suited needs its cards, suit")]
    [InlineData("score[3] = 1", "there's no player 3")]
    public void RunningMistakes(string step, string message) =>
        Assert.Contains(message, game.Evaluate(step, Ace).Failure);

    [Theory]
    [InlineData("move(top(deck), \"pile\")", "single quotes")]
    [InlineData("!deck_full", "use not instead of !")]
    [InlineData("draw(deck, pile", "expected \")\"")]
    [InlineData("'unclosed", "has no closing '")]
    [InlineData("count(deck) = 1", "only a variable can be changed with =")]
    public void ParseMistakes(string step, string message) =>
        Assert.Contains(message, Assert.IsType<UnreadableNode>(StepParser.Parse(step)).Error);

    [Fact]
    public void TreesAreRecordsThatCompareByValue()
    {
        Assert.Equal(new AssignNode("score", null, "+=", new ValueNode(2L)), StepParser.Parse("score += 2"));
        Assert.Equal(
            new AssignNode("score", new ValueNode(2L), "=", new ValueNode(0L)),
            StepParser.Parse("score[2] = 0"));
        BinaryNode sum = Assert.IsType<BinaryNode>(StepParser.Parse("1 + 2 * 3"));
        Assert.Equal("+", sum.Op);
        Assert.IsType<BinaryNode>(sum.Right);
    }

    void Expect(string step, object wanted)
    {
        (object? result, string failure) = game.Evaluate(step);
        Assert.Equal("", failure);
        Assert.True(SameKindAndValue(wanted, result), $"{step} gave {Values.Format(result)} ({result?.GetType().Name}), wanted {Values.Format(wanted)} ({wanted.GetType().Name})");
    }

    /// Like the GDScript test, 3 isn't 3.0: whole numbers must stay whole.
    static bool SameKindAndValue(object? wanted, object? result) => (wanted, result) switch
    {
        (List<object?> wantedItems, List<object?> items) => wantedItems.Count == items.Count && wantedItems.Zip(items).All(pair => SameKindAndValue(pair.First, pair.Second)),
        _ => wanted?.GetType() == result?.GetType() && Equals(wanted, result),
    };
}
