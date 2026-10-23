using Core;
using Godot;

/// Host or join a game, take seats, and (as host) fill empty seats with bots and start. Everyone comes back here
/// between matches. The fields start as they were last time (OnlineSettings).
public partial class LobbyScreen : Control
{
    const string TitleScene = "res://scenes/title.tscn";
    const string MainScene = "res://scenes/main.tscn";
    static readonly Color OpenSeatColor = new(1, 1, 1, 0.5f);

    /// The host's choice of how many play, for a game that allows different numbers.
    readonly OptionButton playerCount = new();
    readonly OnlineSettings settings = OnlineSettings.Load();

    /// Host or join again at once, as last time (the title screen's quick button).
    public bool ConnectAtOnce { get; set; }

    Net Net => Net.Instance;
    OptionButton GamePicker => GetNode<OptionButton>("%GamePicker");

    public override void _Ready()
    {
        GetNode<ColorRect>("%Felt").Color = TableView.FeltColor;
        GetNode<LineEdit>("%PlayerName").Text = settings.Name;
        GetNode<LineEdit>("%Address").Text = settings.Address;
        GetNode<SpinBox>("%HostPort").Value = settings.HostPort;
        GetNode<SpinBox>("%JoinPort").Value = settings.JoinPort;
        GameFiles files = ProjectFiles.With();
        foreach (string path in files.GamePaths())
        {
            GamePicker.AddItem(GameRules.Load(files, path).Title);
            GamePicker.SetItemMetadata(GamePicker.ItemCount - 1, path);
            if (path == settings.Game)
            {
                GamePicker.Select(GamePicker.ItemCount - 1);
            }
        }
        GetNode<Button>("%HostButton").Pressed += HostGame;
        GetNode<Button>("%JoinButton").Pressed += Join;
        GetNode<LineEdit>("%Address").TextSubmitted += _ => Join();
        GamePicker.ItemSelected += index => ChooseGame((string)GamePicker.GetItemMetadata((int)index));
        GamePicker.AddSibling(playerCount);
        playerCount.ItemSelected += index => Net.ChoosePlayers(playerCount.GetItemId((int)index));
        GetNode<Button>("%StartButton").Pressed += Net.StartMatch;
        GetNode<Button>("%LeaveButton").Pressed += Net.Leave;
        GetNode<Button>("%BackButton").Pressed += () =>
        {
            Net.Leave();
            GetTree().ChangeSceneToFile(TitleScene);
        };
        Net.LobbyChanged += Refresh;
        Net.Notice += ShowNotice;
        Net.MatchStarted += StartMatch;
        Refresh();
        if (ConnectAtOnce && !Net.Online)
        {
            if (settings.LastConnection == OnlineSettings.Hosted)
            {
                HostGame();
            }
            else
            {
                Join();
            }
        }
    }

    public override void _ExitTree()
    {
        Net.LobbyChanged -= Refresh;
        Net.Notice -= ShowNotice;
        Net.MatchStarted -= StartMatch;
    }

    void ShowNotice(string text)
    {
        GetNode<Label>("%Status").Text = text;
    }

    void StartMatch()
    {
        GetTree().ChangeSceneToFile(MainScene);
    }

    void HostGame()
    {
        Net.MyName = settings.Name = GetNode<LineEdit>("%PlayerName").Text.StripEdges();
        settings.HostPort = (int)GetNode<SpinBox>("%HostPort").Value;
        settings.LastConnection = OnlineSettings.Hosted;
        settings.Save();
        if (Net.HostGame(settings.HostPort) == Error.Ok)
        {
            ChooseGame((string)GamePicker.GetItemMetadata(GamePicker.Selected));
        }
    }

    void Join()
    {
        Net.MyName = settings.Name = GetNode<LineEdit>("%PlayerName").Text.StripEdges();
        settings.Address = GetNode<LineEdit>("%Address").Text.StripEdges();
        settings.JoinPort = (int)GetNode<SpinBox>("%JoinPort").Value;
        settings.LastConnection = OnlineSettings.Joined;
        settings.Save();
        Net.Join(settings.Address, settings.JoinPort);
    }

    void ChooseGame(string path)
    {
        settings.Game = path;
        settings.Save();
        Net.ChooseGame(path);
    }

    void Refresh()
    {
        GetNode<Control>("%Connect").Visible = !Net.Online;
        GetNode<LineEdit>("%PlayerName").Editable = !Net.Online;
        Control room = GetNode<Control>("%Room");
        room.Visible = Net.Online && Net.Lobby != null;
        if (!room.Visible)
        {
            return;
        }
        Lobby lobby = Net.Lobby!;
        GamePicker.Visible = Net.IsHost();
        for (int index = 0; index < GamePicker.ItemCount; index++)
        {
            if ((string)GamePicker.GetItemMetadata(index) == lobby.Game)
            {
                GamePicker.Select(index);
            }
        }
        playerCount.Clear();
        foreach (int count in lobby.PlayerCounts.Order())
        {
            playerCount.AddItem($"{count} players", count);
        }
        playerCount.Select(playerCount.GetItemIndex(lobby.Seats.Count));
        playerCount.Visible = Net.IsHost() && lobby.PlayerCounts.Count > 1;
        GetNode<Label>("%GameTitle").Visible = !Net.IsHost();
        GetNode<Label>("%GameTitle").Text = lobby.Title;
        GetNode<Label>("%Description").Text = lobby.Description;
        GetNode<Button>("%StartButton").Visible = Net.IsHost();
        GetNode<Button>("%StartButton").Disabled = !Net.CanStart();
        foreach (Node child in GetNode<Container>("%Seats").GetChildren())
        {
            child.QueueFree();
        }
        for (int i = 0; i < lobby.Seats.Count; i++)
        {
            AddSeat(i + 1, lobby.Seats[i], lobby.CanBot);
        }
    }

    /// One row per seat: its number, who's in it, a "Sit here" button, and (for the host) a Bot switch.
    void AddSeat(int seat, Seat occupant, bool canBot)
    {
        Container seats = GetNode<Container>("%Seats");
        seats.AddChild(new Label { Text = $"Seat {seat}" });
        Label who = new() { CustomMinimumSize = new Vector2(260, 0) };
        if (occupant.Bot)
        {
            who.Text = "Bot";
        }
        else if (occupant.Peer != 0)
        {
            who.Text = occupant.Name + (occupant.Peer == Multiplayer.GetUniqueId() ? " (you)" : "");
        }
        else
        {
            who.Text = "open";
            who.AddThemeColorOverride("font_color", OpenSeatColor);
        }
        seats.AddChild(who);
        Button sit = new() { Text = "Sit here", Disabled = occupant.Bot || occupant.Peer != 0 };
        sit.Pressed += () => Net.Sit(seat);
        seats.AddChild(sit);
        CheckBox bot = new() { Text = "Bot", ButtonPressed = occupant.Bot, Disabled = !Net.IsHost() || !canBot || occupant.Peer != 0 };
        bot.Toggled += on => Net.SetBot(seat, on);
        seats.AddChild(bot);
    }
}
