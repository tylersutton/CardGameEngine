namespace Core;

/// An action asked for (ask, ask_all, ask_others), waiting its turn, or the trigger to run once they're done.
/// Window: part of an ask_others response window, which a new window calls off.
public record AskedRequest(string Action = "", int Player = 0, string Trigger = "", bool Window = false);

/// The choice's state as plain data (for undo and bot try-outs).
public record ChoiceState(string Title, string Action, List<object?> Options, int Pick, bool Exactly, List<Card> Selected, bool Must, int ReturnPlayer, List<AskedRequest> Asked)
{
    public List<object?> Plain() => [Title, Action, Options, (long)Pick, Exactly, Selected.Cast<object?>().ToList(), Must, (long)ReturnPlayer, Asked.Cast<object?>().ToList()];
}

/// The choice being made, if any: a button's "choose" or a choose(...) step that waits for its answer. And the
/// asked-for choices (ask, ask_all, ask_others) waiting their turn, each made once the one before is finished.
public class Choice(Game game)
{
    const string ChoosePrompt = "Choose";

    /// What the choice is called (an action's name, or choose(...)'s prompt; "" while nothing is being chosen).
    public string Title { get; private set; } = "";
    /// The action whose steps run once it's made ("" for a choose(...) step, which carries on instead).
    public string Action { get; private set; } = "";
    public List<object?> Options { get; private set; } = [];
    /// How many to pick: exactly that many, or (not Exactly) up to that many.
    public int Pick { get; private set; } = 1;
    public bool Exactly { get; private set; } = true;
    public List<Card> Selected { get; private set; } = [];
    /// Asked for: clicking elsewhere doesn't cancel it, and nothing else happens until it's made.
    public bool Must { get; private set; }
    /// Whose turn it goes back to once the choice is made (asked of another player), or 0.
    public int ReturnPlayer { get; private set; }
    public List<AskedRequest> Asked { get; private set; } = [];
    public bool PerformingAsked { get; private set; }

    public bool IsOpen => Title != "";

    /// A choice a rule asked for (e.g. "discard down to 3") must be made before anything else happens.
    public bool MustBeMade => IsOpen && Must;

    public bool CardsOffered => IsOpen && Options.All(option => option is Card);

    /// What a choice offers: the cards of a zone (or of a mix of zones and cards), the zones of a list of zones (pick
    /// a pile), or the items of a list of values (pick a color).
    public static List<object?> OptionsFrom(object? value)
    {
        if (value is List<object?> zones && zones.Count > 0 && zones.All(option => option is Zone))
        {
            return [.. zones];
        }
        if (value is List<object?> items && items.Any(option => option is not (Card or Zone or List<object?>)))
        {
            return [.. items];
        }
        return Actions.CardsIn(value).Cast<object?>().ToList();
    }

    /// How an option is written on its button: a zone by its label, or its name when the label doesn't tell it apart.
    public string OptionText(object? option)
    {
        if (option is Zone zone)
        {
            int sameLabel = Options.Count(other => other is Zone otherZone && otherZone.Label == zone.Label);
            return zone.Label.Length > 0 && sameLabel == 1 ? zone.Label : zone.Name;
        }
        return Values.Format(option);
    }

    /// A button with "choose" was pressed (or asked for): its options are worked out and the choice opens.
    public void StartForAction(string actionName)
    {
        Dictionary<string, object?> button = game.Rules.Actions[actionName];
        List<object?> offered = OptionsFrom(game.Evaluate((string)button["choose"]!));
        (int count, bool exactly) = PickRule(button, offered.Count);
        if (offered.Count == 0 || offered.Count < count)
        {
            game.LogLine("  nothing to choose");
            return;
        }
        Open(actionName, actionName, offered, count, exactly, PerformingAsked);
    }

    /// A choose(...) step is waiting: the current player picks, and must (nothing else happens until they do).
    /// The step's answer is a single option, or for several (exactly or up to so many) a list.
    public void OpenInline(ChoiceRequest request)
    {
        Open(request.Prompt.Length > 0 ? request.Prompt : ChoosePrompt, "", request.Options, request.Amount, request.Exact, true);
    }

    public void Open(string title, string actionName, List<object?> options, int howMany, bool onlyExactly, bool mustMake)
    {
        Title = title;
        Action = actionName;
        Options = options;
        Pick = howMany;
        Exactly = onlyExactly;
        Selected = [];
        Must = mustMake;
        string chooser = game.PlayerName(game.CurrentPlayer());
        if (!CardsOffered)
        {
            game.LogLine("  choose one: " + string.Join(", ", Options.Select(Values.Format)));
        }
        else if (Exactly)
        {
            game.LogLine($"  {chooser}: choose {Pick} (click the highlighted cards{(Must ? "" : "; click anything else to cancel")})");
        }
        else
        {
            game.LogLine($"  {chooser}: choose up to {Pick} (click the highlighted cards, then Done)");
        }
    }

    /// A highlighted card clicked: chosen at once when it's the only one to pick, otherwise (un)selected, and the
    /// choice made once exactly enough are selected.
    public void Select(Card card)
    {
        if (Exactly && Pick == 1)
        {
            Complete(card);
        }
        else if (Selected.Contains(card))
        {
            Selected.Remove(card);
        }
        else if (Selected.Count < Pick)
        {
            Selected.Add(card);
            if (Exactly && Selected.Count == Pick)
            {
                Complete(Selected.Cast<object?>().ToList());
            }
        }
    }

    /// Runs the action's steps with the choice, or carries on the choose(...) step that was waiting for it. The turn
    /// goes back to whoever had it once those steps are done (or at the end of a choice they wait for in turn).
    public void Complete(object? made)
    {
        string actionName = Action;
        if (!Allows(actionName, made))
        {
            game.LogLine($"[not allowed] {game.Describe(made)}: requires {game.Rules.Actions[actionName]["allow"]}");
            return;
        }
        int backTo = ReturnPlayer;
        game.LogLine($"[{Title}] chose {game.Describe(made)}");
        Clear();
        if (actionName.Length > 0 && Plays.IsCardAction(game.Rules.Actions[actionName]))
        {
            game.PlayCardAction(actionName, (Card)made!);
        }
        else if (actionName.Length > 0)
        {
            game.Plays.AcceptOffer(actionName);
            game.WithChoice(made, () => game.Run(Plays.StepsOf(game.Rules.Actions[actionName]), made as Card));
        }
        else
        {
            game.Runner.Answer(made);
        }
        if (backTo != 0 && IsOpen)
        {
            ReturnPlayer = backTo;
        }
        else if (backTo != 0)
        {
            game.SetVariable(Game.PlayerVariable, (long)backTo);
        }
    }

    /// "allow" refuses a choice as a whole, e.g. cards that aren't all the same rank.
    public bool Allows(string actionName, object? made)
    {
        string allow = game.Rules.Actions.GetValueOrDefault(actionName)?.GetValueOrDefault("allow") as string ?? "";
        if (allow.Length == 0)
        {
            return true;
        }
        return game.WithChoice(made, () => game.Holds(allow, made as Card));
    }

    public void Cancel()
    {
        if (IsOpen && !Must)
        {
            game.LogLine("[cancelled] " + Title);
            Clear();
        }
    }

    public void Clear()
    {
        Title = "";
        Action = "";
        Options = [];
        Selected = [];
        Must = false;
        ReturnPlayer = 0;
        game.AnnounceStateChanged();
    }

    public void Ask(string actionName, int player = 0)
    {
        if (game.Rules.Actions.ContainsKey(actionName))
        {
            Asked.Add(new(actionName, player));
        }
        else
        {
            game.ReportFailure($"No action named \"{actionName}\" to ask for");
        }
    }

    /// Everyone makes the same choice, people first so nobody waits on the bots, then thenTrigger runs.
    /// Choices go somewhere hidden (like a face-down zone), so nobody sees the others' until the trigger reveals them.
    public void AskAll(string actionName, string thenTrigger)
    {
        if (thenTrigger.Length > 0 && !game.Rules.Triggers.ContainsKey(thenTrigger))
        {
            game.ReportFailure($"No trigger named \"{thenTrigger}\" in \"triggers\"");
            return;
        }
        List<int> players = Enumerable.Range(1, game.Rules.Players).ToList();
        foreach (int player in players.Where(each => !game.IsBot(each)).Concat(players.Where(game.IsBot)))
        {
            Ask(actionName, player);
        }
        if (thenTrigger.Length > 0)
        {
            Asked.Add(new(Trigger: thenTrigger));
        }
    }

    /// A response window: each other player in turn order is asked, then thenTrigger runs. A response that opens
    /// a new window (a "Nope" to a "Nope") calls off the rest of this one, so the trigger runs once, when nobody responds.
    public void AskOthers(string actionName, string thenTrigger)
    {
        if (thenTrigger.Length > 0 && !game.Rules.Triggers.ContainsKey(thenTrigger))
        {
            game.ReportFailure($"No trigger named \"{thenTrigger}\" in \"triggers\"");
            return;
        }
        Asked = Asked.Where(request => !request.Window).ToList();
        for (int seat = 1; seat < game.Rules.Players; seat++)
        {
            Asked.Add(new(actionName, (game.CurrentPlayer() - 1 + seat) % game.Rules.Players + 1, Window: true));
        }
        if (thenTrigger.Length > 0)
        {
            Asked.Add(new(Trigger: thenTrigger, Window: true));
        }
    }

    /// Asked-for actions run one at a time, each once the previous choice is finished.
    public void PerformAsked()
    {
        if (PerformingAsked)
        {
            return;
        }
        PerformingAsked = true;
        while (!game.Runner.Running && Asked.Count > 0 && !IsOpen)
        {
            AskedRequest request = Asked[0];
            Asked.RemoveAt(0);
            if (request.Trigger.Length > 0)
            {
                game.Trigger(request.Trigger);
                continue;
            }
            int turn = game.CurrentPlayer();
            int historySize = game.History.Count;
            if (request.Player != 0 && request.Player != turn)
            {
                game.SetVariable(Game.PlayerVariable, (long)request.Player);
            }
            game.Perform(request.Action);
            game.History.RemoveRange(historySize, game.History.Count - historySize);
            if (IsOpen)
            {
                ReturnPlayer = game.CurrentPlayer() != turn ? turn : 0;
            }
            else if (game.CurrentPlayer() != turn)
            {
                game.SetVariable(Game.PlayerVariable, (long)turn);
            }
        }
        PerformingAsked = false;
        game.Runner.Drive();
    }

    /// Every way to make the open choice, for bots: each as the inputs that make it.
    public List<List<PlayerInput>> Moves() => MovesFor(Action, Options, Pick, Exactly);

    /// Every way to make a button's choice once pressed (its options worked out now), for bots.
    public List<List<PlayerInput>> MovesForAction(string actionName)
    {
        Dictionary<string, object?> button = game.Rules.Actions[actionName];
        List<object?> offered = OptionsFrom(game.Evaluate((string)button["choose"]!));
        (int count, bool exactly) = PickRule(button, offered.Count);
        return MovesFor(actionName, offered, count, exactly);
    }

    public ChoiceState State() => new(Title, Action, [.. Options], Pick, Exactly, [.. Selected], Must, ReturnPlayer, [.. Asked]);

    public void Restore(ChoiceState saved)
    {
        Title = saved.Title;
        Action = saved.Action;
        Options = [.. saved.Options];
        Pick = saved.Pick;
        Exactly = saved.Exactly;
        Selected = [.. saved.Selected];
        Must = saved.Must;
        ReturnPlayer = saved.ReturnPlayer;
        Asked = [.. saved.Asked];
    }

    /// How many to pick, and whether exactly. "up_to" (a number or an expression) allows anything from none to that many.
    (int Count, bool Exactly) PickRule(Dictionary<string, object?> button, int optionCount)
    {
        if (button.TryGetValue("up_to", out object? upTo))
        {
            object? limit = upTo is double ? upTo : game.Evaluate((string)upTo!);
            return (Math.Min(WholeNumber(limit), optionCount), false);
        }
        object? howMany = button.GetValueOrDefault("pick", 1.0);
        return (WholeNumber(howMany is double ? howMany : game.Evaluate((string)howMany!)), true);
    }

    static int WholeNumber(object? value) => Values.IsNumber(value) ? (int)Values.ToDouble(value) : 0;

    List<List<PlayerInput>> MovesFor(string actionName, List<object?> options, int howMany, bool onlyExactly)
    {
        List<List<PlayerInput>> found = [];
        if (options.Count < howMany)
        {
            return found;
        }
        static List<PlayerInput> Clicks(List<object?> cards) => cards.Select(card => PlayerInput.Click((Card)card!)).ToList();
        if (!onlyExactly)
        {
            for (int size = 0; size <= howMany; size++)
            {
                foreach (List<object?> combination in Combinations(options, size))
                {
                    if (Allows(actionName, combination))
                    {
                        found.Add([.. Clicks(combination), PlayerInput.Finish()]);
                    }
                }
            }
        }
        else if (!options.All(option => option is Card))
        {
            foreach (object? option in options)
            {
                if (Allows(actionName, option))
                {
                    found.Add([PlayerInput.Choose(option is Zone zone ? zone.Name : option)]);
                }
            }
        }
        else if (howMany == 1)
        {
            foreach (object? card in options)
            {
                if (Allows(actionName, card))
                {
                    found.Add(Clicks([card]));
                }
            }
        }
        else
        {
            foreach (List<object?> combination in Combinations(options, howMany))
            {
                if (Allows(actionName, combination))
                {
                    found.Add(Clicks(combination));
                }
            }
        }
        return found;
    }

    static List<List<object?>> Combinations(List<object?> items, int size)
    {
        if (size == 0)
        {
            return [[]];
        }
        if (items.Count < size)
        {
            return [];
        }
        List<object?> rest = items[1..];
        List<List<object?>> withFirst = Combinations(rest, size - 1).Select(combination => (List<object?>)[items[0], .. combination]).ToList();
        return [.. withFirst, .. Combinations(rest, size)];
    }
}
