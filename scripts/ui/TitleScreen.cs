using System.Text.RegularExpressions;
using Core;
using Editing;
using Godot;

/// The list of games: play one, open it in the editor, make a new one from a template, or go online.
public partial class TitleScreen : Control
{
    const string MainScene = "res://scenes/main.tscn";
    const string LobbyScene = "res://scenes/lobby.tscn";
    const string EditorScene = "res://scenes/editor.tscn";
    const string DecksFolder = "res://decks/";
    static readonly Dictionary<int, string> Templates = new() { [1] = "res://templates/new_solo_game.jsonc", [2] = "res://templates/new_game.jsonc" };
    const int MaxPlayers = 6;
    static readonly string[] SeatNames = ["You", "Player 2", "Player 3", "Player 4", "Player 5", "Player 6"];
    static readonly Color SelectedColor = new(1, 0.85f, 0.4f);

    static readonly StyleBoxFlat selectedStyle = SelectedBox();

    string selectedPath = "";
    /// For a game that can be played with different numbers of players: how many.
    readonly OptionButton playerCount = new();

    Button PlayButton => GetNode<Button>("%Play");

    public override void _Ready()
    {
        GetNode<ColorRect>("%Felt").Color = TableView.FeltColor;
        FitWindowToScreen();
        PlayButton.Pressed += () => Play(selectedPath);
        PlayButton.AddSibling(playerCount);
        PlayButton.GetParent().MoveChild(playerCount, PlayButton.GetIndex());
        GetNode<Button>("%OnlineButton").Pressed += () => GetTree().ChangeSceneToFile(LobbyScene);
        GetNode<Button>("%NewGameButton").Pressed += AskForNewGame;
        GetNode<Button>("%EditButton").Pressed += () => OS.ShellOpen(ProjectSettings.GlobalizePath(selectedPath));
        GetNode<Button>("%OpenEditorButton").Pressed += () => OpenEditor(selectedPath);
        ListGames();
    }

    void ListGames(string select = "")
    {
        Container gameList = GetNode<Container>("%GameList");
        foreach (Node child in gameList.GetChildren())
        {
            child.Free();
        }
        ButtonGroup group = new();
        GameFiles files = ProjectFiles.With();
        List<(string Path, GameRules Rules)> games = files.GamePaths().Select(path => (path, GameRules.Load(files, path))).ToList();
        games.Sort((a, b) => NaturalOrder(TitleOf(a.Path, a.Rules), TitleOf(b.Path, b.Rules)));
        foreach ((string path, GameRules rules) in games)
        {
            AddGame(path, rules, group);
        }
        Button? chosen = null;
        foreach (Button button in gameList.GetChildren().Cast<Button>())
        {
            if (chosen == null || (string)button.GetMeta("path") == select)
            {
                chosen = button;
            }
        }
        if (chosen != null)
        {
            chosen.ButtonPressed = true;
            chosen.GrabFocus();
        }
    }

    /// "New game...": a name, a player count and a deck make a playable game file from a template, ready to edit.
    void AskForNewGame()
    {
        ConfirmationDialog dialog = new() { Title = "New game", OkButtonText = "Create" };
        GridContainer form = new() { Columns = 2 };
        LineEdit gameName = new() { Text = "My Game", CustomMinimumSize = new Vector2(240, 0) };
        SpinBox players = new() { MinValue = 1, MaxValue = MaxPlayers, Value = 2 };
        OptionButton deck = new();
        foreach (string file in DirAccess.GetFilesAt(DecksFolder).Where(file => file.GetExtension() == "csv"))
        {
            deck.AddItem(file);
            if (file == "standard_deck.csv")
            {
                deck.Select(deck.ItemCount - 1);
            }
        }
        foreach ((string label, Control control) in new (string, Control)[] { ("Name", gameName), ("Players", players), ("Deck", deck) })
        {
            form.AddChild(new Label { Text = label });
            form.AddChild(control);
        }
        dialog.AddChild(form);
        dialog.Confirmed += () => CreateGame(gameName.Text.StripEdges(), (int)players.Value, deck.GetItemText(deck.Selected));
        AddChild(dialog);
        dialog.PopupCentered();
    }

    void CreateGame(string title, int players, string deck)
    {
        string baseName = title.Length > 0 ? ValidFileName(title.ToSnakeCase()) : "my_game";
        string path = GameFiles.GamesFolder + baseName + ".jsonc";
        for (int number = 2; FileAccess.FileExists(path); number++)
        {
            path = GameFiles.GamesFolder + $"{baseName}_{number}.jsonc";
        }
        Dictionary<string, string> placeholders = new()
        {
            ["title"] = title.Length > 0 ? title : "My Game",
            ["players"] = players.ToString(),
            ["deck"] = deck,
            ["player_names"] = GameDocument.Format(SeatNames.Take(players).Cast<object?>().ToList(), "\t"),
            ["bot_players"] = GameDocument.Format(Enumerable.Range(2, players - 1).Select(player => (object?)(long)player).ToList(), "\t\t"),
        };
        string text = FileAccess.GetFileAsString(Templates[Math.Min(players, 2)]);
        foreach ((string key, string value) in placeholders)
        {
            text = text.Replace("{" + key + "}", value);
        }
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(text);
        file.Close();
        OpenEditor(path);
    }

    static void FitWindowToScreen()
    {
        Vector2I screen = DisplayServer.ScreenGetUsableRect().Size;
        Vector2I window = DisplayServer.WindowGetSize();
        if (window.X > screen.X || window.Y > screen.Y)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
        }
    }

    /// Selecting a game (click, or arrow keys) shows its description; double-click or Play starts it.
    void AddGame(string path, GameRules rules, ButtonGroup group)
    {
        Button button = new()
        {
            Text = TitleOf(path, rules), ToggleMode = true, ButtonGroup = group, Alignment = HorizontalAlignment.Left,
        };
        button.SetMeta("path", path);
        button.AddThemeStyleboxOverride("pressed", selectedStyle);
        button.AddThemeColorOverride("font_pressed_color", Colors.Black);
        button.Toggled += pressed =>
        {
            if (pressed)
            {
                ShowGame(path, rules);
            }
        };
        button.FocusEntered += () => button.ButtonPressed = true;
        button.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { DoubleClick: true } && rules.Error.Length == 0)
            {
                Play(path);
            }
        };
        GetNode<Container>("%GameList").AddChild(button);
    }

    void ShowGame(string path, GameRules rules)
    {
        selectedPath = path;
        GetNode<Label>("%GameTitle").Text = TitleOf(path, rules);
        GetNode<Label>("%Description").Text = rules.Error.Length > 0 ? rules.Error : rules.Description;
        PlayButton.Disabled = rules.Error.Length > 0;
        playerCount.Clear();
        foreach (int count in rules.PlayerCounts.Order())
        {
            playerCount.AddItem($"{count} players", count);
        }
        playerCount.Select(playerCount.GetItemIndex(rules.Players));
        playerCount.Visible = rules.PlayerCounts.Count > 1;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept") && selectedPath.Length > 0 && !PlayButton.Disabled)
        {
            Play(selectedPath);
        }
    }

    /// Alphabetical, ignoring capitals, with numbers in number order ("Game 2" before "Game 10").
    static int NaturalOrder(string a, string b)
    {
        string[] aParts = NumberRuns().Split(a.ToLowerInvariant());
        string[] bParts = NumberRuns().Split(b.ToLowerInvariant());
        for (int i = 0; i < Math.Min(aParts.Length, bParts.Length); i++)
        {
            bool bothNumbers = long.TryParse(aParts[i], out long aNumber) & long.TryParse(bParts[i], out long bNumber);
            int order = bothNumbers ? aNumber.CompareTo(bNumber) : string.CompareOrdinal(aParts[i], bParts[i]);
            if (order != 0)
            {
                return order;
            }
        }
        return aParts.Length.CompareTo(bParts.Length);
    }

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex NumberRuns();

    /// Characters a file name can't have become _, as GDScript's validate_filename does.
    static string ValidFileName(string name) => string.Concat(name.Select(character => ":/\\?*\"|%<>".Contains(character) ? '_' : character));

    static string TitleOf(string path, GameRules rules) => rules.Error.Length == 0 ? rules.Title : path.GetFile();

    static StyleBoxFlat SelectedBox()
    {
        StyleBoxFlat box = new() { BgColor = SelectedColor, ContentMarginLeft = 4 };
        box.SetCornerRadiusAll(4);
        return box;
    }

    void OpenEditor(string path)
    {
        EditorScreen editor = GD.Load<PackedScene>(EditorScene).Instantiate<EditorScreen>();
        editor.FilePath = path;
        SwitchTo(editor);
    }

    void Play(string path)
    {
        Main main = GD.Load<PackedScene>(MainScene).Instantiate<Main>();
        main.RulesPath = path;
        main.Players = playerCount.Visible ? playerCount.GetSelectedId() : 0;
        SwitchTo(main);
    }

    void SwitchTo(Node scene)
    {
        GetTree().Root.AddChild(scene);
        GetTree().CurrentScene = scene;
        QueueFree();
    }
}
