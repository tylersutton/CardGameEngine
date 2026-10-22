using Core;

/// tests/players_test.gd: a player's own variables and zones (score[2], hand[p]), comparing players (most,
/// players_with_most, players_where), knocking players out, reset and end_game's winners.
public class PlayersTests
{
    const string Rules = """
        {
            "players": 3,
            "player_names": ["Ann", "Bo", "Cy"],
            "zones": {
                "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
                "hand": { "per_player": true }
            },
            "variables": { "passes": 3, "taken": null },
            "player_variables": { "score": 0, "lives": 2 },
            "conditions": { "big_hand": "count(hand) >= 2" },
            "actions": { "Pass": [] },
            "triggers": { "start": ["deal(deck, [hand_1, hand_2, hand_2], 1)"] }
        }
        """;

    readonly List<string> failures = [];

    static Game GameWith(string changes = "{}")
    {
        Game game = TestGames.Create(Rules, changes, 2, "res://players_test.jsonc");
        game.Start();
        return game;
    }

    void Run(Game game, List<object?> steps)
    {
        game.Failed += failures.Add;
        game.Run(steps);
    }

    static List<object?> Numbers(params long[] numbers) => numbers.Select(number => (object?)number).ToList();

    static void AssertWorksOut(Game game, string step, object? wanted) =>
        Assert.True(Values.DeepEquals(wanted, game.Evaluate(step)), $"{step} gave {Values.Format(game.Evaluate(step))}");

    [Fact]
    public void AnyPlayersValues()
    {
        Game game = GameWith();
        Assert.Empty(game.Errors);
        Run(game, ["score[2] = 5", "score[3] += 7", "score += 1"]);
        Assert.Equal(1L, game.Variables["score_1"]);
        Assert.Equal(5L, game.Variables["score_2"]);
        Assert.Equal(7L, game.Variables["score_3"]);
        AssertWorksOut(game, "count(hand[2])", 2L);
        AssertWorksOut(game, "score[next_player()]", 5L);
        Game unwatched = GameWith();
        unwatched.Evaluate("score[4]");
        Assert.True(unwatched.EvaluationFailed, "score[4] with 3 players is a mistake");
        Assert.Empty(failures);
    }

    [Fact]
    public void ComparingPlayers()
    {
        Game game = GameWith();
        Run(game, ["score[1] = 4", "score[2] = 9", "score[3] = 9"]);
        AssertWorksOut(game, "most('score')", 9L);
        AssertWorksOut(game, "least('score')", 4L);
        AssertWorksOut(game, "players_with_most('score')", Numbers(2, 3));
        AssertWorksOut(game, "players_with_least('score')", Numbers(1));
        AssertWorksOut(game, "players_where('big_hand')", Numbers(2));
        Assert.Equal(1, game.CurrentPlayer());
        Assert.Empty(failures);
    }

    [Fact]
    public void KnockingOut()
    {
        Game game = GameWith();
        Run(game, ["knock_out(2)"]);
        AssertWorksOut(game, "players_left()", Numbers(1, 3));
        AssertWorksOut(game, "next_player()", 3L);
        AssertWorksOut(game, "count(opponent_hand)", 0L);
        game.EndTurn();
        Assert.Equal(3, game.CurrentPlayer());
        game.EndTurn();
        Assert.Equal(1, game.CurrentPlayer());
        Run(game, ["bring_back(players)"]);
        AssertWorksOut(game, "players_left()", Numbers(1, 2, 3));
        Assert.Empty(failures);
    }

    [Fact]
    public void ResettingAndWinners()
    {
        Game game = GameWith();
        Run(game, ["passes = 0", "taken = top(deck)", "lives[2] = 0", "reset(['passes', 'taken', 'lives'])"]);
        Assert.Equal(3L, game.Variables["passes"]);
        Assert.Null(game.Variables["taken"]);
        Assert.Equal(2L, game.Variables["lives_2"]);
        Run(game, ["end_game([2, 3])"]);
        Assert.True(game.Over);
        Assert.Equal("Winners: Bo and Cy", game.ResultText());
        game = GameWith();
        Run(game, ["end_game(1)"]);
        Assert.Equal("Winner: Ann", game.ResultText());
        game = GameWith();
        Run(game, ["end_game()"]);
        Assert.Equal("Game over", game.ResultText());
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("""{ "triggers": { "start": ["hand[2] = 1"] } }""")]
    [InlineData("""{ "triggers": { "start": ["passes[2] = 1"] } }""")]
    public void OnlyPlayerVariablesTakeANumber(string changes)
    {
        Assert.Contains(GameWith(changes).Errors, error => error.Contains("only works for player (or team) variables"));
    }
}
