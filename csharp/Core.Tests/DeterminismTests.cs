using System.Text.Json;
using Core;

/// tests/determinism_test.gd: every game played with random legal moves (plus some bot moves and undos), feeding each
/// accepted input to a second copy made from the same seed; both copies stay identical after every input. Then the
/// match's record, sent through JSON like a network message, replays to the same state, and out-of-turn inputs are
/// refused.
public class DeterminismTests
{
    const long Seed = 1234;
    const int Steps = 150;
    const int BotMoves = 15;
    const int UndoEvery = 25;

    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    public static TheoryData<string> Games()
    {
        TheoryData<string> games = [];
        foreach (string path in files.GamePaths())
        {
            games.Add(path);
        }
        return games;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void CopiesStayIdentical(string path)
    {
        List<string> failures = [];
        Game game = new(files, path, Seed);
        Game copy = new(files, path, Seed);
        game.Failed += failures.Add;
        game.Start();
        copy.Start();
        Bot bot = new(game, (int)Seed);
        RandomNumbers chooser = new(Seed);
        int botMoves = 0;
        for (int step = 0; step < Steps; step++)
        {
            int appliedBefore = game.Inputs.Count;
            if (step % UndoEvery == UndoEvery - 1)
            {
                game.Apply(new("undo"));
            }
            else if (game.IsBotTurn() && botMoves < BotMoves && bot.TakeInput())
            {
                botMoves++;
            }
            else if (!RandomMove(game, chooser))
            {
                break;
            }
            foreach (PlayerInput input in game.Inputs.Skip(appliedBefore))
            {
                copy.Apply(input);
            }
            Assert.True(Snapshot.StateHash(copy) == Snapshot.StateHash(game), $"copies differ after input {game.Inputs.Count}");
        }
        Assert.Empty(failures);
        MatchRecord record = game.Record();
        string sent = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["rules"] = record.Rules, ["seed"] = record.Seed, ["inputs"] = record.Inputs.Select(input => input.ToPlain()).ToList(),
        });
        Dictionary<string, object?> received = (Dictionary<string, object?>)GameRules.Parse(sent)!;
        MatchRecord receivedRecord = new((string)received["rules"]!, (long)Values.ToDouble(received["seed"]),
            ((List<object?>)received["inputs"]!).Select(input => PlayerInput.FromPlain((Dictionary<string, object?>)input!)).ToList());
        Assert.Equal(Snapshot.StateHash(game), Snapshot.StateHash(Game.Replay(files, receivedRecord)));
    }

    [Fact]
    public void OutOfTurnInputsAreRefused()
    {
        Game game = new(files, "res://games/gin_rummy.jsonc", Seed);
        game.Start();
        PlayerInput move = game.Plays.LegalMoves()[0][0];
        int mover = game.CurrentPlayer();
        Assert.False(game.Apply(move with { Player = 3 - mover }), "the other player acted out of turn");
        Assert.True(game.Apply(move with { Player = mover }), "the player whose turn it is couldn't act");
    }

    /// A random legal move, or when there are none, a random toolbar button (like Hearts' "Collect trick").
    static bool RandomMove(Game game, RandomNumbers chooser)
    {
        List<List<PlayerInput>> moves = game.Plays.LegalMoves();
        if (moves.Count > 0)
        {
            game.ApplyMove(moves[chooser.NextInRange(0, moves.Count - 1)]);
            return true;
        }
        List<string> buttons = game.Rules.Actions.Keys.Where(action => game.Plays.ShowsButton(action) && game.Plays.CanPerform(action)).ToList();
        if (buttons.Count == 0)
        {
            return false;
        }
        game.Apply(PlayerInput.Perform(buttons[chooser.NextInRange(0, buttons.Count - 1)]));
        return true;
    }
}
