using Core;

/// tests/turn_test.gd: the "turn" steps, offer and offer_until, actions that are only possible while offered,
/// actions done by a drag, a click or a button, end_game, undo and replays, and load errors for old game files.
public class TurnTests
{
    const string Rules = """
        {
            "players": 2,
            "zones": {
                "deck": { "layout": "pile", "face": "down", "cards": "res://decks/standard_deck.csv" },
                "discard": { "layout": "pile" },
                "table": { "per_player": true },
                "hand": { "per_player": true }
            },
            "variables": { "turns": 0, "taps": 0 },
            "actions": {
                "Draw": { "drag": { "from": "deck", "to": "hand" }, "click": true },
                "Discard": { "drag": { "from": "hand", "to": "discard" } },
                "Lay down": { "drag": { "from": "hand", "to": "table" }, "button": true, "when": "count(hand) >= 3" },
                "Pass": [],
                "Tap the discard": { "click": "discard", "steps": "taps += 1" },
                "Sort hand": "sort(hand, ['value'])"
            },
            "turn": [
                "turns += 1",
                "offer(['Draw', 'Pass'])",
                "offer_until('Lay down', 'Discard')",
                { "if": "turns >= 4", "then": "end_game()" },
                "end_turn()"
            ],
            "triggers": { "start": ["deal(deck, all_hand, 2)"] }
        }
        """;

    readonly List<string> failures = [];

    Game GameWith(string changes = "{}", bool watch = true)
    {
        Game game = TestGames.Create(Rules, changes, 5, "res://turn_test.jsonc");
        if (watch)
        {
            game.Failed += failures.Add;
        }
        game.Start();
        return game;
    }

    static PlayerInput DragTo(Card card, string zone) => new("drag") { Card = card.Id, To = zone };

    [Fact]
    public void OffersAndActions()
    {
        Game game = GameWith();
        Assert.Empty(game.Errors);
        Assert.Equal(["Draw", "Pass"], game.Plays.Offered);
        Assert.Equal(1, game.CurrentPlayer());
        Assert.False(game.Apply(DragTo(game.Zones["hand_1"].Cards[0], "discard")), "Discard isn't possible before it's offered");
        Assert.False(game.Plays.CanPerform("Lay down"), "nor is Lay down's button");
        game.Apply(PlayerInput.Perform("Sort hand"));
        Assert.Equal(["value"], game.Zones["hand_1"].SortColumns);
        Assert.Equal(["Draw", "Pass"], game.Plays.Offered);
        game.Apply(new("click_empty") { Zone = "discard" });
        Assert.Equal(1L, game.Variables["taps"]);
        game.Apply(PlayerInput.Click(game.Zones["deck"].Cards[^1]));
        Assert.Equal(3, game.Zones["hand_1"].Cards.Count);
        Assert.Equal(["Lay down", "Discard"], game.Plays.Offered);
        Assert.True(game.Plays.CanPerform("Lay down"), "Lay down's button works while offered and possible");
        game.Apply(PlayerInput.Perform("Lay down"));
        Assert.Equal("Lay down", game.Choice.Title);
        Assert.Equal(3, game.Choice.Options.Count);
        game.Apply(PlayerInput.Click((Card)game.Choice.Options[0]!));
        Assert.Single(game.Zones["table_1"].Cards);
        Assert.Equal(["Lay down", "Discard"], game.Plays.Offered);
        Assert.False(game.Plays.CanPerform("Lay down"), "with 2 cards left, Lay down isn't possible");
        game.Apply(DragTo(game.Zones["hand_1"].Cards[0], "discard"));
        Assert.Equal(2, game.CurrentPlayer());
        Assert.Equal(["Draw", "Pass"], game.Plays.Offered);
        Assert.Empty(failures);
    }

    [Fact]
    public void TurnsUndoAndReplays()
    {
        Game game = GameWith();
        game.Apply(PlayerInput.Perform("Pass"));
        game.Apply(DragTo(game.Zones["hand_1"].Cards[0], "discard"));
        Assert.Equal(2, game.CurrentPlayer());
        Assert.Equal(2L, game.Variables["turns"]);
        Assert.False(game.Apply(PlayerInput.Perform("Pass") with { Player = 1 }), "player 1 can't act on player 2's turn");
        game.Apply(new("undo"));
        Assert.Equal(1, game.CurrentPlayer());
        Assert.Equal(["Lay down", "Discard"], game.Plays.Offered);
        Assert.Equal(1L, game.Variables["turns"]);
        game.Apply(DragTo(game.Zones["hand_1"].Cards[1], "discard"));
        Game replayed = new(game.Files, game.RulesPath, game.MatchSeed);
        replayed.Start();
        foreach (PlayerInput input in game.Inputs)
        {
            replayed.Apply(input);
        }
        Assert.Equal(Snapshot.StateHash(game), Snapshot.StateHash(replayed));
        Assert.Empty(failures);
    }

    [Fact]
    public void GameOverAndEndlessTurns()
    {
        Game game = GameWith();
        for (int turn = 0; turn < 4; turn++)
        {
            game.Apply(PlayerInput.Perform("Pass"));
            game.Apply(DragTo(game.Zones[$"hand_{game.CurrentPlayer()}"].Cards[0], "discard"));
        }
        Assert.True(game.Over);
        Assert.Empty(game.Plays.Offered);
        Assert.Equal(4L, game.Variables["turns"]);
        Assert.True(!game.Apply(PlayerInput.Perform("Pass")) || Equals(game.Variables["turns"], 4L), "offered actions can't be done once the game is over");
        Assert.True(GameWith("""{ "turn": ["end_turn()"] }""", watch: false).Over, "a whole round of turns where nobody can do anything ends the game");
        Assert.Empty(failures);
    }

    /// Without drag actions, a game that offers actions refuses drags (they'd move cards outside its rules); only a
    /// game that offers nothing is a free-play sandbox.
    [Fact]
    public void OnlySandboxesAllowEveryDrag()
    {
        const string clickOnly = """{ "actions": { "Draw": { "click": "deck", "steps": "draw(deck, hand)" } }, "turn": ["offer(['Draw'])", "end_turn()"] }""";
        Game played = GameWith(clickOnly);
        Assert.False(played.Apply(DragTo(played.Zones["hand_1"].Cards[0], "discard")));
        Assert.Empty(played.Zones["discard"].Cards);
        Game sandbox = GameWith("""{ "actions": { "Draw": { "click": "deck", "steps": "draw(deck, hand)" } }, "turn": [] }""");
        Assert.True(sandbox.Apply(DragTo(sandbox.Zones["hand_1"].Cards[0], "discard")));
        Assert.Single(sandbox.Zones["discard"].Cards);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("""{ "moves": [{ "from": "deck", "to": "hand" }] }""", "\"moves\" are now actions with \"drag\"")]
    [InlineData("""{ "zones": { "deck": { "cards": "res://decks/standard_deck.csv", "on_click": "draw(deck, discard)" }, "discard": {}, "table": { "per_player": true }, "hand": { "per_player": true } } }""", "make an action with \"click\"")]
    [InlineData("""{ "actions": { "Pass": { "phase": "draw", "steps": [] } } }""", "\"phase\" on actions is gone")]
    [InlineData("""{ "turn": ["offer('Drew')"] }""", "no action named \"Drew\"")]
    [InlineData("""{ "turn": ["offer(turns)"] }""", "needs action names in quotes")]
    [InlineData("""{ "actions": { "Pass": { "click": true, "steps": [] } } }""", "it needs \"drag\"")]
    [InlineData("""{ "actions": { "Draw": { "drag": { "from": "deck", "to": "hand" }, "choose": "hand" } } }""", "\"choose\" is for buttons")]
    public void LoadErrors(string changes, string message)
    {
        Game game = TestGames.Create(Rules, changes, 1, "res://turn_mistake.jsonc");
        Assert.Contains(game.Errors, error => error.Contains(message));
    }

    /// Cards dealt to private hands, and buttons people press out of turn or to pick several cards.
    const string TableRules = """
        {
            "players": 2,
            "zones": {
                "deck": { "layout": "pile", "face": "down", "cards": "res://decks/standard_deck.csv" },
                "pile": { "layout": "pile" },
                "hand": { "per_player": true, "face": "private" }
            },
            "actions": {
                "Draw": { "drag": { "from": "deck", "to": "hand" }, "click": true },
                "Play": { "choose": "hand", "up_to": 3, "steps": "move(choice, pile)" },
                "Pass": [],
                "Sort hand": { "any_time": true, "steps": "sort(hand, ['value'])" }
            },
            "turn": ["offer(['Play', 'Pass'])", "end_turn()"],
            "triggers": { "start": ["deal(deck, all_hand, 5)", "player = 1"] }
        }
        """;

    static Game TableGame()
    {
        Game game = TestGames.Create(TableRules, "{}", 3, "res://table_test.jsonc");
        Assert.Empty(game.Errors);
        game.Start();
        return game;
    }

    [Fact]
    public void AnyTimeActionsRunAsWhoeverDidThem()
    {
        Game game = TableGame();
        Zone otherHand = game.Zones["hand_2"];
        List<Card> before = [.. game.Zones["hand_1"].Cards];
        Assert.True(game.Apply(PlayerInput.Perform("Sort hand") with { Player = 2 }));
        Assert.Equal(otherHand.Cards.OrderBy(card => Values.ToInt(card.Data["value"])).Select(card => card.Data["value"]), otherHand.Cards.Select(card => card.Data["value"]));
        Assert.Equal(before, game.Zones["hand_1"].Cards);
        Assert.Equal(1, game.CurrentPlayer());
        Assert.Equal(["Play", "Pass"], game.Plays.Offered);
    }

    [Fact]
    public void AnyTimeActionsLeaveAChoiceBeingMadeAlone()
    {
        Game game = TableGame();
        game.Apply(PlayerInput.Click(game.Zones["hand_1"].Cards[0]) with { Player = 1 });
        Assert.True(game.Apply(PlayerInput.Perform("Sort hand") with { Player = 2 }));
        Assert.Equal("Play", game.Choice.Title);
        Assert.Single(game.Choice.Selected);
    }

    [Fact]
    public void PlayersRearrangeTheirOwnHandOnAnyTurnWithoutShowingIt()
    {
        Game game = TableGame();
        game.LocalSeat = 1;
        List<string> logged = [];
        game.GameLog.Logged += logged.Add;
        Card moved = game.Zones["hand_2"].Cards[0];
        Assert.True(game.Apply(new("reorder") { Card = moved.Id, Index = 3, Player = 2 }));
        Assert.Equal(moved, game.Zones["hand_2"].Cards[3]);
        Assert.DoesNotContain(logged, line => line.Contains(moved.Title));
        Assert.False(game.Apply(new("reorder") { Card = game.Zones["hand_1"].Cards[0].Id, Index = 3, Player = 2 }), "nobody rearranges someone else's hand");
    }

    [Fact]
    public void ClickingACardStartsAChoiceOfSeveralWithIt()
    {
        Game game = TableGame();
        Card first = game.Zones["hand_1"].Cards[0];
        Card second = game.Zones["hand_1"].Cards[1];
        game.Apply(PlayerInput.Click(first) with { Player = 1 });
        Assert.Equal("Play", game.Choice.Title);
        Assert.Equal([first], game.Choice.Selected);
        game.Apply(PlayerInput.Click(second) with { Player = 1 });
        game.Apply(PlayerInput.Finish() with { Player = 1 });
        Assert.Equal([first, second], game.Zones["pile"].Cards);
        Assert.Equal(2, game.CurrentPlayer());
    }

    [Fact]
    public void ARefusedDragChangesNothing()
    {
        Game game = TableGame();
        game.Apply(PlayerInput.Click(game.Zones["hand_1"].Cards[0]) with { Player = 1 });
        Assert.False(game.Apply(PlayerInput.Drag(game.Zones["hand_1"].Cards[1], game.Zones["deck"]) with { Player = 1 }));
        Assert.Equal("Play", game.Choice.Title);
        Assert.Single(game.Choice.Selected);
    }

    [Fact]
    public void ThePersonAtTheScreenActsOnlyOnTheirTurn()
    {
        Game game = TableGame();
        game.Rules.Bot["players"] = new List<object?> { 2.0 };
        game.Apply(PlayerInput.Perform("Pass") with { Player = 1 });
        Assert.Equal(1, game.Viewer());
        Assert.False(game.MayAct(game.Viewer(), PlayerInput.Drag(game.Zones["deck"].Cards[^1], game.Zones["hand_2"])), "the bot's draw isn't theirs to make");
        Assert.True(game.MayAct(game.Viewer(), PlayerInput.Perform("Sort hand")));
    }
}
