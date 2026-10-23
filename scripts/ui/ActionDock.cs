using Core;
using Godot;

/// The dock under the table: the bar for choices and offers (what to do now) above the game's action buttons. A
/// button for an offered action shows only while it's offered to this screen, and what can be done now is gold, like
/// the name tag of the player whose turn it is.
public partial class ActionDock : MarginContainer
{
    const float ButtonRowHeight = 32;
    static readonly Color AccentColor = new(1, 0.85f, 0.4f);
    static readonly Color AccentHoverColor = new(1, 0.92f, 0.6f);
    static readonly Color AccentTextColor = new(0.1f, 0.1f, 0.1f);
    static readonly StyleBoxFlat accentStyle = AccentBox(AccentColor);
    static readonly StyleBoxFlat accentHoverStyle = AccentBox(AccentHoverColor);

    /// Where inputs go: the game itself, or the online session (set by Main).
    public Func<PlayerInput, bool> Submit { get; set; } = _ => false;
    /// The buttons once the game is over (online, the host's Play again and Back to lobby), and what anyone else
    /// is told then.
    public List<(string Text, Action Pressed)> ChoicesWhenOver { get; set; } = [];
    public string WaitingWhenOver { get; set; } = "";

    Game? game;
    Container choiceBar = null!;
    Container actionButtons = null!;

    public override void _Ready()
    {
        choiceBar = GetNode<Container>("%ChoiceBar");
        actionButtons = GetNode<Container>("%ActionButtons");
    }

    public void ShowGame(Game gameToShow)
    {
        game = gameToShow;
        foreach (Node button in actionButtons.GetChildren())
        {
            button.Free();
        }
        foreach (string actionName in game.Rules.Actions.Keys.Where(game.Plays.ShowsButton))
        {
            Button button = new() { Text = actionName };
            button.Pressed += () => Submit(PlayerInput.Perform(actionName));
            actionButtons.AddChild(button);
        }
        // The row keeps its height while every button is hidden, so the table doesn't jump as offers come and go.
        actionButtons.CustomMinimumSize = new(0, actionButtons.GetChildCount() > 0 ? ButtonRowHeight : 0);
    }

    public void Refresh()
    {
        RefreshActionButtons();
        RefreshChoiceBar();
    }

    void RefreshActionButtons()
    {
        foreach (Button button in actionButtons.GetChildren().Cast<Button>())
        {
            string actionName = button.Text;
            bool canPress = game!.Plays.CanPerform(actionName) && game.MayAct(game.Viewer(), PlayerInput.Perform(actionName));
            button.Visible = ShowsNow(actionName);
            button.Disabled = !canPress;
            SetAccent(button, canPress && !game.Plays.IsAnyTime(actionName));
        }
    }

    /// An action that's only done when offered shows while it's offered to this screen; the others always show.
    bool ShowsNow(string actionName) =>
        game!.Sandbox || !game.Plays.OfferedActions.Contains(actionName) || game.Plays.Offered.Contains(actionName) && game.IsLocalTurn();

    void RefreshChoiceBar()
    {
        foreach (Node child in choiceBar.GetChildren())
        {
            choiceBar.RemoveChild(child);
            child.QueueFree();
        }
        Choice choice = game!.Choice;
        bool pickingCards = choice.CardsOffered && !choice.Exactly;
        // The bar keeps its space even when empty, so the table doesn't jump up and down as choices come and go.
        bool shown = game.Over || (choice.IsOpen || game.Plays.Offered.Count > 0) && game.IsLocalTurn();
        if (!shown)
        {
            return;
        }
        if (game.Over)
        {
            choiceBar.AddChild(new Label { Text = game.ResultText() });
            foreach ((string text, Action pressed) in ChoicesWhenOver)
            {
                AddChoiceButton(text, pressed);
            }
            if (WaitingWhenOver.Length > 0)
            {
                choiceBar.AddChild(new Label { Text = WaitingWhenOver });
            }
            return;
        }
        if (!choice.IsOpen)
        {
            choiceBar.AddChild(new Label { Text = "Your turn: " + string.Join(" or ", game.Plays.Offered) });
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
        choiceBar.AddChild(prompt);
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
        SetAccent(button, true);
        choiceBar.AddChild(button);
    }

    static void SetAccent(Button button, bool on)
    {
        if (!on)
        {
            button.RemoveThemeStyleboxOverride("normal");
            button.RemoveThemeStyleboxOverride("hover");
            button.RemoveThemeStyleboxOverride("pressed");
            button.RemoveThemeColorOverride("font_color");
            button.RemoveThemeColorOverride("font_hover_color");
            button.RemoveThemeColorOverride("font_pressed_color");
            return;
        }
        button.AddThemeStyleboxOverride("normal", accentStyle);
        button.AddThemeStyleboxOverride("hover", accentHoverStyle);
        button.AddThemeStyleboxOverride("pressed", accentStyle);
        button.AddThemeColorOverride("font_color", AccentTextColor);
        button.AddThemeColorOverride("font_hover_color", AccentTextColor);
        button.AddThemeColorOverride("font_pressed_color", AccentTextColor);
    }

    static StyleBoxFlat AccentBox(Color color)
    {
        StyleBoxFlat box = new() { BgColor = color, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 };
        box.SetCornerRadiusAll(4);
        return box;
    }
}
