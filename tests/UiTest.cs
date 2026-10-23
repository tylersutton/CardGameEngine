using Core;
using Editing;
using Godot;

/// Opens the real screens: the title screen, the lobby, every game on the table screen with bots playing every seat
/// for a few moves, the dock's keys, dimmed cards, and every game in the editor (edited and undone back to the file). Reports load
/// errors, failed steps and errors Godot reports.
/// Run: godot --headless res://tests/ui_test.tscn
public partial class UiTest : Node
{
    const string MainScene = "res://scenes/main.tscn";
    const string EditorScene = "res://scenes/editor.tscn";
    const int BotMoves = 12;

    readonly List<string> problems = [];

    public override async void _Ready()
    {
        ScriptErrors scriptErrors = ScriptErrors.Watch();
        foreach (string path in new[] { "res://scenes/title.tscn", "res://scenes/lobby.tscn" })
        {
            Node screen = GD.Load<PackedScene>(path).Instantiate();
            AddChild(screen);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            screen.QueueFree();
        }
        foreach (string path in ProjectFiles.With().GamePaths())
        {
            await Play(path);
            await Edit(path);
        }
        await PressKeys();
        await DimOffSuitCards();
        problems.AddRange(scriptErrors.Messages);
        GD.Print("UI TEST: ", problems.Count == 0 ? "all passed" : "FAILED\n  " + string.Join("\n  ", problems));
        GetTree().Quit(problems.Count == 0 ? 0 : 1);
    }

    /// The editor: open the game, drag a zone, change a deck cell, then undo it all back to the file as it was.
    async Task Edit(string path)
    {
        string original = ProjectFiles.With().Read(path);
        EditorScreen editor = GD.Load<PackedScene>(EditorScene).Instantiate<EditorScreen>();
        editor.FilePath = path;
        AddChild(editor);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        string name = path.GetFile();
        if (editor.Document.Text != original)
        {
            problems.Add($"{name}: the editor changed the file just by opening it");
        }
        Zone zone = editor.Preview.Game!.Zones.Values.First();
        editor.MoveZone(zone, new Vector2(0.3f, 0.3f));
        if (editor.Document.Text == original)
        {
            problems.Add($"{name}: dragging {zone.Name} didn't change the file");
        }
        editor.SelectSection("Deck");
        if (editor.Entry.Length > 0)
        {
            string deckText = ProjectFiles.With().Read(editor.Entry);
            editor.DeckEdited(deckText + deckText.Split('\n')[1] + "\n");
        }
        editor.SelectSection("Table");
        await ToSignal(GetTree().CreateTimer(0.5), Timer.SignalName.Timeout);
        if (editor.Preview.Game!.Errors.Count > 0)
        {
            problems.Add($"{name}: after editing in the editor: {string.Join("; ", editor.Preview.Game.Errors)}");
        }
        while (editor.UndoSteps > 0)
        {
            editor.Undo();
        }
        if (editor.Document.Text != original || editor.EditedDecks > 0)
        {
            problems.Add($"{name}: undoing every edit didn't give back the file");
        }
        CheckRuleEditing(editor, name);
        CheckRecipes(editor, name);
        CheckOutline(editor, name);
        editor.SetPlaying(true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        editor.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// The outline adds, renames, copies and removes entries; the Game file tab edits the file; the palette finds
    /// things. Undoing it all gives the file back.
    void CheckOutline(EditorScreen editor, string name)
    {
        string before = editor.Document.Text;
        editor.AddEntry("Table");
        editor.RenameEntry("Table", editor.Entry, "test_zone");
        if (!editor.Document.Has(["zones", "test_zone"]))
        {
            problems.Add($"{name}: adding and renaming a zone didn't work");
        }
        editor.Duplicate("Table", "test_zone");
        editor.RemoveEntry("Table", "test_zone_copy");
        if (editor.Document.Has(["zones", "test_zone_copy"]) || !editor.Document.Has(["zones", "test_zone"]))
        {
            problems.Add($"{name}: copying and removing a zone didn't work");
        }
        editor.AddEntry("Actions");
        editor.ShowTab(EditorScreen.FileTab);
        editor.FileEdited(editor.Document.Text.Replace("\"test_zone\"", "\"file_zone\""));
        if (!editor.Document.Has(["zones", "file_zone"]))
        {
            problems.Add($"{name}: editing the game file's text didn't change the game");
        }
        editor.ShowTab(EditorScreen.TableTab);
        editor.OpenPalette();
        editor.Palette.Search.Text = "file_zone";
        editor.Palette.Filter();
        if (editor.Palette.Shown.Count == 0 || editor.Palette.Shown[0].Text != "file_zone")
        {
            problems.Add($"{name}: the palette didn't find file_zone");
        }
        editor.Palette.Hide();
        while (editor.Document.Text != before && editor.UndoSteps > 0)
        {
            editor.Undo();
        }
        if (editor.Document.Text != before)
        {
            problems.Add($"{name}: undoing the outline edits didn't give back the file");
        }
    }

    /// The players, scoring and melds sections open, and every recipe can be added and undone.
    void CheckRecipes(EditorScreen editor, string name)
    {
        foreach (string section in new[] { "Players", "Scoring", "Melds" })
        {
            editor.SelectSection(section);
        }
        string before = editor.Document.Text;
        List<string> recipes = Recipes.Paths(ProjectFiles.With());
        foreach (string recipe in recipes)
        {
            editor.AddRecipe(recipe);
            if (editor.Document.ParseError.Length > 0)
            {
                problems.Add($"{name}: adding {recipe.GetFile()} broke the file: {editor.Document.ParseError}");
            }
        }
        foreach (string _ in recipes)
        {
            editor.Undo();
        }
        if (editor.Document.Text != before)
        {
            problems.Add($"{name}: undoing the recipes didn't give back the file");
        }
    }

    /// Steps are checked and suggested as they're typed, and step lists can be added to and changed.
    void CheckRuleEditing(EditorScreen editor, string name)
    {
        if (editor.CheckStep("draw(deck, no_such_zone)").Count == 0)
        {
            problems.Add($"{name}: a step naming a missing zone wasn't flagged");
        }
        SmartField field = editor.SmartField("tri");
        field.Code.SetCaretColumn(3);
        field.Suggest();
        List<string> options = Suggestions(field);
        if (!options.Any(text => text.StartsWith("trigger(")))
        {
            problems.Add($"{name}: no suggestion for 'tri': {string.Join(", ", options)}");
        }
        field.Free();
        SmartField quoted = editor.SmartField("offer('");
        quoted.Code.SetCaretColumn(7);
        quoted.Suggest();
        // Inside quotes, Godot shows the suggestions quoted and sorted.
        List<string> actionOptions = Suggestions(quoted).Select(text => text.Trim('\'')).Order().ToList();
        List<string> actions = editor.CheckedGame!.Rules.Actions.Keys.Order().ToList();
        if (!actionOptions.SequenceEqual(actions))
        {
            problems.Add($"{name}: offer(' should suggest the actions: {string.Join(", ", actionOptions)}");
        }
        quoted.Free();
        editor.SelectSection("Turn");
        editor.SelectSection("Triggers");
        if (editor.Entry.Length == 0)
        {
            return;
        }
        string before = editor.Document.Text;
        StepList steps = new(editor, ["triggers", editor.Entry]);
        steps.Add(new Dictionary<string, object?> { ["if"] = "true", ["then"] = new List<object?> { "shuffle(deck)" } });
        List<object?> list = (List<object?>)editor.Document.GetValue(["triggers", editor.Entry])!;
        if (list[^1] is not Dictionary<string, object?> added || !Equals(added.GetValueOrDefault("if"), "true"))
        {
            problems.Add($"{name}: adding an if block to a trigger didn't work");
        }
        steps.Move(list.Count - 1, 0);
        if (editor.Document.GetValue(["triggers", editor.Entry, 0]) is not Dictionary<string, object?>)
        {
            problems.Add($"{name}: moving a step up didn't work");
        }
        steps.Free();
        editor.Undo();
        editor.Undo();
        if (editor.Document.Text != before)
        {
            problems.Add($"{name}: undoing step edits didn't give back the trigger");
        }
    }

    /// Gin Rummy starts with each player taking the upcard or passing: Pass is the one gold button and the first
    /// one shown, so Space presses it, and then 1 does for the other player.
    async Task PressKeys()
    {
        Main main = GD.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.RulesPath = "res://games/gin_rummy.jsonc";
        AddChild(main);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Game game = main.Game!;
        game.Rules.Bot["players"] = new List<object?>();
        game.AnnounceStateChanged();
        foreach (Key key in new[] { Key.Space, Key.Key1 })
        {
            int inputs = game.Inputs.Count;
            GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (game.Inputs.Count != inputs + 1 || game.Inputs[^1].Action != "Pass")
            {
                problems.Add($"dock keys: {key} didn't press Pass (inputs: {string.Join(", ", game.Inputs)})");
            }
        }
        main.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await PressNumberKey();
    }

    /// Spades' bids are the buttons 0 to 13: 3 bids 3, not the third button.
    async Task PressNumberKey()
    {
        Main main = GD.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.RulesPath = "res://games/spades.jsonc";
        AddChild(main);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Game game = main.Game!;
        game.Rules.Bot["players"] = new List<object?>();
        game.AnnounceStateChanged();
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Key3, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (game.Inputs.Count != 1 || Values.Format(game.Inputs[0].Option) != "3")
        {
            problems.Add($"dock keys: 3 didn't bid 3 in Spades (inputs: {string.Join(", ", game.Inputs)})");
        }
        main.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// Hearts: when you must follow suit and hold other suits too, those other cards are dimmed, and only those.
    async Task DimOffSuitCards()
    {
        Main main = GD.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.RulesPath = "res://games/hearts.jsonc";
        AddChild(main);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Game game = main.Game!;
        List<object?> bots = (List<object?>)game.Rules.Bot["players"]!;
        game.Rules.Bot["players"] = Enumerable.Range(1, game.Rules.Players).Select(player => (object?)(double)player).ToList();
        Bot bot = new(game);
        List<Card> hand = game.Zones["hand_1"].Cards;
        bool MustFollowWithOthers()
        {
            string lead = Values.Text(game.Variables["lead"]);
            return game.CurrentPlayer() == 1 && !game.Choice.IsOpen && lead != ""
                && hand.Any(card => card.Data["suit"] == lead) && hand.Any(card => card.Data["suit"] != lead);
        }
        for (int move = 0; move < 200 && !MustFollowWithOthers(); move++)
        {
            bot.TakeInput();
        }
        game.Rules.Bot["players"] = bots;
        game.AnnounceStateChanged();
        TableView table = main.GetNode<TableView>("%TableView");
        string leadSuit = Values.Text(game.Variables["lead"]);
        List<Card> dimmed = hand.Where(card => table.CardViews[card].Dimmed).ToList();
        if (!MustFollowWithOthers() || !dimmed.SequenceEqual(hand.Where(card => card.Data["suit"] != leadSuit)))
        {
            problems.Add($"dimming: in Hearts, following {leadSuit}, dimmed {string.Join(", ", dimmed.Select(card => card.Title))}");
        }
        main.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    static List<string> Suggestions(SmartField field) =>
        field.Code.GetCodeCompletionOptions().Select(option => option["display_text"].AsString()).ToList();

    async Task Play(string path)
    {
        Main main = GD.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.RulesPath = path;
        AddChild(main);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Game? game = main.Game;
        if (game == null)
        {
            problems.Add($"{path.GetFile()}: didn't load");
            main.QueueFree();
            return;
        }
        game.Failed += text => problems.Add($"{path.GetFile()}: {text}");
        game.Rules.Bot["players"] = Enumerable.Range(1, game.Rules.Players).Select(player => (object?)(double)player).ToList();
        Bot bot = new(game);
        for (int move = 0; move < BotMoves; move++)
        {
            string? button = game.Rules.Actions.Keys.FirstOrDefault(action => game.Rules.Actions[action].GetValueOrDefault("human_only") is true && game.Plays.CanPerform(action));
            if (button != null)
            {
                game.Apply(PlayerInput.Perform(button));
            }
            else if (!bot.TakeInput())
            {
                break;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        main.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
