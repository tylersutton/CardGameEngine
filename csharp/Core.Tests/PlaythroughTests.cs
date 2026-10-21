using System.Text.Json;
using Core;

/// tests/playthrough_test.gd: every game played to its end with random bots in every seat (pressing the buttons only
/// people press, like Next hand), with nothing failing on the way. Games with no end (solitaire, the sandbox,
/// President's endless hands) just play a while at random.
public class PlaythroughTests
{
    const int MostInputs = 3000;
    const int EndlessInputs = 300;
    /// Games that never call end_game(): one player games, the sandbox, and President (hands go on for ever).
    static readonly string[] Endless = ["pyramid", "solitaire", "standard", "president"];
    /// Games too long for random bots to finish (they rarely knock in Gin): finishing a hand will do.
    static readonly string[] Long = ["gin_rummy"];
    /// Bots with an aim, for games random play wouldn't finish (Color Match's random bots draw as often as they play).
    static readonly Dictionary<string, string> Prefer = new() { ["color_match"] = "count(opponent_hand) - count(hand)" };

    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    public static TheoryData<string> Games()
    {
        TheoryData<string> games = [];
        foreach (string path in files.GamePaths())
        {
            games.Add(Path.GetFileNameWithoutExtension(path));
        }
        return games;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void BotsPlayToTheEnd(string gameName)
    {
        string path = files.GamePaths().Single(each => Path.GetFileNameWithoutExtension(each) == gameName);
        Dictionary<string, object?> data = (Dictionary<string, object?>)GameRules.Parse(files.Read(path))!;
        int players = (int)(data.GetValueOrDefault("players") as double? ?? 1);
        Dictionary<string, object?> bot = new() { ["players"] = Enumerable.Range(1, players).Select(player => (object?)(double)player).ToList() };
        if (Prefer.TryGetValue(gameName, out string? prefer))
        {
            bot["prefer"] = prefer;
        }
        data["bot"] = bot;
        Game game = new(new(RepositoryFiles.PathOf(""), new() { [path] = JsonSerializer.Serialize(data) }), path, 11);
        Assert.Empty(game.Errors);
        List<string> failures = [];
        game.Failed += failures.Add;
        game.Start();
        Bot bots = new(game, 11);
        RandomNumbers chooser = new(11);
        bool endless = Endless.Contains(gameName);
        int inputs = 0;
        int nextHands = 0;
        game.InputApplied += input => nextHands += input.Action == "Next hand" ? 1 : 0;
        while (inputs < (endless ? EndlessInputs : MostInputs) && !game.Over && failures.Count == 0)
        {
            bool moved = endless ? RandomMove(game, chooser) : bots.TakeInput();
            if (!moved && !PressAButton(game))
            {
                break;
            }
            inputs++;
        }
        Assert.Empty(failures);
        if (Long.Contains(gameName))
        {
            Assert.True(nextHands > 0, $"{gameName} didn't finish a hand ({inputs} inputs)");
        }
        else if (!endless)
        {
            Assert.True(game.Over, $"{gameName} didn't end ({inputs} inputs; offered {string.Join(", ", game.Plays.Offered)}, choosing '{game.Choice.Title}')");
        }
    }

    [Fact]
    public void DominionsOwnBotsFinishAGame()
    {
        string path = "res://games/dominion.jsonc";
        Dictionary<string, object?> data = (Dictionary<string, object?>)GameRules.Parse(files.Read(path))!;
        ((Dictionary<string, object?>)data["bot"]!)["players"] = new List<object?> { 1.0, 2.0 };
        Game game = new(new(RepositoryFiles.PathOf(""), new() { [path] = JsonSerializer.Serialize(data) }), path, 2);
        List<string> failures = [];
        game.Failed += failures.Add;
        game.Start();
        Bot bots = new(game, 2);
        int inputs = 0;
        while (!game.Over && inputs < 600 && bots.TakeInput())
        {
            inputs++;
        }
        Assert.Empty(failures);
        Assert.True(game.Over, $"stopped after {inputs} inputs");
    }

    [Fact]
    public void TryingMovesOutLogsNothing()
    {
        Game game = new(files, "res://games/dominion.jsonc", 2);
        List<string> lines = [];
        game.GameLog.Logged += lines.Add;
        game.Start();
        Bot bot = new(game);
        Dictionary<string, object?> before = Snapshot.Describe(game);
        lines.Clear();
        foreach (List<Input> move in game.Plays.LegalMoves())
        {
            bot.TryOut(move, "0");
        }
        Assert.Empty(lines);
        Assert.True(Values.DeepEquals(before, Snapshot.Describe(game)));
    }

    [Fact]
    public void UndoGoesBackExactly()
    {
        Game game = new(files, "res://games/solitaire.jsonc", 6);
        game.Start();
        RandomNumbers chooser = new(6);
        List<Dictionary<string, object?>> states = [];
        for (int i = 0; i < 20; i++)
        {
            states.Add(Snapshot.Describe(game));
            int history = game.History.Count;
            RandomMove(game, chooser);
            if (game.History.Count == history)
            {
                states.RemoveAt(states.Count - 1);
            }
        }
        for (int i = states.Count - 1; i >= 0; i--)
        {
            game.Apply(new("undo"));
            Assert.True(Values.DeepEquals(states[i], Snapshot.Describe(game)), $"undo {states.Count - i} didn't go back");
        }
    }

    static bool RandomMove(Game game, RandomNumbers chooser)
    {
        List<List<Input>> moves = game.Plays.LegalMoves();
        if (moves.Count == 0)
        {
            return false;
        }
        game.ApplyMove(moves[chooser.NextInRange(0, moves.Count - 1)]);
        return true;
    }

    /// A button only people press (Collect trick, Next hand), as the person at the table would.
    static bool PressAButton(Game game)
    {
        foreach ((string actionName, Dictionary<string, object?> action) in game.Rules.Actions)
        {
            if (action.GetValueOrDefault("human_only") is true && game.Plays.CanPerform(actionName))
            {
                game.Apply(Input.Perform(actionName));
                return true;
            }
        }
        return false;
    }
}
