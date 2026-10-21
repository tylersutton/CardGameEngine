using System.Text.Json;
using Core;

/// The C# engine plays the recorded matches (Reference/matches, from tests/record_reference.gd) exactly as GDScript
/// did: the same log after every input, and the same cards, variables and result at the end.
public class ReplayTests
{
    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    public static TheoryData<string, long> Matches()
    {
        TheoryData<string, long> matches = [];
        foreach (ReferenceMatch match in ReferenceMatch.All())
        {
            matches.Add(match.Game, match.Seed);
        }
        return matches;
    }

    [Fact]
    public void EveryGameLoadsWithoutErrors()
    {
        Assert.All(files.GamePaths(), path => Assert.Empty(new Game(files, path).Errors));
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void PlaysAsInGDScript(string gameName, long seed)
    {
        ReferenceMatch match = ReferenceMatch.All().Single(each => each.Game == gameName && each.Seed == seed);
        Game game = new(files, GamePath(gameName), seed);
        Assert.Empty(game.Errors);
        List<string> lines = [];
        game.GameLog.Logged += lines.Add;
        game.Failed += text => lines.Add("[failed] " + text);
        game.Start();
        List<List<string>> logs = [[.. lines]];
        foreach (JsonElement recorded in match.Inputs)
        {
            lines.Clear();
            Assert.True(game.Apply(InputFrom(recorded)), $"{match}: input {logs.Count} {recorded} was refused");
            logs.Add([.. lines]);
        }
        Assert.Null(match.FirstLogDifference(logs));
        Assert.Equal(match.End.Zones, game.Zones.ToDictionary(pair => pair.Key, pair => pair.Value.Cards.Select(card => (card.FaceUp ? "" : "▼") + card.Title).ToList()));
        Assert.Equal(match.End.Variables, game.Variables.ToDictionary(pair => pair.Key, pair => Values.Format(pair.Value)));
        Assert.Equal(match.End.Over, game.Over);
        Assert.Equal(match.End.Result, game.Over ? game.ResultText() : "");
    }

    static Input InputFrom(JsonElement recorded)
    {
        Dictionary<string, object?> fields = (Dictionary<string, object?>)GameRules.Parse(recorded.GetRawText())!;
        return new((string)fields["type"]!)
        {
            Card = WholeNumber(fields.GetValueOrDefault("card")),
            To = fields.GetValueOrDefault("to") as string,
            Index = WholeNumber(fields.GetValueOrDefault("index")),
            Zone = fields.GetValueOrDefault("zone") as string,
            Action = fields.GetValueOrDefault("action") as string,
            Option = fields.GetValueOrDefault("option"),
            Player = WholeNumber(fields.GetValueOrDefault("player")),
        };
    }

    static int? WholeNumber(object? value) => value is double number ? (int)number : null;

    static string GamePath(string game) => files.GamePaths().Single(path => Path.GetFileNameWithoutExtension(path) == game);
}
