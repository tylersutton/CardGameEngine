using System.Text.Json;
using Core;

/// Every game loads in C# as it does in GDScript (Reference/loading, from tests/record_reference.gd): GameRules'
/// fields, the zones built from them, and each deck's cards.
public class LoadingTests
{
    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    public static TheoryData<string> Games()
    {
        TheoryData<string> games = [];
        foreach (string path in Directory.GetFiles(RepositoryFiles.PathOf("csharp/Core.Tests/Reference/loading"), "*.json").Order())
        {
            games.Add(Path.GetFileNameWithoutExtension(path));
        }
        return games;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void RulesReadAsInGDScript(string game)
    {
        GameRules rules = GameRules.Load(files, GamePath(game));
        Dictionary<string, object?> fields = new()
        {
            ["title"] = rules.Title, ["description"] = rules.Description, ["zones"] = rules.Zones, ["actions"] = rules.Actions,
            ["triggers"] = rules.Triggers, ["effects"] = rules.Effects, ["conditions"] = rules.Conditions,
            ["condition_parameters"] = rules.ConditionParameters, ["condition_errors"] = rules.ConditionErrors,
            ["constants"] = rules.Constants, ["turn"] = rules.Turn, ["variables"] = rules.Variables, ["players"] = rules.Players,
            ["seats"] = rules.Seats, ["player_names"] = rules.PlayerNames, ["player_variables"] = rules.PlayerVariables,
            ["teams"] = rules.Teams, ["team_variables"] = rules.TeamVariables, ["melds"] = rules.Melds, ["status"] = rules.Status,
            ["show_variables"] = rules.ShowVariables, ["bot"] = rules.Bot, ["card_face"] = rules.CardFace,
            ["unknown_keys"] = rules.UnknownKeys, ["error"] = rules.Error,
        };
        Assert.Null(ReferenceJson.Difference(Recorded(game).GetProperty("rules"), fields, "rules"));
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void ZonesBuildAsInGDScript(string game)
    {
        GameRules rules = GameRules.Load(files, GamePath(game));
        List<Dictionary<string, object?>> zones = BuildZones(rules).Select(zone => new Dictionary<string, object?>
        {
            ["name"] = zone.Name, ["label"] = zone.Label, ["has_outline"] = zone.HasOutline, ["kind"] = zone.Kind,
            ["owner"] = zone.Owner, ["layout"] = zone.Layout, ["face"] = zone.Face,
            ["anchor"] = new List<object?> { (double)zone.Anchor.X, (double)zone.Anchor.Y }, ["width"] = zone.Width,
            ["rotation"] = zone.Rotation, ["drags_stack"] = zone.DragsStack, ["spread"] = zone.Spread, ["covered_by"] = zone.CoveredBy,
        }).ToList();
        // Anchors are 32-bit floats in Godot, turned round the table with its own sine and cosine.
        Assert.Null(ReferenceJson.Difference(Recorded(game).GetProperty("zones"), zones, "zones", tolerance: 1e-6));
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void DecksLoadAsInGDScript(string game)
    {
        foreach (JsonProperty deck in Recorded(game).GetProperty("decks").EnumerateObject())
        {
            List<Card> cards = DeckLoader.Load(files.Read(deck.Name));
            Assert.Equal(deck.Value.GetProperty("title_column").GetString(), cards[0].TitleColumn);
            Assert.Equal(
                deck.Value.GetProperty("number_columns").EnumerateArray().Select(column => column.GetString()).Order(),
                cards[0].NumberColumns.Order());
            Assert.Null(ReferenceJson.Difference(deck.Value.GetProperty("cards"), cards.Select(card => card.Data).ToList(), deck.Name));
        }
    }

    /// The zones as Game builds them: one of each, "copies" of one, or one per player.
    static List<Zone> BuildZones(GameRules rules)
    {
        List<Zone> zones = [];
        foreach ((string kind, object? value) in rules.Zones)
        {
            Dictionary<string, object?> definition = (Dictionary<string, object?>)value!;
            bool perPlayer = definition.GetValueOrDefault("per_player") is true;
            int copies = perPlayer ? rules.Players : (int)(double)(definition.GetValueOrDefault("copies") ?? 0.0);
            if (copies == 0)
            {
                zones.Add(new(kind, definition));
            }
            for (int number = 1; number <= copies; number++)
            {
                zones.Add(new(kind, definition, number, rules.Players));
            }
        }
        return zones;
    }

    static string GamePath(string game) =>
        files.GamePaths().Single(path => Path.GetFileNameWithoutExtension(path) == game);

    static JsonElement Recorded(string game) =>
        JsonDocument.Parse(File.ReadAllText(RepositoryFiles.PathOf($"csharp/Core.Tests/Reference/loading/{game}.json"))).RootElement;
}
