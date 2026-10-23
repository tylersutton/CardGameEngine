using Core;
using Godot;

/// The play screen: the toolbar (undo, how to play, the designer menu), the table, the dock under it (ActionDock) and the log.
/// Bots take their turns here; online, inputs go through Net.
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
    StatusView status = null!;
    ActionDock dock = null!;
    DesignerMenu designerMenu = null!;
    WinnerBanner banner = null!;
    readonly HowToPlayWindow howToPlay = new();
    Net Net => Net.Instance;

    public override void _Ready()
    {
        table = GetNode<TableView>("%TableView");
        log = GetNode<LogView>("%LogView");
        status = GetNode<StatusView>("%StatusView");
        dock = GetNode<ActionDock>("%ActionDock");
        dock.Submit = Submit;
        designerMenu = GetNode<DesignerMenu>("%DesignerMenu");
        banner = new() { Over = table };
        AddChild(banner);
        AddChild(howToPlay);
        GetNode<Button>("%HowToPlayButton").Pressed += () =>
        {
            if (Game != null)
            {
                howToPlay.ShowRules(Game.Rules);
            }
        };
        GetNode<Button>("%GamesButton").Pressed += LeaveTable;
        GetNode<Button>("%UndoButton").Pressed += () =>
        {
            if (Game != null)
            {
                Submit(new("undo"));
            }
        };
        designerMenu.ReloadPressed += LoadGame;
        designerMenu.SandboxToggled += SetSandbox;
        designerMenu.ZoneNamesToggled += on =>
        {
            table.ShowZoneNames = on;
            table.QueueRedraw();
        };
        designerMenu.ShowPlaysToggled += on =>
        {
            table.ShowPlays = on;
            table.RefreshPlays();
        };
        designerMenu.DesignerInfoToggled += ShowDesignerInfo;
        // Offline, whoever plays is most likely making the game; online, they're playing it with others.
        designerMenu.DesignerInfo = !Net.Online;
        ShowDesignerInfo(designerMenu.DesignerInfo);
        GetNode<Button>("%GamesButton").Visible = !Embedded;
        if (Embedded)
        {
            designerMenu.HideReload();
        }
        if (!Net.Online && !Embedded)
        {
            Timer watcher = new();
            watcher.Timeout += ReloadIfChanged;
            AddChild(watcher);
            watcher.Start(WatchInterval);
        }
        if (Net.Online)
        {
            designerMenu.DisableReloadAndSandbox();
            GetNode<Button>("%GamesButton").Text = Net.IsHost() ? "Lobby" : "Leave";
            GetNode<Button>("%GamesButton").TooltipText = Net.IsHost() ? "Back to the lobby, for everyone" : "Leave the match (rejoin under the same name)";
            foreach ((string text, Action pressed) in HostChoicesWhenOver())
            {
                banner.AddButton(text, pressed);
            }
            dock.ChoicesWhenOver = HostChoicesWhenOver();
            dock.WaitingWhenOver = Net.IsHost() ? "" : "Waiting for the host to play again or go back to the lobby";
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
        dock.Visible = shown;
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
        Game.Sandbox = designerMenu.Sandbox;
        log.ClearLines();
        Game.GameLog.Logged += log.Add;
        Game.Failed += log.AddError;
        Game.StateChanged += RefreshUndo;
        Game.StateChanged += dock.Refresh;
        Game.StateChanged += ScheduleBot;
        bot = new(Game);
        table.Submit = Submit;
        table.ShowGame(Game);
        status.ShowGame(Game);
        banner.ShowGame(Game);
        dock.ShowGame(Game);
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
        Game.StateChanged -= RefreshUndo;
        Game.StateChanged -= dock.Refresh;
        Game.StateChanged -= ScheduleBot;
    }

    void ShowDesignerInfo(bool shown)
    {
        log.ShowDesignerInfo(shown);
        status.ShowDesignerInfo(shown);
    }

    void SetSandbox(bool enabled)
    {
        if (Game != null)
        {
            Game.Sandbox = enabled;
            Game.AnnounceStateChanged();
        }
    }

    void RefreshUndo()
    {
        GetNode<Button>("%UndoButton").Disabled = Game!.History.Count == 0;
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
