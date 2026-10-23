using Core;
using Godot;

/// The play screen: the toolbar (undo, sandbox), the table, the dock under it (the bar for choices and offers,
/// and the action buttons), and the log. Bots take their turns here; online, inputs go through Net.
public partial class Main : Control
{
    const float BotDelay = 0.6f;
    const string TitleScene = "res://scenes/title.tscn";
    const string LobbyScene = "res://scenes/lobby.tscn";
    const double WatchInterval = 1.0;

    [Export(PropertyHint.File, "*.jsonc,*.json")]
    public string RulesPath { get; set; } = "res://games/gin_rummy.jsonc";

    /// How many play, for a game that allows different numbers (0: the game's usual number).
    public int Players { get; set; }
    public Game? Game { get; private set; }
    /// Game and deck text to play instead of the files on disk (the editor's unsaved edits).
    public Dictionary<string, string> GivenFiles { get; set; } = [];
    /// Inside the editor: no Games or Reload buttons, and no reloading when files change (the editor reloads it).
    public bool Embedded { get; set; }

    Bot? bot;
    bool botWaiting;
    /// Modified times of the game file and its decks, to reload when one is saved.
    readonly Dictionary<string, ulong> watchedFiles = [];

    // Kept from _Ready: by the time _ExitTree runs, the children have left the tree and can't be looked up.
    TableView table = null!;
    LogView log = null!;
    WinnerBanner banner = null!;
    Net Net => Net.Instance;

    public override void _Ready()
    {
        table = GetNode<TableView>("%TableView");
        log = GetNode<LogView>("%LogView");
        banner = new() { Over = table };
        AddChild(banner);
        GetNode<Button>("%GamesButton").Pressed += LeaveTable;
        GetNode<Button>("%ReloadButton").Pressed += LoadGame;
        GetNode<Button>("%UndoButton").Pressed += () =>
        {
            if (Game != null)
            {
                Submit(new("undo"));
            }
        };
        GetNode<CheckButton>("%SandboxToggle").Toggled += SetSandbox;
        GetNode<CheckButton>("%ZoneNamesToggle").Toggled += on =>
        {
            table.ShowZoneNames = on;
            table.QueueRedraw();
        };
        GetNode<Button>("%GamesButton").Visible = !Embedded;
        GetNode<Button>("%ReloadButton").Visible = !Embedded;
        if (!Net.Online && !Embedded)
        {
            Timer watcher = new();
            watcher.Timeout += ReloadIfChanged;
            AddChild(watcher);
            watcher.Start(WatchInterval);
        }
        if (Net.Online)
        {
            GetNode<Button>("%ReloadButton").Disabled = true;
            GetNode<CheckButton>("%SandboxToggle").Disabled = true;
            GetNode<Button>("%GamesButton").Text = Net.IsHost() ? "Lobby" : "Leave";
            GetNode<Button>("%GamesButton").TooltipText = Net.IsHost() ? "Back to the lobby, for everyone" : "Leave the match (rejoin under the same name)";
            foreach ((string text, Action pressed) in HostChoicesWhenOver())
            {
                banner.AddButton(text, pressed);
            }
            Net.MatchStarted += LoadGame;
            Net.MatchEnded += GoToLobby;
            Net.UndoAsked += AskUndo;
            Net.Notice += log.Add;
            Net.Disconnected += Leave;
        }
        LoadGame();
    }

    public override void _ExitTree()
    {
        Net.MatchStarted -= LoadGame;
        Net.MatchEnded -= GoToLobby;
        Net.UndoAsked -= AskUndo;
        Net.Notice -= log.Add;
        Net.Disconnected -= Leave;
        ForgetGame();
    }

    /// The toolbar, the dock and the log around the table. The editor hides them while it is laying out the table,
    /// not playing.
    public void ShowPlayControls(bool shown)
    {
        GetNode<Control>("Layout/TableColumn/ToolbarMargin").Visible = shown;
        GetNode<Control>("%ActionDock").Visible = shown;
        GetNode<Control>("Layout/LogPanel").Visible = shown;
    }

    /// Offline the table changes the game directly, as the person at this screen (so not on a bot's turn); online,
    /// inputs go through the session.
    bool Submit(PlayerInput input) => Net.Online ? Net.Submit(input) : Game!.Apply(input with { Player = Game.Viewer() });

    /// Offline, back to the list of games. Online, the host takes everyone back to the lobby (asking first if the match
    /// isn't over); anyone else leaves the match.
    void LeaveTable()
    {
        if (!Net.IsHost())
        {
            Leave();
            return;
        }
        if (Game?.Over ?? true)
        {
            Net.ReturnToLobby();
            return;
        }
        ConfirmationDialog dialog = new() { DialogText = "End this match for everyone and go back to the lobby?", OkButtonText = "End match" };
        dialog.Confirmed += Net.ReturnToLobby;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    void Leave()
    {
        Net.Leave();
        GetTree().ChangeSceneToFile(TitleScene);
    }

    void GoToLobby()
    {
        GetTree().ChangeSceneToFile(LobbyScene);
    }

    /// The host's ways on once a match is over: the same game and seats again with a new deal, or the lobby.
    List<(string Text, Action Pressed)> HostChoicesWhenOver() => Net.IsHost() ? [("Play again", Net.StartMatch), ("Back to lobby", Net.ReturnToLobby)] : [];

    void AskUndo(string asker)
    {
        ConfirmationDialog dialog = new()
        {
            DialogText = $"{asker} wants to undo the last move. Allow it?", OkButtonText = "Allow", CancelButtonText = "Don't allow",
        };
        dialog.Confirmed += () => Net.AnswerUndo(true);
        dialog.Canceled += () => Net.AnswerUndo(false);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// Saving the game file or a deck reloads the game, so changes show up without pressing Reload.
    void ReloadIfChanged()
    {
        foreach ((string path, ulong modified) in watchedFiles)
        {
            if (FileAccess.GetModifiedTime(path) != modified)
            {
                LoadGame();
                log.Add($"[reloaded: {path.GetFile()} changed]");
                return;
            }
        }
    }

    public void LoadGame()
    {
        Game newGame = Net.Online ? Net.Game! : new(ProjectFiles.With(GivenFiles), RulesPath, -1, Players);
        watchedFiles.Clear();
        foreach (string path in newGame.Rules.FilesUsed(RulesPath))
        {
            watchedFiles[path] = FileAccess.GetModifiedTime(path);
        }
        if (newGame.Errors.Count > 0)
        {
            foreach (string error in newGame.Errors)
            {
                log.AddError(error);
            }
            return;
        }
        ForgetGame();
        Game = newGame;
        Game.Sandbox = GetNode<CheckButton>("%SandboxToggle").ButtonPressed;
        log.Clear();
        Game.GameLog.Logged += log.Add;
        Game.Failed += log.AddError;
        Game.StateChanged += RefreshActionButtons;
        Game.StateChanged += RefreshChoiceBar;
        Game.StateChanged += ScheduleBot;
        bot = new(Game);
        table.Submit = Submit;
        table.ShowGame(Game);
        GetNode<StatusView>("%StatusView").ShowGame(Game);
        banner.ShowGame(Game);
        AddActionButtons();
        Game.Start();
        if (Net.Online)
        {
            Net.CatchUp();
        }
    }

    /// Stops listening to the game being replaced (online, it lives on in Net after this screen is gone).
    void ForgetGame()
    {
        if (Game == null)
        {
            return;
        }
        Game.GameLog.Logged -= log.Add;
        Game.Failed -= log.AddError;
        Game.StateChanged -= RefreshActionButtons;
        Game.StateChanged -= RefreshChoiceBar;
        Game.StateChanged -= ScheduleBot;
    }

    void SetSandbox(bool enabled)
    {
        if (Game != null)
        {
            Game.Sandbox = enabled;
            RefreshActionButtons();
        }
    }

    void AddActionButtons()
    {
        Container buttons = GetNode<Container>("%ActionButtons");
        foreach (Node button in buttons.GetChildren())
        {
            button.Free();
        }
        foreach (string actionName in Game!.Rules.Actions.Keys.Where(Game.Plays.ShowsButton))
        {
            Button button = new() { Text = actionName };
            button.Pressed += () => Submit(PlayerInput.Perform(actionName));
            buttons.AddChild(button);
        }
    }

    void RefreshActionButtons()
    {
        GetNode<Button>("%UndoButton").Disabled = Game!.History.Count == 0;
        foreach (Button button in GetNode<Container>("%ActionButtons").GetChildren().Cast<Button>())
        {
            button.Disabled = !Game.Plays.CanPerform(button.Text) || !MayPress(button.Text);
        }
    }

    bool MayPress(string actionName) => Game!.MayAct(Game.Viewer(), PlayerInput.Perform(actionName));

    void RefreshChoiceBar()
    {
        Container bar = GetNode<Container>("%ChoiceBar");
        foreach (Node child in bar.GetChildren())
        {
            bar.RemoveChild(child);
            child.QueueFree();
        }
        Choice choice = Game!.Choice;
        bool pickingCards = choice.CardsOffered && !choice.Exactly;
        // The bar keeps its space even when empty, so the table doesn't jump up and down as choices come and go.
        bool shown = Game.Over || (choice.IsOpen || Game.Plays.Offered.Count > 0) && Game.IsLocalTurn();
        if (!shown)
        {
            return;
        }
        if (Game.Over)
        {
            bar.AddChild(new Label { Text = Game.ResultText() });
            foreach ((string text, Action pressed) in HostChoicesWhenOver())
            {
                AddChoiceButton(text, pressed);
            }
            if (Net.Online && !Net.IsHost())
            {
                bar.AddChild(new Label { Text = "Waiting for the host to play again or go back to the lobby" });
            }
            return;
        }
        if (!choice.IsOpen)
        {
            bar.AddChild(new Label { Text = "Your turn: " + string.Join(" or ", Game.Plays.Offered) });
            return;
        }
        Label prompt = new() { Text = choice.Title + ":" };
        if (pickingCards)
        {
            prompt.Text += $" choose up to {choice.Pick} ({choice.Selected.Count} selected)";
        }
        else if (choice.CardsOffered)
        {
            prompt.Text += " click " + (choice.Pick == 1 ? "a highlighted card" : $"{choice.Pick} highlighted cards ({choice.Selected.Count} selected)");
        }
        bar.AddChild(prompt);
        if (pickingCards)
        {
            AddChoiceButton("Done", () => Submit(PlayerInput.Finish()));
        }
        else if (!choice.CardsOffered)
        {
            foreach (object? option in choice.Options)
            {
                object? sent = option is Zone zone ? zone.Name : option;
                AddChoiceButton(choice.OptionText(option), () => Submit(PlayerInput.Choose(sent)));
            }
        }
    }

    void AddChoiceButton(string text, Action onPressed)
    {
        Button button = new() { Text = text };
        button.Pressed += onPressed;
        GetNode<Container>("%ChoiceBar").AddChild(button);
    }

    /// Bots play on the host only; everyone else gets their moves like any other input.
    async void ScheduleBot()
    {
        if (botWaiting || !Game!.IsBotTurn() || Net.Online && !Net.IsHost())
        {
            return;
        }
        botWaiting = true;
        await ToSignal(GetTree().CreateTimer(BotDelay), Timer.SignalName.Timeout);
        botWaiting = false;
        if (IsInsideTree())
        {
            bot!.TakeInput();
        }
    }
}
