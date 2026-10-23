using System.Text.Json;
using Core;

/// tests/regression_test.gd: Gin, Hearts and Color Match played with bots, Solitaire's auto-finish and undo, and some
/// situations from Dominion and Sea Salt & Paper that once went wrong.
public class RegressionTests
{
    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));
    readonly List<string> failures = [];

    Game Started(string path, long seed = -1)
    {
        Game game = new(files, path, seed);
        game.Failed += failures.Add;
        game.Start();
        return game;
    }

    [Fact]
    public void GinHandEnds()
    {
        Game game = Started("res://games/gin_rummy.jsonc", 1);
        Bot bot = new(game, 1);
        RandomNumbers chooser = new(1);
        for (int turn = 0; turn < 300 && !game.Plays.Offered.Contains("Next hand") && !game.Over; turn++)
        {
            if (game.CurrentPlayer() == 1)
            {
                List<List<PlayerInput>> moves = game.Plays.LegalMoves();
                game.ApplyMove(moves[chooser.NextInRange(0, moves.Count - 1)]);
            }
            else
            {
                while (bot.TakeInput())
                {
                }
            }
        }
        Assert.True(game.Plays.Offered.Contains("Next hand") || game.Over, $"gin hand ends (offered: {string.Join(", ", game.Plays.Offered)})");
        Assert.Empty(failures);
    }

    [Fact]
    public void HeartsHandScores()
    {
        Game game = Started("res://games/hearts.jsonc", 1);
        Bot bot = new(game, 1);
        RandomNumbers chooser = new(1);
        for (int step = 0; step < 400 && !game.Plays.Offered.Contains("Next hand") && !game.Over; step++)
        {
            if (game.Plays.Offered.Contains("Collect trick"))
            {
                game.Perform("Collect trick");
            }
            else if (game.IsBotTurn())
            {
                bot.TakeInput();
            }
            else
            {
                List<List<PlayerInput>> moves = game.Plays.LegalMoves();
                game.ApplyMove(moves[chooser.NextInRange(0, moves.Count - 1)]);
            }
        }
        double total = Enumerable.Range(1, 4).Sum(player => Values.ToDouble(game.Variables[$"score_{player}"]));
        Assert.Equal(13L, game.Variables["tricks"]);
        Assert.True(total is 26 or 78, $"{total} points (26, or 78 when someone shoots the moon)");
        Assert.Empty(failures);
    }

    [Fact]
    public void ColorMatchBotsFinish()
    {
        string path = "res://color_bots.json";
        Dictionary<string, object?> data = (Dictionary<string, object?>)GameRules.Parse(files.Read("res://games/color_match.jsonc"))!;
        data["bot"] = new Dictionary<string, object?> { ["players"] = new List<object?> { 1.0, 2.0 }, ["prefer"] = "count(opponent_hand) - count(hand)" };
        Game game = new(TestGames.Files(path, JsonSerializer.Serialize(data)), path, 1);
        game.Failed += failures.Add;
        game.Start();
        Bot bot = new(game, 1);
        for (int step = 0; step < 600 && game.Variables["winner"] == null; step++)
        {
            bot.TakeInput();
        }
        Assert.NotNull(game.Variables["winner"]);
        Assert.Empty(failures);
    }

    [Fact]
    public void SolitaireAutoFinishAndUndo()
    {
        Game game = Started("res://games/solitaire.jsonc");
        Dictionary<string, Card> byName = game.Cards.ToDictionary(card => card.Title);
        foreach (Zone zone in game.Zones.Values)
        {
            zone.Cards.Clear();
        }
        string[] ranks = ["K", "Q", "J", "10", "9", "8", "7", "6", "5", "4", "3", "2", "A"];
        string[][] pairs = [["♠", "♥"], ["♥", "♠"], ["♣", "♦"], ["♦", "♣"]];
        for (int column = 0; column < 4; column++)
        {
            for (int i = 0; i < 13; i++)
            {
                Card card = byName[ranks[i] + pairs[column][i % 2]];
                game.Zones[$"tableau_{column + 1}"].Add(card);
                card.FaceUp = true;
            }
        }
        game.Perform("Auto-finish");
        Assert.Equal("You win!", game.Variables["result"]);
        game.Undo();
        Assert.Empty(Actions.CardsIn(game.ZoneGroups["foundation"]));
        Assert.Empty(failures);
    }

    /// run_effect replays a card's effect (Dominion's Throne Room): a Smithy's should draw 3.
    [Fact]
    public void RunEffectReplaysACardsEffect()
    {
        Game game = Started("res://games/dominion.jsonc");
        Card smithy = TestGames.Named(game, "Smithy");
        Zone hand = game.Zones["hand_" + game.CurrentPlayer()];
        int handBefore = hand.Cards.Count;
        new Actions(game).RunEffect(smithy, "play");
        Assert.Equal(handBefore + 3, hand.Cards.Count);
        Assert.Empty(failures);
    }

    /// Remodel chooses twice in a row: a card to trash, then one to gain costing up to 2 more.
    [Fact]
    public void RemodelChoosesTwice()
    {
        Game game = Started("res://games/dominion.jsonc", 4);
        Zone hand = game.Zones["hand_" + game.CurrentPlayer()];
        Card Named(string title) => game.Cards.First(card => card.Title == title && game.ZoneOf(card) != hand);
        Card remodel = Named("Remodel");
        Card estate = Named("Estate");
        game.Move(remodel, hand);
        game.Move(estate, hand);
        game.Apply(PlayerInput.Click(remodel));
        Assert.StartsWith("Remodel: trash", game.Choice.Title);
        game.Apply(PlayerInput.Click(estate));
        Assert.Contains(estate, game.Zones["trash"].Cards);
        Assert.StartsWith("Remodel: gain", game.Choice.Title);
        Card silver = game.Choice.Options.Cast<Card>().First(card => card.Title == "Silver");
        Assert.All(game.Choice.Options.Cast<Card>(), card => Assert.True(Values.ToInt(card.Data["cost"]) <= 4));
        game.Apply(PlayerInput.Click(silver));
        Assert.Contains(silver, game.Zones["discard_" + game.CurrentPlayer()].Cards);
        Assert.False(game.Choice.IsOpen);
        Assert.Empty(failures);
    }

    /// Sea Salt: draw two and keep one, discard the other onto a chosen pile, then two crabs take a card from a pile.
    [Fact]
    public void SeaSaltDrawingAndCrabs()
    {
        Game game = Started("res://games/sea_salt_+_paper.jsonc", 2);
        Zone hand = game.Zones["hand_" + game.CurrentPlayer()];
        game.Apply(PlayerInput.Click(game.Zones["deck"].Cards[^1]));
        Assert.Equal("Keep one card", game.Choice.Title);
        Assert.Equal(2, game.Choice.Options.Count);
        Card kept = (Card)game.Choice.Options[0]!;
        game.Apply(PlayerInput.Click(kept));
        Assert.Contains(kept, hand.Cards);
        Assert.Equal("Discard the other card onto a pile", game.Choice.Title);
        game.Apply(PlayerInput.Choose("discard_piles_2"));
        Assert.Equal(2, game.Zones["discard_piles_2"].Cards.Count);
        Assert.Equal(["Play duo", "End turn", "Stop", "Last chance"], game.Plays.Offered);
        List<Card> crabs = game.Zones["deck"].Cards.Where(card => card.Data["Kind"] == "Crab").Take(2).ToList();
        foreach (Card crab in crabs)
        {
            game.Move(crab, hand);
        }
        game.Apply(PlayerInput.Perform("Play duo"));
        foreach (Card crab in crabs)
        {
            game.Apply(PlayerInput.Click(crab));
        }
        Assert.Equal("Crabs: look through which pile?", game.Choice.Title);
        game.Apply(PlayerInput.Choose("discard_piles_2"));
        Assert.Equal(2, game.Zones["browsing"].Cards.Count);
        Card taken = game.Zones["browsing"].Cards[0];
        game.Apply(PlayerInput.Click(taken));
        Assert.Contains(taken, hand.Cards);
        Assert.Single(game.Zones["discard_piles_2"].Cards);
        Assert.True(game.Zones["browsing"].IsEmpty);
        Assert.Empty(failures);
    }

    /// Three shells (4), two crabs (a duo, 1) and a mermaid with two cards of each color (2) make 7 points.
    [Fact]
    public void SeaSaltScoring()
    {
        Game game = Started("res://games/sea_salt_+_paper.jsonc", 3);
        foreach (string kind in new[] { "Shell", "Shell", "Shell", "Crab", "Crab", "Mermaid", "Octopus" })
        {
            Card card = game.Cards.First(each => each.Data["Kind"] == kind && game.ZoneOf(each) != game.Zones["hand_1"]);
            game.Move(card, game.Zones["hand_1"]);
        }
        Assert.True(Values.DeepEquals(7L, game.Evaluate("my_points")), $"my_points is {Values.Format(game.Evaluate("my_points"))}");
        Assert.True(Values.DeepEquals(4L, game.Evaluate("collection_points(hand)")));
        Assert.Empty(failures);
    }

    /// Clicking one of several copies tells the steps which one it was, even while it's empty.
    [Fact]
    public void ClickedZoneIsTheCopyClicked()
    {
        string rules = """
            { "zones": { "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" }, "pile": { "layout": "pile", "copies": 2 } },
              "actions": { "Deal here": { "click": "pile", "steps": "move(top(deck), clicked_zone)" } } }
            """;
        Game game = new(TestGames.Files("res://clicked_copy_test.jsonc", rules), "res://clicked_copy_test.jsonc", 1);
        game.Failed += failures.Add;
        game.Start();
        Assert.Empty(game.Errors);
        game.Apply(new("click_empty") { Zone = "pile_2" });
        game.Apply(PlayerInput.Click(game.Zones["pile_2"].Cards[0]));
        Assert.Empty(game.Zones["pile_1"].Cards);
        Assert.Equal(2, game.Zones["pile_2"].Cards.Count);
        Assert.Empty(failures);
    }

    [Fact]
    public void PickWithUpToIsALoadError()
    {
        string rules = """{ "zones": { "hand": { "layout": "fan" } }, "actions": { "Pass": { "choose": "hand", "pick": 2, "up_to": 2, "steps": [] } } }""";
        Game game = new(TestGames.Files("res://pick_test.jsonc", rules), "res://pick_test.jsonc", 1);
        Assert.Contains(game.Errors, error => error.Contains("not both"));
    }
}
