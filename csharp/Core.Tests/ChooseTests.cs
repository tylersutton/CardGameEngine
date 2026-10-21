using Core;

/// tests/choose_test.gd: steps that wait for a choice (choose, choose_exactly, choose_up_to): picking cards, zones and
/// values, carrying on afterwards, undo while waiting, choices inside loops and triggers, bots and replays.
public class ChooseTests
{
    const string Rules = """
        {
            "players": 2,
            "zones": {
                "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
                "piles": { "layout": "pile", "copies": 2, "label": "Pile #" },
                "hand": { "per_player": true }
            },
            "variables": { "total": 0, "color": "" },
            "actions": {
                "Play one": ["picked = choose(hand, 'Play a card')", "move(picked, piles_1)", "total += 1"],
                "Deal to a pile": ["target = choose(piles, 'Which pile?')", "move(top(deck), target)"],
                "Pick a color": ["color = choose(['Red', 'Black'])"],
                "Pass two": ["move(choose_exactly(hand, 2, 'Pass two'), piles_2)"],
                "Discard some": ["gone = choose_up_to(hand, 2)", "move(gone, piles_2)", "total = count(gone)"],
                "Everyone plays": [{ "for": "player", "in": "players", "do": ["move(choose(hand), piles_1)"] }, "total = count(piles_1)"],
                "Ask in a trigger": ["trigger('choose_one')", "total = 100 + count(piles_1)"]
            },
            "triggers": {
                "start": ["deal(deck, all_hand, 3)"],
                "choose_one": ["move(choose(hand), piles_1)"]
            }
        }
        """;

    readonly List<string> failures = [];

    Game GameWith(string changes = "{}")
    {
        Game game = TestGames.Create(Rules, changes, 3, "res://choose_test.jsonc");
        game.Failed += failures.Add;
        game.Start();
        Assert.Empty(game.Errors);
        return game;
    }

    static void Click(Game game, Card card, int? player = null) => game.Apply(PlayerInput.Click(card) with { Player = player });

    [Fact]
    public void WaitsAndCarriesOn()
    {
        Game game = GameWith();
        game.Apply(PlayerInput.Perform("Play one"));
        Assert.Equal("Play a card", game.Choice.Title);
        Assert.Equal("", game.Choice.Action);
        Assert.True(game.Runner.IsWaiting);
        Assert.Equal(3, game.Choice.Options.Count);
        Assert.False(game.Apply(new("drag") { Card = game.Zones["deck"].Cards[^1].Id, To = "piles_2" }), "nothing else happens while waiting");
        Click(game, game.Zones["deck"].Cards[^1]);
        Assert.True(game.Runner.IsWaiting, "clicking a card that isn't offered doesn't cancel a choose");
        Card card = game.Zones["hand_1"].Cards[1];
        Click(game, card);
        Assert.Contains(card, game.Zones["piles_1"].Cards);
        Assert.Equal(1L, game.Variables["total"]);
        Assert.False(game.Runner.IsWaiting);
        Assert.False(game.Choice.IsOpen);
        Assert.Empty(failures);
    }

    [Fact]
    public void ZonesAndValues()
    {
        Game game = GameWith();
        game.Apply(PlayerInput.Perform("Deal to a pile"));
        Assert.Equal([game.Zones["piles_1"], game.Zones["piles_2"]], game.Choice.Options);
        Assert.Equal("Pile 2", game.Choice.OptionText(game.Zones["piles_2"]));
        game.Apply(PlayerInput.Choose("piles_2"));
        Assert.Single(game.Zones["piles_2"].Cards);
        game.Apply(PlayerInput.Perform("Deal to a pile"));
        game.Apply(new("click_empty") { Zone = "piles_1" });
        Assert.Single(game.Zones["piles_1"].Cards);
        game.Apply(PlayerInput.Perform("Pick a color"));
        game.Apply(PlayerInput.Choose("Black"));
        Assert.Equal("Black", game.Variables["color"]);
        Assert.Empty(failures);
    }

    [Fact]
    public void SeveralCards()
    {
        Game game = GameWith();
        game.Apply(PlayerInput.Perform("Pass two"));
        List<Card> hand = [.. game.Zones["hand_1"].Cards];
        Click(game, hand[0]);
        Assert.True(game.Runner.IsWaiting, "one of two picked: still waiting");
        Click(game, hand[2]);
        Assert.Equal([hand[0], hand[2]], game.Zones["piles_2"].Cards);
        game.Apply(PlayerInput.Perform("Pass two"));
        Assert.Equal(3, game.Zones["piles_2"].Cards.Count);
        Assert.False(game.Runner.IsWaiting, "with only one card left, it's passed without asking");
        game = GameWith();
        game.Apply(PlayerInput.Perform("Discard some"));
        game.Apply(PlayerInput.Finish());
        Assert.Equal(0L, game.Variables["total"]);
        Assert.Equal(3, game.Zones["hand_1"].Cards.Count);
        Assert.Empty(failures);
    }

    [Fact]
    public void LoopsAndTriggers()
    {
        Game game = GameWith();
        game.Apply(PlayerInput.Perform("Everyone plays"));
        Assert.Equal(1, game.CurrentPlayer());
        Assert.True(game.Runner.IsWaiting);
        Assert.False(game.Apply(PlayerInput.Click(game.Zones["hand_2"].Cards[0]) with { Player = 2 }), "player 2 waits their turn");
        Click(game, game.Zones["hand_1"].Cards[0]);
        Assert.Equal(2, game.CurrentPlayer());
        Assert.Equal(game.Zones["hand_2"].Cards.Cast<object?>(), game.Choice.Options);
        Click(game, game.Zones["hand_2"].Cards[0], 2);
        Assert.Equal(2L, game.Variables["total"]);
        Assert.Equal(1, game.CurrentPlayer());
        game = GameWith();
        game.Apply(PlayerInput.Perform("Ask in a trigger"));
        Assert.Equal(0L, game.Variables["total"]);
        Assert.True(game.Runner.IsWaiting, "the step after trigger() waits for the trigger's choice");
        Click(game, game.Zones["hand_1"].Cards[0]);
        Assert.Equal(101L, game.Variables["total"]);
        Assert.Empty(failures);
    }

    [Fact]
    public void UndoBotsAndReplays()
    {
        Game game = GameWith();
        game.Apply(PlayerInput.Perform("Play one"));
        Click(game, game.Zones["hand_1"].Cards[0]);
        game.Apply(new("undo"));
        Assert.True(game.Runner.IsWaiting, "undo goes back to the waiting choice");
        Assert.Empty(game.Zones["piles_1"].Cards);
        Assert.Equal(0L, game.Variables["total"]);
        Click(game, game.Zones["hand_1"].Cards[2]);
        Assert.Equal(1L, game.Variables["total"]);
        Game replayed = new(game.Files, game.RulesPath, game.MatchSeed);
        replayed.Start();
        foreach (PlayerInput input in game.Inputs)
        {
            replayed.Apply(input);
        }
        Assert.Equal(Snapshot.StateHash(game), Snapshot.StateHash(replayed));
        game = GameWith("""{ "bot": { "players": [1] } }""");
        game.Apply(PlayerInput.Perform("Pass two"));
        Assert.True(new Bot(game, 1).TakeInput(), "a bot answers a waiting choice");
        Assert.Equal(2, game.Zones["piles_2"].Cards.Count);
        Assert.Empty(failures);
    }

    [Fact]
    public void ChooseOnlyWorksInSteps()
    {
        string rules = """{ "zones": { "hand": {} }, "actions": { "Go": { "when": "choose(hand) == null", "steps": [] } } }""";
        Game game = new(TestGames.Files("res://choose_mistake.jsonc", rules), "res://choose_mistake.jsonc", 1);
        Assert.Contains(game.Errors, error => error.Contains("only works in steps"));
    }
}
