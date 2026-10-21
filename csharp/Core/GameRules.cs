using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Core;

/// A game file, read: its sections as plain values (Dictionary<string, object?>, List<object?>, string, double,
/// bool), the way StepCompiler checks them and the steps use them. Step lists are always lists, even where the
/// file gives a single step. Load problems that stop the game from being read at all go in Error.
public partial class GameRules
{
    public static readonly string[] Keys =
    [
        "title", "description", "players", "player_names", "seats", "teams", "zones", "variables", "player_variables",
        "team_variables", "show_variables", "status", "conditions", "constants", "effects", "actions", "turn", "triggers",
        "melds", "bot", "card_face",
    ];
    static readonly string[] NestedStepKeys = ["then", "else", "do"];

    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public Dictionary<string, object?> Zones { get; private set; } = [];
    /// Each action's settings; an action written as just steps becomes {"steps": [...]}.
    public Dictionary<string, Dictionary<string, object?>> Actions { get; } = [];
    public Dictionary<string, object?> Triggers { get; private set; } = [];
    /// "effects" is the older name for triggers that cards name in their on_<zone> column.
    public Dictionary<string, object?> Effects { get; private set; } = [];
    /// Named expressions, by name; a condition written "points(cards)" takes parameters, listed by name in
    /// ConditionParameters (a malformed name is listed in ConditionErrors).
    public Dictionary<string, object?> Conditions { get; } = [];
    public Dictionary<string, List<string>> ConditionParameters { get; } = [];
    public List<string> ConditionErrors { get; } = [];
    /// Named values that never change, e.g. "shell_points": [0, 2, 4, 6, 8, 10].
    public Dictionary<string, object?> Constants { get; private set; } = [];
    /// The steps of a turn, run again and again for whoever's turn it is (empty: no turns run by themselves).
    public List<object?> Turn { get; private set; } = [];
    public Dictionary<string, object?> Variables { get; private set; } = [];
    public int Players { get; private set; } = 1;
    public List<object?> Seats { get; private set; } = [];
    public List<object?> PlayerNames { get; private set; } = [];
    public Dictionary<string, object?> PlayerVariables { get; private set; } = [];
    public List<object?> Teams { get; private set; } = [];
    public Dictionary<string, object?> TeamVariables { get; private set; } = [];
    public Dictionary<string, object?> Melds { get; private set; } = [];
    public Dictionary<string, object?> Status { get; private set; } = [];
    public List<object?> ShowVariables { get; private set; } = [];
    public Dictionary<string, object?> Bot { get; private set; } = [];
    public Dictionary<string, object?> CardFace { get; private set; } = DefaultCardFace();
    /// Top-level keys the file shouldn't have, for StepCompiler to report ("turn" too, when it isn't a list).
    public List<string> UnknownKeys { get; } = [];
    public string Error { get; private set; } = "";

    public static GameRules Load(GameFiles files, string path)
    {
        GameRules rules = new();
        if (!files.Exists(path))
        {
            rules.Error = "Game file not found: " + path;
            return rules;
        }
        try
        {
            if (Parse(files.Read(path)) is not Dictionary<string, object?> data)
            {
                rules.Error = $"{Path.GetFileName(path)}: the game file should be an object, {{ ... }}";
                return rules;
            }
            rules.Read(data, path);
        }
        catch (JsonException error)
        {
            string message = error.Message.Split(" LineNumber:")[0].Split(" Path:")[0];
            rules.Error = $"{Path.GetFileName(path)}, line {error.LineNumber + 1}: {message}";
        }
        return rules;
    }

    /// JSONC text (JSON with // and /* */ comments and trailing commas) as plain values: objects as
    /// Dictionary<string, object?>, lists as List<object?>, and every number as a double, as GDScript's JSON gives them.
    public static object? Parse(string text)
    {
        JsonDocumentOptions options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using JsonDocument document = JsonDocument.Parse(text, options);
        return PlainValue(document.RootElement);
    }

    /// Every file a game reads: the game file and its decks (for reloading when one changes).
    public List<string> FilesUsed(string path)
    {
        List<string> paths = [path];
        foreach (object? zone in Zones.Values)
        {
            if (zone is Dictionary<string, object?> definition && definition.GetValueOrDefault("cards") is string deck)
            {
                paths.Add(deck);
            }
        }
        return paths;
    }

    void Read(Dictionary<string, object?> data, string path)
    {
        UnknownKeys.AddRange(data.Keys.Where(key => !Keys.Contains(key)));
        Title = Section(data, "title", Capitalize(Path.GetFileNameWithoutExtension(path)));
        Description = Section(data, "description", "");
        Zones = Section<Dictionary<string, object?>>(data, "zones", []);
        foreach ((string actionName, object? action) in Section<Dictionary<string, object?>>(data, "actions", []))
        {
            Dictionary<string, object?> settings = action as Dictionary<string, object?> ?? new() { ["steps"] = action };
            if (settings.TryGetValue("steps", out object? steps))
            {
                settings["steps"] = AsSteps(steps);
            }
            Actions[actionName] = settings;
        }
        if (AsSteps(data.GetValueOrDefault("turn", new List<object?>())) is List<object?> turn)
        {
            Turn = turn;
        }
        else
        {
            UnknownKeys.Add("turn");
        }
        Triggers = StepsByName(Section<Dictionary<string, object?>>(data, "triggers", []));
        Effects = StepsByName(Section<Dictionary<string, object?>>(data, "effects", []));
        foreach ((string name, object? steps) in Effects)
        {
            Triggers.TryAdd(name, steps);
        }
        ReadConditions(Section<Dictionary<string, object?>>(data, "conditions", []));
        Constants = WholeNumbers(Section<Dictionary<string, object?>>(data, "constants", []), insideLists: true);
        Variables = WholeNumbers(Section<Dictionary<string, object?>>(data, "variables", []));
        Players = (int)Section(data, "players", 1.0);
        Seats = Section<List<object?>>(data, "seats", []);
        PlayerNames = Section<List<object?>>(data, "player_names", []);
        PlayerVariables = WholeNumbers(Section<Dictionary<string, object?>>(data, "player_variables", []));
        Teams = Section<List<object?>>(data, "teams", []);
        TeamVariables = WholeNumbers(Section<Dictionary<string, object?>>(data, "team_variables", []));
        Melds = Section<Dictionary<string, object?>>(data, "melds", []);
        Status = Section<Dictionary<string, object?>>(data, "status", []);
        ShowVariables = Section<List<object?>>(data, "show_variables", []);
        Bot = Section<Dictionary<string, object?>>(data, "bot", []);
        CardFace = Section(data, "card_face", DefaultCardFace());
    }

    /// A top-level section, or its default when the file leaves it out. One of the wrong kind stops the game
    /// loading, as a JSON mistake does.
    T Section<T>(Dictionary<string, object?> data, string key, T fallback)
    {
        if (!data.TryGetValue(key, out object? value))
        {
            return fallback;
        }
        if (value is T typed)
        {
            return typed;
        }
        Error = Error.Length > 0 ? Error : $"\"{key}\" should be {KindName(typeof(T))}";
        return fallback;
    }

    static string KindName(Type type)
    {
        if (type == typeof(string))
        {
            return "text in quotes";
        }
        if (type == typeof(double))
        {
            return "a number";
        }
        return type == typeof(List<object?>) ? "a list, [ ... ]" : "an object, { ... }";
    }

    /// "name" or "name(parameter, ...)" keys: the expressions by name, and each one's parameters.
    void ReadConditions(Dictionary<string, object?> written)
    {
        foreach ((string key, object? expression) in written)
        {
            Match found = ConditionName().Match(key);
            List<string> parameters = found.Success && found.Groups[2].Success
                ? found.Groups[2].Value.Split(',').Where(parameter => parameter.Length > 0).Select(parameter => parameter.Trim()).ToList()
                : [];
            if (!found.Success || !parameters.All(IsIdentifier))
            {
                ConditionErrors.Add($"Condition \"{key}\": write a name, or a name with parameters like points(cards)");
                continue;
            }
            Conditions[found.Groups[1].Value] = expression;
            ConditionParameters[found.Groups[1].Value] = parameters;
        }
    }

    /// A condition's name, maybe with parameters: "fits" or "points(cards, color)".
    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\(([^)]*)\))?\s*$")]
    private static partial Regex ConditionName();

    static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    /// Wherever a list of steps goes, a single step will do: "then": "end_turn()".
    static object? AsSteps(object? steps)
    {
        if (steps is string or Dictionary<string, object?>)
        {
            return AsSteps(new List<object?> { steps });
        }
        if (steps is not List<object?> list)
        {
            return steps;
        }
        foreach (Dictionary<string, object?> step in list.OfType<Dictionary<string, object?>>())
        {
            foreach (string key in NestedStepKeys.Where(step.ContainsKey))
            {
                step[key] = AsSteps(step[key]);
            }
        }
        return list;
    }

    static Dictionary<string, object?> StepsByName(Dictionary<string, object?> lists)
    {
        foreach (string name in lists.Keys.ToList())
        {
            lists[name] = AsSteps(lists[name]);
        }
        return lists;
    }

    /// JSON numbers all arrive as decimals; starting values like 0 or 4 become whole numbers, so % and list
    /// positions work.
    static Dictionary<string, object?> WholeNumbers(Dictionary<string, object?> values, bool insideLists = false)
    {
        foreach (string name in values.Keys.ToList())
        {
            values[name] = WholeNumber(values[name], insideLists);
        }
        return values;
    }

    static object? WholeNumber(object? value, bool insideLists) => value switch
    {
        double number when number == Math.Floor(number) => (long)number,
        List<object?> items when insideLists => items.Select(item => WholeNumber(item, true)).ToList(),
        _ => value,
    };

    static object? PlainValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => PlainObject(element),
        JsonValueKind.Array => element.EnumerateArray().Select(PlainValue).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    // A key written twice keeps its last value, as in GDScript.
    static Dictionary<string, object?> PlainObject(JsonElement element)
    {
        Dictionary<string, object?> values = [];
        foreach (JsonProperty property in element.EnumerateObject())
        {
            values[property.Name] = PlainValue(property.Value);
        }
        return values;
    }

    static Dictionary<string, object?> DefaultCardFace() => new() { ["corner"] = "name", ["title"] = "name" };

    /// GDScript's capitalize(), for a title from the file's name: "gin_rummy" is "Gin Rummy", "myGame2" is
    /// "My Game 2". Words start at underscores, spaces, a capital after a small letter ("aB"), the last capital
    /// before small letters ("HTMLParser"), and where letters and digits meet ("game2", "2game").
    static string Capitalize(string name)
    {
        StringBuilder words = new();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && StartsWord(name, i))
            {
                words.Append(' ');
            }
            words.Append(name[i] == '_' ? ' ' : char.ToLowerInvariant(name[i]));
        }
        return string.Join(" ", words.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    static bool StartsWord(string name, int i)
    {
        char previous = name[i - 1];
        char current = name[i];
        bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
        return char.IsLower(previous) && char.IsUpper(current)
            || (char.IsUpper(previous) || char.IsAsciiDigit(previous)) && char.IsUpper(current) && nextIsLower
            || char.IsAsciiDigit(previous) && char.IsLower(current) && nextIsLower
            || (char.IsUpper(previous) || char.IsLower(previous)) && char.IsAsciiDigit(current);
    }
}
