using System.Globalization;
using System.Text;
using Core;

// Records what every game does, for the tests in Core.Tests/Reference: what each game loads (rules, zones, decks),
// and two seeded matches of each (the inputs, and the log after each input). Each match is replayed first, to make
// sure the recording itself repeats. Rerun after changing a game file or what the engine does on purpose, then read
// the differences before committing them:
//   dotnet run --project csharp/Record
// (The random_numbers and sorting recordings are Godot's own answers, made once, and aren't rewritten here.)

const string Folder = "csharp/Core.Tests/Reference/";
long[] seeds = [1, 2];
const int MostInputs = 300;

GameFiles files = new(RepositoryFiles.PathOf(""));
List<string> problems = [];
foreach (string path in files.GamePaths())
{
    string name = Path.GetFileNameWithoutExtension(path);
    Write($"loading/{name}.json", Loading(path));
    List<object?> matches = [];
    foreach (long seed in seeds)
    {
        Dictionary<string, object?>? recorded = RecordMatch(path, seed);
        if (recorded == null)
        {
            break;
        }
        List<PlayerInput> inputs = ((List<object?>)recorded["inputs"]!).Select(input => PlayerInput.FromPlain((Dictionary<string, object?>)input!)).ToList();
        if (!Values.DeepEquals(LogsOf(path, seed, inputs), recorded["logs"]))
        {
            problems.Add($"{name} seed {seed}: replaying gave a different log");
        }
        matches.Add(recorded);
    }
    if (matches.Count > 0)
    {
        Write($"matches/{name}.json", matches);
        Console.WriteLine($"  {Path.GetFileName(path)}: {string.Join(", ", matches.Select(match => ((List<object?>)((Dictionary<string, object?>)match!)["inputs"]!).Count))} inputs");
    }
}
Console.WriteLine("RECORD: " + (problems.Count == 0 ? "done" : "FAILED\n  " + string.Join("\n  ", problems)));
return problems.Count == 0 ? 0 : 1;

/// What the game file reads as (every GameRules field), the zones the game builds from it, and the cards of each
/// deck it uses.
Dictionary<string, object?> Loading(string path)
{
    GameRules rules = GameRules.Load(files, path);
    Dictionary<string, object?> read = new()
    {
        ["title"] = rules.Title, ["description"] = rules.Description, ["how_to_play"] = rules.HowToPlay.Cast<object?>().ToList(), ["zones"] = rules.Zones, ["actions"] = rules.Actions,
        ["triggers"] = rules.Triggers, ["effects"] = rules.Effects, ["conditions"] = rules.Conditions,
        ["condition_parameters"] = rules.ConditionParameters, ["condition_errors"] = rules.ConditionErrors, ["constants"] = rules.Constants,
        ["turn"] = rules.Turn, ["variables"] = rules.Variables, ["players"] = (long)rules.Players, ["seats"] = rules.Seats,
        ["player_names"] = rules.PlayerNames, ["player_variables"] = rules.PlayerVariables, ["teams"] = rules.Teams,
        ["team_variables"] = rules.TeamVariables, ["melds"] = rules.Melds, ["status"] = rules.Status, ["show_variables"] = rules.ShowVariables,
        ["bot"] = rules.Bot, ["card_face"] = rules.CardFace, ["unknown_keys"] = rules.UnknownKeys, ["error"] = rules.Error,
    };
    List<object?> zones = new Game(files, path, 1).Zones.Values.Select(zone => (object?)new Dictionary<string, object?>
    {
        ["name"] = zone.Name, ["label"] = zone.Label, ["has_outline"] = zone.HasOutline, ["kind"] = zone.Kind, ["owner"] = (long)zone.Owner,
        ["layout"] = zone.Layout, ["face"] = zone.Face, ["anchor"] = new List<object?> { (double)zone.Anchor.X, (double)zone.Anchor.Y },
        ["width"] = zone.Width, ["rotation"] = zone.Rotation, ["drags_stack"] = zone.DragsStack, ["spread"] = (long)zone.Spread,
        ["covered_by"] = zone.CoveredBy,
    }).ToList();
    Dictionary<string, object?> decks = [];
    foreach (string deck in rules.FilesUsed(path).Skip(1))
    {
        List<Card> cards = DeckLoader.Load(files.Read(deck));
        List<string> numberColumns = cards.Count > 0 ? cards[0].Data.Keys.Where(cards[0].NumberColumns.Contains).ToList() : [];
        decks[deck] = new Dictionary<string, object?>
        {
            ["title_column"] = cards.Count > 0 ? cards[0].TitleColumn : "", ["number_columns"] = numberColumns,
            ["cards"] = cards.Select(card => (object?)card.Data).ToList(),
        };
    }
    return new() { ["rules"] = read, ["zones"] = zones, ["decks"] = decks };
}

/// Plays random legal moves (pressing a people-only button like Next hand when there are none), as the playthrough
/// test does. Null if the game doesn't load.
Dictionary<string, object?>? RecordMatch(string path, long seed)
{
    Game game = new(files, path, seed);
    if (game.Errors.Count > 0)
    {
        problems.Add($"{Path.GetFileName(path)} doesn't load: {game.Errors[0]}");
        return null;
    }
    List<string> lines = [];
    game.GameLog.Logged += lines.Add;
    game.Failed += text => lines.Add("[failed] " + text);
    game.Start();
    List<object?> logs = [lines.Cast<object?>().ToList()];
    List<object?> inputs = [];
    RandomNumbers chooser = new((ulong)seed);
    while (inputs.Count < MostInputs && !game.Over)
    {
        List<List<PlayerInput>> moves = game.Plays.LegalMoves();
        List<PlayerInput> move = moves.Count > 0 ? moves[chooser.NextInRange(0, moves.Count - 1)] : PeopleOnlyButton(game);
        if (move.Count == 0)
        {
            break;
        }
        foreach (PlayerInput input in move)
        {
            lines.Clear();
            if (game.Apply(input))
            {
                inputs.Add(input.ToPlain());
                logs.Add(lines.Cast<object?>().ToList());
            }
        }
    }
    return new() { ["seed"] = seed, ["inputs"] = inputs, ["logs"] = logs, ["end"] = EndState(game) };
}

List<PlayerInput> PeopleOnlyButton(Game game)
{
    string? button = game.Rules.Actions.Keys.FirstOrDefault(action => game.Rules.Actions[action].GetValueOrDefault("human_only") is true && game.Plays.CanPerform(action));
    return button != null ? [PlayerInput.Perform(button)] : [];
}

List<object?> LogsOf(string path, long seed, List<PlayerInput> inputs)
{
    Game game = new(files, path, seed);
    List<string> lines = [];
    game.GameLog.Logged += lines.Add;
    game.Failed += text => lines.Add("[failed] " + text);
    game.Start();
    List<object?> logs = [lines.Cast<object?>().ToList()];
    foreach (PlayerInput input in inputs)
    {
        lines.Clear();
        game.Apply(input);
        logs.Add(lines.Cast<object?>().ToList());
    }
    return logs;
}

/// Where every card ended up (face-down cards marked "▼"), the variables, and the result.
Dictionary<string, object?> EndState(Game game) => new()
{
    ["zones"] = game.Zones.ToDictionary(pair => pair.Key, pair => (object?)pair.Value.Cards.Select(card => (object?)((card.FaceUp ? "" : "▼") + card.Title)).ToList()),
    ["variables"] = game.Variables.ToDictionary(pair => pair.Key, pair => (object?)Values.Format(pair.Value)),
    ["over"] = game.Over,
    ["result"] = game.Over ? game.ResultText() : "",
};

void Write(string file, object? data)
{
    File.WriteAllText(RepositoryFiles.PathOf(Folder + file), GodotJson(data, ""));
}

/// JSON the way Godot writes it (tab indents, keys in their own order, decimals with every digit and always a point),
/// so recordings made here and in GDScript read the same.
static string GodotJson(object? value, string indent)
{
    string inner = indent + "\t";
    switch (value)
    {
        case System.Collections.IDictionary { Count: 0 }:
            return "{}";
        case System.Collections.IDictionary dictionary:
            IEnumerable<string> members = dictionary.Keys.Cast<object>().Select(key => inner + Quoted(key.ToString()!) + ": " + GodotJson(dictionary[key], inner));
            return "{\n" + string.Join(",\n", members) + "\n" + indent + "}";
        case System.Collections.ICollection { Count: 0 }:
            return "[]";
        case System.Collections.IEnumerable items and not string:
            return "[\n" + string.Join(",\n", items.Cast<object?>().Select(item => inner + GodotJson(item, inner))) + "\n" + indent + "]";
        case null:
            return "null";
        case bool flag:
            return flag ? "true" : "false";
        case string text:
            return Quoted(text);
        case double number:
            string written = number.ToString("R", CultureInfo.InvariantCulture);
            return written.Contains('.') || written.Contains('E') ? written : written + ".0";
        default:
            return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }
}

static string Quoted(string text)
{
    StringBuilder quoted = new("\"");
    foreach (char character in text)
    {
        quoted.Append(character switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => character.ToString(),
        });
    }
    return quoted.Append('"').ToString();
}
