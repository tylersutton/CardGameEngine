using Core;
using Godot;

/// The game's result over the middle of the table once it's over: "You win!", or who won. A click puts it away, to
/// look at the table (every hand is shown at the end); it comes back if the game ends again after an undo. Online,
/// the host also gets buttons for what's next.
public partial class WinnerBanner : PanelContainer
{
    static readonly Color Gold = new(1, 0.82f, 0.3f);
    static readonly Color Background = new(0.08f, 0.08f, 0.08f, 0.9f);
    const int HeadlineFontSize = 44;
    const int DetailFontSize = 16;
    const float PopTime = 0.4f;
    const float StarRadius = 18;
    const float StarInset = 30;
    const int SideMargin = 64;
    const int TopMargin = 18;

    /// The control to sit in the middle of (the table).
    public Control Over { get; set; } = null!;

    readonly Label headline = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly Label detail = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly HBoxContainer buttons = new() { Alignment = BoxContainer.AlignmentMode.Center, Visible = false };
    Game? game;
    bool putAway;

    public WinnerBanner()
    {
        StyleBoxFlat box = new()
        {
            BgColor = Background, BorderColor = Gold, ContentMarginLeft = SideMargin, ContentMarginRight = SideMargin,
            ContentMarginTop = TopMargin, ContentMarginBottom = TopMargin,
        };
        box.SetBorderWidthAll(3);
        box.SetCornerRadiusAll(14);
        AddThemeStyleboxOverride("panel", box);
        headline.AddThemeFontSizeOverride("font_size", HeadlineFontSize);
        headline.AddThemeColorOverride("font_color", Gold);
        detail.AddThemeFontSizeOverride("font_size", DetailFontSize);
        VBoxContainer lines = new();
        lines.AddChild(headline);
        lines.AddChild(detail);
        lines.AddChild(buttons);
        AddChild(lines);
        Visible = false;
    }

    public override void _Ready()
    {
        Over.Resized += PlaceInMiddle;
    }

    public override void _ExitTree()
    {
        Over.Resized -= PlaceInMiddle;
        Forget();
    }

    public void AddButton(string text, Action pressed)
    {
        Button button = new() { Text = text };
        button.Pressed += pressed;
        buttons.AddChild(button);
        buttons.Visible = true;
    }

    public void ShowGame(Game gameToShow)
    {
        Forget();
        game = gameToShow;
        game.StateChanged += Refresh;
        putAway = false;
        Visible = false;
        Refresh();
    }

    void Forget()
    {
        if (game != null)
        {
            game.StateChanged -= Refresh;
        }
    }

    void Refresh()
    {
        if (!game!.Over)
        {
            putAway = false;
            Visible = false;
            return;
        }
        if (Visible || putAway)
        {
            return;
        }
        headline.Text = Headline();
        detail.Text = game.ResultText() + "  ·  click to see the table";
        Visible = true;
        PlaceInMiddle();
        PivotOffset = Size / 2;
        Scale = Vector2.One * 0.5f;
        CreateTween().TweenProperty(this, "scale", Vector2.One, PopTime).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// "You win!" for someone with a seat of their own (online, or the only person at a table of bots); otherwise
    /// the winners by name.
    string Headline()
    {
        List<int> winners = game!.Winners;
        if (winners.Count == 0)
        {
            return "Game over";
        }
        int people = Enumerable.Range(1, game.Rules.Players).Count(player => !game.IsBot(player));
        bool ownSeat = game.LocalSeat != 0 ? game.LocalSeat <= game.Rules.Players : people == 1;
        if (ownSeat && winners.Contains(game.Viewer()))
        {
            return "You win!";
        }
        string names = string.Join(" and ", winners.Select(game.SeatName));
        return names + (winners.Count == 1 ? " wins" : " win") + (ownSeat ? "" : "!");
    }

    void PlaceInMiddle()
    {
        ResetSize();
        GlobalPosition = Over.GlobalPosition + (Over.Size - Size) / 2;
    }

    public override void _Draw()
    {
        if (game?.Winners.Count > 0)
        {
            foreach (float x in new[] { StarInset, Size.X - StarInset })
            {
                DrawColoredPolygon(Star(new Vector2(x, Size.Y / 2)), Gold);
            }
        }
    }

    static Vector2[] Star(Vector2 center) =>
        Enumerable.Range(0, 10).Select(point => center + Vector2.FromAngle(Mathf.Pi / 5 * point - Mathf.Pi / 2) * (point % 2 == 0 ? StarRadius : StarRadius * 0.45f)).ToArray();

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true })
        {
            putAway = true;
            Visible = false;
            AcceptEvent();
        }
    }
}
