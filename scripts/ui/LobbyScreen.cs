using Core;
using Godot;

/// Host or join a game, take seats, and (as host) fill empty seats with bots and start.
public partial class LobbyScreen : Control
{
    const string TitleScene = "res://scenes/title.tscn";
    const string MainScene = "res://scenes/main.tscn";
    static readonly Color OpenSeatColor = new(1, 1, 1, 0.5f);

    Net Net => Net.Instance;
    OptionButton GamePicker => GetNode<OptionButton>("%GamePicker");

    public override void _Ready()
    {
        GetNode<ColorRect>("%Felt").Color = TableView.FeltColor;
        GetNode<LineEdit>("%PlayerName").Text = Net.MyName;
        GetNode<SpinBox>("%HostPort").Value = Net.DefaultPort;
        GetNode<SpinBox>("%JoinPort").Value = Net.DefaultPort;
        GameFiles files = ProjectFiles.With();
        foreach (string path in files.GamePaths())
        {
            GamePicker.AddItem(GameRules.Load(files, path).Title);
            GamePicker.SetItemMetadata(GamePicker.ItemCount - 1, path);
        }
        GetNode<Button>("%HostButton").Pressed += HostGame;
        GetNode<Button>("%JoinButton").Pressed += Join;
        GamePicker.ItemSelected += index => Net.ChooseGame((string)GamePicker.GetItemMetadata((int)index));
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
        Net.MyName = GetNode<LineEdit>("%PlayerName").Text.StripEdges();
        if (Net.HostGame((int)GetNode<SpinBox>("%HostPort").Value) == Error.Ok)
        {
            Net.ChooseGame((string)GamePicker.GetItemMetadata(GamePicker.Selected));
        }
    }

    void Join()
    {
        Net.MyName = GetNode<LineEdit>("%PlayerName").Text.StripEdges();
        Net.Join(GetNode<LineEdit>("%Address").Text.StripEdges(), (int)GetNode<SpinBox>("%JoinPort").Value);
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
