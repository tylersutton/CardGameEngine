using System.Globalization;
using System.Text;

namespace Core;

/// Checks a game file when it loads and parses every step and condition (StepParser), so mistakes (unknown names,
/// wrong argument counts, misspelled keys) show up before anything runs. Used once per load.
public class StepCompiler
{
    static readonly string[] ConditionalKeys = ["if", "then", "else"];
    static readonly string[] LoopKeys = ["for", "in", "do"];
    static readonly string[] WhileKeys = ["while", "do"];
    const string ViewerBlock = "viewer";
    /// Building blocks that wait for a player, so they only work in steps.
    static readonly string[] WaitingBlocks = ["choose", "choose_exactly", "choose_up_to", "offer", "offer_until"];
    public static readonly string[] OfferBlocks = ["offer", "offer_until"];
    /// Keys that are gone, with what to use instead.
    static readonly Dictionary<string, string> ReplacedKeys = new()
    {
        ["moves"] = "\"moves\" are now actions with \"drag\": { \"from\": ..., \"to\": ... } (see docs/reference.md, \"actions\")",
        ["on_click"] = "zones no longer have \"on_click\": make an action with \"click\": \"<zone>\" (or \"click\": true on a drag action)",
        ["on_double_click"] = "zones no longer have \"on_double_click\": make an action with \"double_click\": \"<zone>\" (or true on a drag action)",
        ["phase"] = "\"phase\" on actions is gone: offer the action from the \"turn\" steps with offer(...), or check a variable in \"when\"",
    };
    public static readonly string[] ZoneKeys =
    [
        "label", "labels", "outline", "layout", "face", "anchor", "anchors", "width", "rotation", "rotations", "cards",
        "spread", "drag", "per_player", "copies", "grid", "group", "covered_by", "columns", "board",
    ];
    public static readonly string[] CardFaceKeys = ["corner", "title", "body", "text", "color", "background", "zoom", "shape", "size", "back"];

    /// The kinds of value a setting in the game file can be.
    enum Kind { Text, List, Number, Bool, Object }

    static readonly Dictionary<Kind, string> KindDescriptions = new()
    {
        [Kind.Text] = "text", [Kind.List] = "a list", [Kind.Number] = "a number", [Kind.Bool] = "true or false", [Kind.Object] = "an object",
    };

    /// Where a text is used (see CollectSteps), strictest first.
    enum Place { Expression, StepCondition, Step }

    readonly Game game;
    readonly GameRules rules;
    readonly List<string> errors;
    /// Every name a step may use (zones, variables, loop names, conditions...), for quick lookups while checking.
    readonly HashSet<string> knownNames = [];
    /// Names set with = that aren't game variables (see LearnRememberedNames).
    readonly List<string> rememberedNames = [];

    public StepCompiler(Game game)
    {
        this.game = game;
        rules = game.Rules;
        errors = game.Errors;
    }

    /// Before any zone is made: misspelled keys, and settings that would stop the zones being built at all.
    public void CheckZoneDefinitions()
    {
        foreach (string key in rules.UnknownKeys)
        {
            if (key == "turn")
            {
                errors.Add("\"turn\" should be a list of steps");
            }
            else
            {
                errors.Add(ReplacedKeys.GetValueOrDefault(key) ?? $"Unknown key \"{key}\" at the top of the game file");
            }
        }
        foreach (string key in rules.CardFace.Keys.Where(key => !CardFaceKeys.Contains(key)))
        {
            errors.Add($"card_face: unknown key \"{key}\" (it has {string.Join(", ", CardFaceKeys)})");
        }
        if (rules.CardFace.GetValueOrDefault("size") is not (null or string or double))
        {
            errors.Add("card_face: \"size\" should be a column or a number of pixels");
        }
        foreach ((string zoneKind, object? value) in rules.Zones)
        {
            if (value is not Dictionary<string, object?> definition)
            {
                errors.Add($"Zone \"{zoneKind}\" should be an object");
                continue;
            }
            foreach (string key in definition.Keys)
            {
                if (ReplacedKeys.TryGetValue(key, out string? replaced))
                {
                    errors.Add($"Zone \"{zoneKind}\": {replaced}");
                }
                else if (!ZoneKeys.Contains(key))
                {
                    errors.Add($"Zone \"{zoneKind}\": unknown key \"{key}\"");
                }
            }
            object? columns = definition.GetValueOrDefault("columns", new Dictionary<string, object?>());
            if (columns is not Dictionary<string, object?> formulas || formulas.Values.Any(formula => formula is not string))
            {
                errors.Add($"Zone \"{zoneKind}\": \"columns\" should be column names with expressions, e.g. {{ \"points\": \"min(card.value, 10)\" }}");
            }
            else if (formulas.Count > 0 && !definition.ContainsKey("cards"))
            {
                errors.Add($"Zone \"{zoneKind}\": \"columns\" works out columns for its \"cards\", so it needs \"cards\"");
            }
            object? face = definition.GetValueOrDefault("face", Zone.FaceUp);
            if (face is not string faceName || !Zone.Faces.Contains(faceName) && faceName is not ("owner" or "human"))
            {
                errors.Add($"Zone \"{zoneKind}\": \"face\" should be {string.Join(", ", Zone.Faces)}");
            }
            bool perPlayer = definition.GetValueOrDefault("per_player") is true;
            if (perPlayer && definition.ContainsKey("copies"))
            {
                errors.Add($"Zone \"{zoneKind}\": use \"per_player\" or \"copies\", not both");
            }
            int copies = perPlayer ? rules.Players : WholeNumber(definition.GetValueOrDefault("copies"));
            if (definition.ContainsKey("board"))
            {
                copies = CheckBoard(zoneKind, definition);
            }
            object? grid = definition.GetValueOrDefault("grid");
            if (grid != null && !(grid is Dictionary<string, object?> gridSettings && gridSettings.GetValueOrDefault("columns") is double
                && gridSettings.GetValueOrDefault("spacing") is List<object?> { Count: 2 }))
            {
                errors.Add($"Zone \"{zoneKind}\": \"grid\" should look like {{ \"columns\": 5, \"spacing\": [0.09, 0.19] }}");
            }
            object? covering = definition.GetValueOrDefault("covered_by");
            if (covering != null && !(covering is List<object?> coveringLists && coveringLists.Count == copies && coveringLists.All(each => each is List<object?>)))
            {
                errors.Add($"Zone \"{zoneKind}\": \"covered_by\" needs one list of copy numbers per copy ({copies})");
            }
            if (definition.ContainsKey("group") && copies == 0)
            {
                errors.Add($"Zone \"{zoneKind}\": \"group\" needs \"copies\" or \"per_player\"");
            }
            if (copies > 0 && definition.GetValueOrDefault("anchors") is List<object?> anchors && anchors.Count < copies)
            {
                errors.Add($"Zone \"{zoneKind}\" needs {copies} anchors in \"anchors\" (one per {(perPlayer ? "player" : "copy")})");
            }
        }
    }

    /// A board is a grid of spaces, each a zone: it can't also be per player or have copies. Gives how many spaces it has.
    int CheckBoard(string zoneKind, Dictionary<string, object?> definition)
    {
        foreach (string key in new[] { "per_player", "copies", "grid", "anchors" }.Where(definition.ContainsKey))
        {
            errors.Add($"Zone \"{zoneKind}\": a \"board\" makes its own spaces, so it can't have \"{key}\"");
        }
        if (definition["board"] is not Dictionary<string, object?> board || WholeNumber(board.GetValueOrDefault("columns")) < 1
            || WholeNumber(board.GetValueOrDefault("rows")) < 1 || board.GetValueOrDefault("size") is not (null or double)
            || board.GetValueOrDefault("shape", Board.Square) is not string shape || !Board.Shapes.Contains(shape)
            || board.Keys.Any(key => key is not ("shape" or "columns" or "rows" or "size" or "paint")))
        {
            errors.Add($"Zone \"{zoneKind}\": \"board\" should look like {{ \"shape\": \"square\", \"columns\": 8, \"rows\": 8, \"size\": 60 }} (shape square or hex; size in pixels)");
            return 0;
        }
        if (board.GetValueOrDefault("paint", new Dictionary<string, object?>()) is not Dictionary<string, object?> paint || paint.Values.Any(color => color is not string))
        {
            errors.Add($"Zone \"{zoneKind}\": \"paint\" should be conditions with their colors, e.g. {{ \"top_edge\": \"#c8443a\" }}");
        }
        else
        {
            foreach (string condition in paint.Keys.Where(condition => rules.ConditionParameters.GetValueOrDefault(condition)?.Count != 1))
            {
                errors.Add($"Zone \"{zoneKind}\": \"paint\" names \"{condition}\", which should be a condition with one parameter, e.g. \"{condition}(space)\"");
            }
        }
        return WholeNumber(board["columns"]) * WholeNumber(board["rows"]);
    }

    /// After the zones and variables exist: everything else, then every step is compiled.
    public void CheckAndCompile()
    {
        CheckTeams();
        CheckNamesAreUnique();
        CheckActions();
        CheckMelds();
        if (errors.Count == 0)
        {
            CompileAllSteps();
        }
    }

    /// Problems with one step or condition on its own, e.g. for the editor as it's typed.
    public List<string> ProblemsWith(string step)
    {
        LearnNames();
        StepNode tree = StepParser.Parse(step);
        if (tree is UnreadableNode unreadable)
        {
            return [unreadable.Error];
        }
        List<string> problems = [];
        FindProblems(tree, step, Place.Step, problems);
        return problems;
    }

    void CheckTeams()
    {
        if (rules.TeamVariables.Count > 0 && rules.Teams.Count == 0)
        {
            errors.Add("\"team_variables\" needs \"teams\", e.g. [[1, 3], [2, 4]]");
        }
        for (int player = 1; player <= rules.Players; player++)
        {
            int teamCount = rules.Teams.Count(team => team is List<object?> members && members.Any(member => Values.IsNumber(member) && Values.ToDouble(member) == player));
            if (rules.Teams.Count > 0 && teamCount != 1)
            {
                errors.Add($"teams: player {player} should be on exactly one team");
            }
        }
    }

    void CheckNamesAreUnique()
    {
        errors.AddRange(rules.ConditionErrors);
        HashSet<string> seen = [];
        foreach (string name in game.InputNames())
        {
            if (!seen.Add(name))
            {
                errors.Add($"\"{name}\" is used as more than one zone or variable name");
            }
        }
        foreach ((string name, object? expression) in rules.Conditions)
        {
            if (seen.Contains(name) || game.Blocks.Has(name))
            {
                errors.Add($"Condition \"{name}\" has the same name as a zone, variable or building block");
            }
            else if (expression is not string)
            {
                errors.Add($"Condition \"{name}\" should be an expression in text");
            }
        }
    }

    void CheckActions()
    {
        Dictionary<string, Kind[]> actionFields = new()
        {
            ["when"] = [Kind.Text], ["choose"] = [Kind.Text], ["pick"] = [Kind.Number, Kind.Text], ["up_to"] = [Kind.Number, Kind.Text],
            ["allow"] = [Kind.Text], ["human_only"] = [Kind.Bool], ["button"] = [Kind.Bool], ["prefer"] = [Kind.Text], ["steps"] = [Kind.List],
            ["drag"] = [Kind.Object], ["click"] = [Kind.Bool, Kind.Text, Kind.List], ["double_click"] = [Kind.Bool, Kind.Text, Kind.List],
        };
        foreach ((string actionName, Dictionary<string, object?> action) in rules.Actions)
        {
            string label = $"Action \"{actionName}\"";
            if (action.ContainsKey("phase"))
            {
                errors.Add($"{label}: {ReplacedKeys["phase"]}");
                continue;
            }
            CheckEntry(label, action, actionFields);
            foreach (string key in new[] { "pick", "up_to", "allow" })
            {
                if (action.ContainsKey(key) && !action.ContainsKey("choose"))
                {
                    errors.Add($"{label}: \"{key}\" needs \"choose\"");
                }
            }
            if (action.ContainsKey("pick") && action.ContainsKey("up_to"))
            {
                errors.Add($"{label}: use \"pick\" (exactly that many) or \"up_to\" (any number up to it), not both");
            }
            if (action.ContainsKey("choose") && Plays.IsCardAction(action))
            {
                errors.Add($"{label}: \"choose\" is for buttons; an action done by a drag or click already has its card");
            }
            if (action.GetValueOrDefault(Plays.DragKey) is Dictionary<string, object?> drag)
            {
                CheckEntry($"{label}: \"drag\"", drag, new() { ["from"] = [Kind.Text, Kind.List], ["to"] = [Kind.Text, Kind.List] });
                CheckZoneNames(label, drag.GetValueOrDefault("from"));
                CheckZoneNames(label, drag.GetValueOrDefault("to"));
            }
            foreach (string key in new[] { Plays.ClickKey, Plays.DoubleClickKey })
            {
                object? binding = action.GetValueOrDefault(key);
                if (binding is bool && !action.ContainsKey(Plays.DragKey))
                {
                    errors.Add($"{label}: \"{key}\": true means clicking does its drag, so it needs \"drag\"; to click a zone, name it: \"{key}\": \"deck\"");
                }
                else if (action.ContainsKey(key) && action.ContainsKey(Plays.DragKey) && binding is not bool)
                {
                    errors.Add($"{label}: it has a \"drag\", so \"{key}\" can only be true (clicking the card does that drag). To have it done by clicking a zone instead, take the drag out.");
                }
                else if (binding is not bool)
                {
                    CheckZoneNames(label, binding);
                }
            }
        }
        if (rules.Seats.Count is not 0 and not 1 && rules.Seats.Count != rules.Players)
        {
            errors.Add($"\"seats\" needs one spot per player ({rules.Players}), or just seat 1's to place the rest round the table");
        }
        if (rules.PlayerNames.Count > 0 && rules.PlayerNames.Count < rules.Players)
        {
            errors.Add($"\"player_names\" needs one name per player ({rules.Players})");
        }
        if (rules.Bot.Count > 0)
        {
            CheckEntry("bot", rules.Bot, new() { ["players"] = [Kind.List], ["prefer"] = [Kind.Text], ["peek"] = [Kind.Bool], ["samples"] = [Kind.Number] });
        }
        foreach (string name in rules.Effects.Keys)
        {
            if (!Values.DeepEquals(rules.Triggers[name], rules.Effects[name]))
            {
                errors.Add($"\"{name}\" is both an effect and a trigger");
            }
        }
        foreach ((string name, object? steps) in rules.Triggers)
        {
            if (steps is not List<object?>)
            {
                errors.Add($"Trigger \"{name}\" should be a list of steps");
            }
        }
        foreach (object? name in rules.ShowVariables)
        {
            if (name is not string variable || !game.Variables.ContainsKey(variable))
            {
                errors.Add($"show_variables: unknown variable \"{Values.Text(name)}\"");
            }
        }
        foreach ((string label, object? expression) in rules.Status)
        {
            if (expression is not string)
            {
                errors.Add($"Status \"{label}\" should be an expression in text");
            }
        }
    }

    void CheckZoneNames(string label, object? zoneNames)
    {
        foreach (object? zoneName in zoneNames as List<object?> ?? [zoneNames])
        {
            if (zoneName is string name && !game.Zones.ContainsKey(name) && !game.Aliases().ContainsKey(name) && !game.ZoneGroups.ContainsKey(name))
            {
                errors.Add($"{label}: unknown zone \"{name}\"");
            }
        }
    }

    void CheckMelds()
    {
        if (rules.Melds.Count == 0)
        {
            return;
        }
        CheckEntry("melds", rules.Melds, new() { ["types"] = [Kind.List], ["points"] = [Kind.Text] });
        foreach (object? type in rules.Melds.GetValueOrDefault("types") as List<object?> ?? [])
        {
            string label = $"Meld type {GodotJson(type)}";
            CheckEntry(label, type, new() { ["same"] = [Kind.Text], ["sequence"] = [Kind.Text], ["min"] = [Kind.Number] });
            if (type is Dictionary<string, object?> settings && !settings.ContainsKey("same"))
            {
                errors.Add($"{label} needs \"same\"");
            }
        }
    }

    void CheckEntry(string label, object? entry, Dictionary<string, Kind[]> fieldKinds)
    {
        if (entry is not Dictionary<string, object?> settings)
        {
            errors.Add($"{label} should be an object");
            return;
        }
        foreach ((string key, object? value) in settings)
        {
            if (!fieldKinds.TryGetValue(key, out Kind[]? allowed))
            {
                errors.Add($"{label}: unknown key \"{key}\"");
                continue;
            }
            if (KindOf(value) is not { } kind || !allowed.Contains(kind))
            {
                errors.Add($"{label}: \"{key}\" should be {string.Join(" or ", allowed.Select(each => KindDescriptions[each]))}");
            }
        }
    }

    static Kind? KindOf(object? value) => value switch
    {
        string => Kind.Text,
        List<object?> => Kind.List,
        double or long => Kind.Number,
        bool => Kind.Bool,
        Dictionary<string, object?> => Kind.Object,
        _ => null,
    };

    void CompileAllSteps()
    {
        List<List<object?>> stepLists = [.. rules.Triggers.Values.OfType<List<object?>>(), rules.Turn];
        List<string> expressions = [];
        foreach (Dictionary<string, object?> action in rules.Actions.Values)
        {
            stepLists.Add(Plays.StepsOf(action));
            expressions.Add(action.GetValueOrDefault("when") as string ?? "");
            expressions.Add(action.GetValueOrDefault("choose") as string ?? "");
            foreach (string key in new[] { "pick", "up_to", "allow", "prefer" })
            {
                if (action.GetValueOrDefault(key) is string expression)
                {
                    expressions.Add(expression);
                }
            }
        }
        expressions.AddRange(rules.Status.Values.OfType<string>());
        expressions.AddRange(rules.Conditions.Values.OfType<string>());
        foreach (Dictionary<string, object?> zone in rules.Zones.Values.OfType<Dictionary<string, object?>>())
        {
            expressions.AddRange((zone.GetValueOrDefault("columns") as Dictionary<string, object?> ?? []).Values.OfType<string>());
        }
        expressions.Add(rules.Bot.GetValueOrDefault("prefer") as string ?? "");
        foreach (Zone zone in game.Zones.Values)
        {
            foreach (Card card in zone.Cards)
            {
                foreach (Zone destination in game.Zones.Values)
                {
                    stepLists.Add(game.CardEffect(card, destination.Kind));
                }
            }
        }
        Dictionary<string, Place> places = [];
        foreach (List<object?> steps in stepLists)
        {
            CollectSteps(steps, places);
        }
        foreach (string expression in expressions)
        {
            NotePlace(places, expression, Place.Expression);
        }
        Dictionary<string, StepNode> trees = [];
        foreach (string text in places.Keys)
        {
            StepNode tree = StepParser.Parse(text);
            if (tree is UnreadableNode unreadable)
            {
                errors.Add($"Could not read step \"{text}\": {unreadable.Error}");
            }
            else
            {
                trees[text] = tree;
            }
        }
        LearnRememberedNames(trees.Values);
        LearnNames();
        CheckConditionCircles();
        if (errors.Count > 0)
        {
            return;
        }
        foreach ((string text, StepNode tree) in trees)
        {
            List<string> problems = [];
            FindProblems(tree, text, places[text], problems);
            if (problems.Count > 0)
            {
                errors.Add($"Step \"{text}\": {string.Join(", ", problems)}");
            }
            else
            {
                game.StepTrees[text] = tree;
            }
        }
        CheckRememberedNamesAreUsed(trees.Values);
        LearnOfferedActions(trees.Values);
        foreach ((string text, StepNode tree) in trees)
        {
            if (!rules.Status.Values.Contains(text) && !rules.Conditions.Values.Contains(text) && Calls(tree, ViewerBlock, []))
            {
                errors.Add($"Step \"{text}\": viewer() only works in \"status\" (each screen has its own, so rules can't depend on it)");
            }
        }
    }

    /// Where each text is used, keeping the strictest place: a whole step may change things and wait for a choice,
    /// a step's own condition (an "if", a loop's "in", a "while") may wait but not change things, and anything else
    /// (a "when", a condition, a status) may do neither.
    void CollectSteps(List<object?> steps, Dictionary<string, Place> places)
    {
        foreach (object? step in steps)
        {
            if (step is string text)
            {
                NotePlace(places, text, Place.Step);
            }
            else if (step is Dictionary<string, object?> block && IsBlankCondition(block))
            {
                string written = GodotJson(block);
                errors.Add($"A step {written[..Math.Min(60, written.Length)]} has nothing to check: fill in its condition, or remove it");
            }
            else if (IsValidConditional(step))
            {
                Dictionary<string, object?> conditional = (Dictionary<string, object?>)step!;
                NotePlace(places, (string)conditional["if"]!, Place.StepCondition);
                CollectSteps(conditional.GetValueOrDefault("then") as List<object?> ?? [], places);
                CollectSteps(conditional.GetValueOrDefault("else") as List<object?> ?? [], places);
            }
            else if (IsValidLoop(step))
            {
                Dictionary<string, object?> loop = (Dictionary<string, object?>)step!;
                AddLoopName((string)loop["for"]!);
                NotePlace(places, (string)loop["in"]!, Place.StepCondition);
                CollectSteps(loop.GetValueOrDefault("do") as List<object?> ?? [], places);
            }
            else if (IsValidWhile(step))
            {
                Dictionary<string, object?> repeat = (Dictionary<string, object?>)step!;
                NotePlace(places, (string)repeat["while"]!, Place.StepCondition);
                CollectSteps(repeat.GetValueOrDefault("do") as List<object?> ?? [], places);
            }
            else
            {
                errors.Add($"Invalid step {GodotJson(step)}: use a string, {{ \"if\": ..., \"then\": [...], \"else\": [...] }}, {{ \"for\": \"name\", \"in\": ..., \"do\": [...] }}, or {{ \"while\": ..., \"do\": [...] }}");
            }
        }
    }

    static void NotePlace(Dictionary<string, Place> places, string text, Place place)
    {
        if (text.Trim().Length > 0)
        {
            places[text] = places.TryGetValue(text, out Place known) && known < place ? known : place;
        }
    }

    /// An "if", "for each ... in" or "repeat while" whose condition was left empty (e.g. just added in the editor).
    static bool IsBlankCondition(Dictionary<string, object?> step) =>
        new[] { "if", "in", "while" }.Any(key => step.GetValueOrDefault(key) is string text && text.Trim().Length == 0);

    void AddLoopName(string name)
    {
        List<string> names = [name, name + Game.LoopNumberSuffix];
        if (name == Game.PlayerVariable)
        {
            names.RemoveAt(0);
            if (!game.Variables.ContainsKey(Game.PlayerVariable))
            {
                errors.Add("Looping over \"player\" needs \"players\" of 2 or more");
            }
        }
        foreach (string loopName in names)
        {
            if (game.LocalNames.Contains(loopName))
            {
                continue;
            }
            if (game.InputNames().Contains(loopName) || rules.Conditions.ContainsKey(loopName))
            {
                errors.Add($"Loop name \"{loopName}\" is already used by a zone, variable or condition");
            }
            else
            {
                game.LocalNames.Add(loopName);
            }
        }
    }

    static bool IsValidConditional(object? step) =>
        step is Dictionary<string, object?> block
        && block.GetValueOrDefault("if") is string
        && block.Keys.All(ConditionalKeys.Contains)
        && block.GetValueOrDefault("then", new List<object?>()) is List<object?>
        && block.GetValueOrDefault("else", new List<object?>()) is List<object?>;

    static bool IsValidLoop(object? step) =>
        step is Dictionary<string, object?> block
        && block.GetValueOrDefault("for") is string name && IsIdentifier(name)
        && block.GetValueOrDefault("in") is string
        && block.Keys.All(LoopKeys.Contains)
        && block.GetValueOrDefault("do", new List<object?>()) is List<object?>;

    static bool IsValidWhile(object? step) =>
        step is Dictionary<string, object?> block
        && block.GetValueOrDefault("while") is string
        && block.Keys.All(WhileKeys.Contains)
        && block.GetValueOrDefault("do", new List<object?>()) is List<object?>;

    static bool IsIdentifier(string text) =>
        text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    void LearnNames()
    {
        knownNames.Clear();
        knownNames.UnionWith(game.InputNames());
        knownNames.UnionWith(rules.Conditions.Keys);
        knownNames.UnionWith(rules.ConditionParameters.Values.SelectMany(parameters => parameters));
    }

    void FindProblems(StepNode node, string text, Place place, List<string> problems)
    {
        switch (node)
        {
            case NameNode name:
                if (!knownNames.Contains(name.Name))
                {
                    problems.Add(text.Trim() == name.Name
                        ? $"unknown trigger \"{name.Name}\" (a card's on_ column names a trigger)"
                        : $"unknown zone or variable \"{name.Name}\"");
                }
                else if (rules.ConditionParameters.TryGetValue(name.Name, out List<string>? parameters) && parameters.Count > 0)
                {
                    problems.Add($"{name.Name} needs its {string.Join(", ", parameters)}: {name.Name}(...)");
                }
                break;
            case CallNode call:
                if (CallProblem(call.Name, call.Args.Count) is { } problem)
                {
                    problems.Add(problem);
                }
                if (WaitingBlocks.Contains(call.Name) && place == Place.Expression)
                {
                    problems.Add($"{call.Name} waits for a player, so it only works in steps (not in a when, a condition or a status)");
                }
                if (OfferBlocks.Contains(call.Name))
                {
                    problems.AddRange(OfferProblems(call));
                }
                break;
            case AssignNode assign:
                if (place != Place.Step)
                {
                    problems.Add("= changes a variable, so it only works as a step of its own; to compare, use ==");
                }
                else if (assign.Player != null)
                {
                    if (!game.IsPerPlayer(assign.Name) || !game.Variables.ContainsKey(game.NumberedName(assign.Name, 1) ?? ""))
                    {
                        problems.Add($"{assign.Name}[...] = only works for player (or team) variables, like score[2] = 0");
                    }
                }
                else if (!game.Variables.ContainsKey(game.Aliases().GetValueOrDefault(assign.Name, assign.Name)) && !game.LocalNames.Contains(assign.Name))
                {
                    problems.Add(AssignProblem(assign.Name));
                }
                break;
        }
        foreach (StepNode child in Children(node))
        {
            FindProblems(child, text, place, problems);
        }
    }

    /// A name set with = that isn't a game variable is remembered by the steps that set it (pile = choose(...)).
    /// Zones, conditions and the like can't be set, which FindProblems reports.
    void LearnRememberedNames(IEnumerable<StepNode> trees)
    {
        foreach (StepNode tree in trees)
        {
            string target = tree is AssignNode { Player: null } assign ? assign.Name : "";
            if (target.Length == 0 || game.LocalNames.Contains(target) || game.Variables.ContainsKey(game.Aliases().GetValueOrDefault(target, target)))
            {
                continue;
            }
            if (!game.InputNames().Contains(target) && !rules.Conditions.ContainsKey(target) && !game.Blocks.Has(target))
            {
                game.LocalNames.Add(target);
                rememberedNames.Add(target);
            }
        }
    }

    /// offer(...) names actions in quotes, so the game knows which actions wait to be offered (Plays.OfferedActions).
    List<string> OfferProblems(CallNode node)
    {
        List<string> problems = [];
        foreach (string? name in OfferedNames(node))
        {
            if (name == null)
            {
                problems.Add($"{node.Name} needs action names in quotes, e.g. offer(['Play a card', 'Draw'])");
            }
            else if (!rules.Actions.ContainsKey(name))
            {
                problems.Add($"{node.Name}: no action named \"{name}\"");
            }
        }
        return problems;
    }

    /// The names an offer(...) or offer_until(...) gives: its first argument, a name or a list of names (null for
    /// anything that isn't a name in quotes), plus offer_until's last one.
    static List<string?> OfferedNames(CallNode node)
    {
        List<string?> names = [];
        foreach (StepNode arg in node.Args.Take(node.Name == "offer_until" ? 2 : 1))
        {
            foreach (StepNode item in arg is ListNode list ? list.Items : [arg])
            {
                names.Add(item is ValueNode { Value: string name } ? name : null);
            }
        }
        return names;
    }

    void LearnOfferedActions(IEnumerable<StepNode> trees)
    {
        foreach (StepNode tree in trees)
        {
            foreach (CallNode call in CallsIn(tree, OfferBlocks))
            {
                foreach (string? name in OfferedNames(call))
                {
                    if (name != null)
                    {
                        game.Plays.OfferedActions.Add(name);
                    }
                }
            }
        }
    }

    static List<CallNode> CallsIn(StepNode node, string[] names)
    {
        List<CallNode> found = node is CallNode call && names.Contains(call.Name) ? [call] : [];
        foreach (StepNode child in Children(node))
        {
            found.AddRange(CallsIn(child, names));
        }
        return found;
    }

    /// A remembered name that's never read is most likely a misspelled variable (phse = 'draw').
    void CheckRememberedNamesAreUsed(IEnumerable<StepNode> trees)
    {
        HashSet<string> read = [.. trees.SelectMany(NamesIn)];
        foreach (string name in rememberedNames.Where(name => !read.Contains(name)))
        {
            errors.Add($"\"{name}\" is set but never used: is it misspelled? (Values the game keeps go in \"variables\")");
        }
    }

    public static List<StepNode> Children(StepNode node) => node switch
    {
        ListNode list => [.. list.Items],
        CallNode call => [.. call.Args],
        MemberNode member => [member.Target],
        IndexNode index => [index.Target, index.Index],
        UnaryNode unary => [unary.Term],
        BinaryNode binary => [binary.Left, binary.Right],
        ChooseNode choose => [choose.Condition, choose.IfTrue, choose.IfFalse],
        AssignNode assign => [assign.Value],
        _ => [],
    };

    string? CallProblem(string name, int given)
    {
        if (rules.ConditionParameters.TryGetValue(name, out List<string>? parameters))
        {
            if (parameters.Count == 0)
            {
                return $"{name} is a condition without parameters: write it without ( )";
            }
            return given != parameters.Count ? $"{name} takes {parameters.Count} ({string.Join(", ", parameters)}), got {given}" : null;
        }
        if (StepEvaluator.MathArguments.TryGetValue(name, out int needed))
        {
            if (name is "min" or "max")
            {
                return given < needed ? $"{name} takes two or more numbers, got {given}" : null;
            }
            return given != needed ? $"{name} takes one number, got {given}" : null;
        }
        if (!game.Blocks.Has(name))
        {
            return $"unknown building block \"{name}\"";
        }
        return game.Blocks.ArgumentCountProblem(name, given);
    }

    string AssignProblem(string name)
    {
        if (rules.Constants.ContainsKey(name))
        {
            return $"\"{name}\" is a constant, so = can't change it (make it a variable to change it)";
        }
        if (knownNames.Contains(name) && game.ValueOf(name) is Zone)
        {
            return $"\"{name}\" is a zone, so = can't change it (move cards with move or draw)";
        }
        if (knownNames.Contains(name))
        {
            return $"\"{name}\" can't be changed with =: only variables can (from \"variables\", \"player_variables\" or \"team_variables\")";
        }
        return $"unknown variable \"{name}\" (declare it in \"variables\")";
    }

    /// Whether the tree, or a condition it names, calls that building block.
    bool Calls(StepNode? node, string buildingBlock, HashSet<string> conditionsSeen)
    {
        if (node == null)
        {
            return false;
        }
        if (node is CallNode call && call.Name == buildingBlock)
        {
            return true;
        }
        if (node is NameNode name && rules.Conditions.TryGetValue(name.Name, out object? expression) && conditionsSeen.Add(name.Name)
            && Calls(game.StepTrees.GetValueOrDefault(expression as string ?? ""), buildingBlock, conditionsSeen))
        {
            return true;
        }
        return Children(node).Any(child => Calls(child, buildingBlock, conditionsSeen));
    }

    /// A condition that names itself, directly or through others, would never finish being worked out.
    void CheckConditionCircles()
    {
        foreach (string name in rules.Conditions.Keys)
        {
            if (ConditionNames(name, [name]).Contains(name))
            {
                errors.Add($"Condition \"{name}\" refers to itself (through {string.Join(" → ", ConditionPath(name, [name]))}), so it can never be worked out");
            }
        }
    }

    /// Every condition reachable from this one (stopping at any already on the path).
    HashSet<string> ConditionNames(string name, List<string> path)
    {
        HashSet<string> reached = [];
        foreach (string used in NamesIn(StepParser.Parse((string)rules.Conditions[name]!)).Where(rules.Conditions.ContainsKey))
        {
            reached.Add(used);
            if (!path.Contains(used))
            {
                reached.UnionWith(ConditionNames(used, [.. path, used]));
            }
        }
        return reached;
    }

    List<string> ConditionPath(string name, List<string> path)
    {
        foreach (string used in NamesIn(StepParser.Parse((string)rules.Conditions[name]!)))
        {
            if (used == path[0])
            {
                return [.. path, used];
            }
            if (rules.Conditions.ContainsKey(used) && !path.Contains(used))
            {
                List<string> found = ConditionPath(used, [.. path, used]);
                if (found.Count > 0)
                {
                    return found;
                }
            }
        }
        return [];
    }

    /// The names a tree reads, and the names it calls (so a condition called with parameters counts).
    static List<string> NamesIn(StepNode node)
    {
        List<string> names = node switch
        {
            NameNode name => [name.Name],
            CallNode call => [call.Name],
            _ => [],
        };
        foreach (StepNode child in Children(node))
        {
            names.AddRange(NamesIn(child));
        }
        return names;
    }

    static int WholeNumber(object? value) => Values.IsNumber(value) ? (int)Values.ToDouble(value) : 0;

    /// A value written as GDScript's JSON.stringify writes it (keys in order of their names), for messages.
    static string GodotJson(object? value)
    {
        StringBuilder text = new();
        Write(value);
        return text.ToString();

        void Write(object? item)
        {
            switch (item)
            {
                case null:
                    text.Append("null");
                    break;
                case bool flag:
                    text.Append(flag ? "true" : "false");
                    break;
                case string words:
                    text.Append(Quoted(words));
                    break;
                case long whole:
                    text.Append(whole.ToString(CultureInfo.InvariantCulture));
                    break;
                case double number:
                    text.Append(Values.Text(number));
                    break;
                case List<object?> list:
                    text.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        text.Append(i > 0 ? "," : "");
                        Write(list[i]);
                    }
                    text.Append(']');
                    break;
                case Dictionary<string, object?> dictionary:
                    text.Append('{');
                    bool first = true;
                    foreach (string key in dictionary.Keys.Order(StringComparer.Ordinal))
                    {
                        text.Append(first ? "" : ",").Append(Quoted(key)).Append(':');
                        Write(dictionary[key]);
                        first = false;
                    }
                    text.Append('}');
                    break;
                default:
                    text.Append(Quoted(Values.Format(item)));
                    break;
            }
        }

        static string Quoted(string words) =>
            "\"" + words.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }
}
