using System.Text.Json;

namespace Core;

/// A match's rules, seed and inputs: replaying them rebuilds it exactly.
public record MatchRecord(string Rules, long Seed, List<Input> Inputs);

/// A game being played: its zones, cards, variables and players. It carries out inputs (Apply) and runs the
/// game file's steps (with StepRunner); Plays knows what players can do and Choice is the choice being made.
public class Game : IStepContext
{
    public const string CardVariable = StepEvaluator.CardVariable;
    public const string ChoiceVariable = "choice";
    public const string PlayersVariable = "players";
    public const string AllPrefix = "all_";
    public const string FromVariable = "from";
    public const string ToVariable = "to";
    public const string ClickedZoneVariable = "clicked_zone";
    public const string PlayerVariable = "player";
    public const string OpponentPrefix = "opponent_";
    public const string TeamPrefix = "team_";
    public const string CardEffectPrefix = "on_";
    public const string HiddenCard = "a hidden card";
    public const char CardEffectSeparator = ';';
    public const string LoopNumberSuffix = "_number";

    static readonly BuildingBlocks buildingBlocks = new(typeof(Actions));

    public event Action<string>? Failed;
    public event Action? VariablesChanged;
    public event Action? StateChanged;
    public event Action<Input>? InputApplied;

    public GameRules Rules { get; } = new();
    public string RulesPath { get; }
    public long MatchSeed { get; }
    public RandomNumbers Random { get; }
    public GameFiles Files { get; }
    public List<Card> Cards { get; } = [];
    public List<Input> Inputs { get; } = [];
    /// The player this screen belongs to (0 when people share a screen).
    public int LocalSeat { get; set; }
    public bool Started { get; private set; }
    public Dictionary<string, Zone> Zones { get; } = [];
    public Dictionary<string, object?> Variables { get; } = [];
    public List<string> PerPlayerNames { get; } = [];
    public List<string> PerTeamNames { get; } = [];
    public Dictionary<string, List<Zone>> ZoneGroups { get; } = [];
    public Melds MeldRules { get; private set; } = new([]);
    /// Each step's text, parsed by StepParser.
    public Dictionary<string, StepNode> StepTrees { get; } = [];
    public bool EvaluationFailed { get; private set; }
    public List<string> Errors { get; } = [];
    public bool Sandbox { get; set; }
    public StepRunner Runner { get; }
    /// The choice being made (and those asked for), and what players can do (actions and offers).
    public Choice Choice { get; }
    public Plays Plays { get; private set; }
    /// What "choice" means in the steps being run: what was just chosen.
    public object? Chosen { get; private set; }
    /// Set by EndGame(): no more turns, and nothing waits for an offer.
    public bool Over { get; set; }
    /// Who won, as given to end_game(...) (empty: nobody, or not over yet).
    public List<int> Winners { get; set; } = [];
    /// Players out of the game (knock_out): turns skip them.
    public List<int> KnockedOut { get; set; } = [];
    public List<Snapshot> History { get; } = [];
    /// Whether inputs are kept for undo: a bot trying inputs out turns it off, as it undoes them itself.
    public bool Remembering { get; set; } = true;
    public GameLog GameLog { get; } = new();
    /// Names that steps remember without declaring them: loop items (and their _number) and names set with =.
    public List<string> LocalNames { get; } = [];
    /// While a bot tries moves out, nothing is announced.
    public bool Quiet { get; private set; }

    readonly Dictionary<int, Dictionary<string, string>> aliasesByPlayer = [];
    readonly Dictionary<int, Dictionary<string, List<Zone>>> teamGroupsByTeam = [];
    readonly Dictionary<string, Condition> conditionTrees = [];
    Zone? moveFrom;
    Zone? moveTo;
    Zone? clickedZone;
    bool checkingMoves;
    bool checkedItself;

    /// A seed makes the match repeatable: the same seed and inputs always give the same game.
    public Game(GameFiles files, string path, long seed = -1)
    {
        Runner = new(this);
        Choice = new(this);
        Plays = new(this, []);
        Files = files;
        RulesPath = path;
        MatchSeed = seed >= 0 ? seed : System.Random.Shared.NextInt64(0, 1L << 32);
        Random = new((ulong)MatchSeed);
        Rules = GameRules.Load(files, path);
        if (Rules.Error.Length > 0)
        {
            Errors.Add(Rules.Error);
            return;
        }
        Plays = new(this, Rules.Actions);
        StepCompiler compiler = new(this);
        compiler.CheckZoneDefinitions();
        if (Errors.Count > 0)
        {
            return;
        }
        foreach ((string zoneKind, object? value) in Rules.Zones)
        {
            Dictionary<string, object?> definition = (Dictionary<string, object?>)value!;
            bool perPlayer = definition.GetValueOrDefault("per_player") is true;
            int copies = perPlayer ? Rules.Players : (int)(definition.GetValueOrDefault("copies") as double? ?? 0);
            if (copies == 0)
            {
                AddZone(new(zoneKind, definition), definition);
                continue;
            }
            string groupName = perPlayer ? AllPrefix + zoneKind : zoneKind;
            if (perPlayer)
            {
                PerPlayerNames.Add(zoneKind);
            }
            ZoneGroups[groupName] = [];
            for (int number = 1; number <= copies; number++)
            {
                Zone zone = new(zoneKind, definition, number, Rules.Players);
                AddZone(zone, definition);
                ZoneGroups[groupName].Add(zone);
                if (definition.GetValueOrDefault("group") is string group)
                {
                    if (!ZoneGroups.TryGetValue(group, out List<Zone>? members))
                    {
                        ZoneGroups[group] = members = [];
                    }
                    members.Add(zone);
                }
            }
        }
        foreach ((string name, object? start) in Rules.Variables)
        {
            Variables[name] = Values.DeepCopy(start);
        }
        if (Rules.Players > 1)
        {
            Variables[PlayerVariable] = 1L;
        }
        AddNumberedVariables(Rules.PlayerVariables, Rules.Players, PerPlayerNames);
        AddNumberedVariables(Rules.TeamVariables, Rules.Teams.Count, PerTeamNames);
        MeldRules = new(Rules.Melds);
        compiler.CheckAndCompile();
        if (Errors.Count == 0)
        {
            ComputeColumns();
        }
    }

    public void Start()
    {
        Started = true;
        Trigger("start");
        GameLog.Flush();
        Choice.PerformAsked();
        AnnounceStateChanged();
    }

    public void Perform(string actionName)
    {
        if (!Plays.CanPerform(actionName))
        {
            return;
        }
        Choice.Cancel();
        Snapshot? before = UndoPoint();
        Dictionary<string, object?> action = Rules.Actions[actionName];
        LogLine("[action] " + actionName);
        if (action.ContainsKey("choose"))
        {
            Choice.StartForAction(actionName);
        }
        else if (Plays.IsCardAction(action))
        {
            Choice.Open(actionName, actionName, Plays.PlayableCards(actionName).Cast<object?>().ToList(), 1, true, Choice.PerformingAsked);
        }
        else
        {
            Plays.AcceptOffer(actionName);
            Run(Plays.StepsOf(action));
        }
        FinishInput(before);
    }

    /// The one way in for people, bots and the network. An input is plain data, so it can be sent, recorded and
    /// replayed (see Input). With a player, it's refused unless that player may act now.
    public bool Apply(Input input)
    {
        if (!IsWellFormed(input))
        {
            ReportFailure("Ignored a malformed input: " + JsonSerializer.Serialize(input));
            return false;
        }
        int player = input.Player ?? LocalSeat;
        if (player != 0 && !MayAct(player, input))
        {
            LogLine($"[refused] {PlayerName(player)} can't do that now: {input.Type}");
            GameLog.Flush();
            return false;
        }
        if (!RunInput(input))
        {
            return false;
        }
        Inputs.Add(input);
        InputApplied?.Invoke(input);
        return true;
    }

    /// Applies each input of a move (e.g. press an action, then click the card to choose), as that player.
    public void ApplyMove(List<Input> move, int player = 0)
    {
        foreach (Input input in move)
        {
            Apply(player != 0 ? input with { Player = player } : input);
        }
    }

    /// Who may make an input: the player whose turn (or choice) it is. Human-only buttons are for any person,
    /// anyone may rearrange their own cards, and undo is for the table to agree on.
    public bool MayAct(int player, Input input)
    {
        switch (input.Type)
        {
            case "undo":
                return true;
            case "reorder":
                return ZoneOf(Cards[input.Card!.Value]).Owner is 0 || ZoneOf(Cards[input.Card!.Value]).Owner == player;
            case "perform" when Rules.Actions[input.Action!].GetValueOrDefault("human_only") is true:
                return !IsBot(player);
        }
        return Sandbox || player == CurrentPlayer();
    }

    bool IsWellFormed(Input input)
    {
        if ((input.Card ?? 0) < 0 || (input.Card ?? 0) >= Cards.Count)
        {
            return false;
        }
        return input.Type switch
        {
            "drag" => input.Card != null && input.To != null && Zones.ContainsKey(input.To),
            "click" or "double_click" => input.Card != null,
            "reorder" => input.Card != null && input.Index != null,
            "click_empty" => input.Zone != null && Zones.ContainsKey(input.Zone),
            "perform" => input.Action != null && Rules.Actions.ContainsKey(input.Action),
            "choose" => input.Option != null,
            "finish" or "undo" => true,
            _ => false,
        };
    }

    /// Carries out an input without checking who sent it or recording it (Apply does both; bots trying moves out don't).
    public bool RunInput(Input input)
    {
        Card? card = input.Card is int id ? Cards[id] : null;
        switch (input.Type)
        {
            case "drag":
                return Drag(card!, Zones[input.To!], input.Index ?? -1);
            case "click":
                Click(card!);
                break;
            case "double_click":
                DoubleClick(card!);
                break;
            case "click_empty":
                ClickEmpty(Zones[input.Zone!]);
                break;
            case "perform":
                Perform(input.Action!);
                break;
            case "choose":
                // Sent numbers may arrive as decimals (4.0), so options are matched by value; zones are sent by name.
                List<object?> matches = Choice.Options.Where(option => IsOptionSent(option, input.Option)).ToList();
                if (matches.Count == 0)
                {
                    return false;
                }
                ChooseOption(matches[0]);
                break;
            case "finish":
                FinishChoice();
                break;
            case "reorder":
                Reorder(card!, input.Index!.Value);
                break;
            case "undo":
                Undo();
                break;
        }
        return true;
    }

    static bool IsOptionSent(object? option, object? sent)
    {
        if (option is Zone zone)
        {
            return Equals(zone.Name, sent);
        }
        if (Values.IsNumber(option) && Values.IsNumber(sent))
        {
            return Values.ToDouble(option) == Values.ToDouble(sent);
        }
        return option?.GetType() == sent?.GetType() && Values.DeepEquals(option, sent);
    }

    /// A match is its rules, seed and inputs; replaying them rebuilds it exactly.
    public MatchRecord Record() => new(RulesPath, MatchSeed, [.. Inputs]);

    public static Game Replay(GameFiles files, MatchRecord record)
    {
        Game game = new(files, record.Rules, record.Seed);
        game.Start();
        foreach (Input input in record.Inputs)
        {
            game.Apply(input);
        }
        return game;
    }

    public void ChooseOption(object? option)
    {
        if (Choice.CardsOffered || !Choice.Options.Any(each => Values.DeepEquals(each, option)))
        {
            return;
        }
        Snapshot? before = UndoPoint();
        Choice.Complete(option);
        FinishInput(before);
    }

    /// The game is over: no more turns, and offers give null straight away.
    public void EndGame(List<int> winners)
    {
        Over = true;
        Plays.Offered = [];
        Winners = winners;
        LogLine("[game over] " + ResultText());
    }

    /// "Winner: You", "Winners: You and North", or "Game over" when end_game() named nobody.
    public string ResultText()
    {
        if (Winners.Count == 0)
        {
            return "Game over";
        }
        return (Winners.Count == 1 ? "Winner: " : "Winners: ") + string.Join(" and ", Winners.Select(SeatName));
    }

    public void KnockOut(List<int> playersOut)
    {
        foreach (int player in playersOut.Where(player => !KnockedOut.Contains(player)))
        {
            KnockedOut.Add(player);
            LogLine($"  {PlayerName(player)} is out");
        }
        ClearPlayerCaches();
    }

    public void BringBack(List<int> playersBack)
    {
        KnockedOut.RemoveAll(playersBack.Contains);
        ClearPlayerCaches();
    }

    public List<int> PlayersLeft() => Enumerable.Range(1, Rules.Players).Where(player => !KnockedOut.Contains(player)).ToList();

    /// The player that many seats on from this one (back, if negative), skipping players knocked out.
    public int SeatAfter(int player, int seats)
    {
        List<int> left = PlayersLeft();
        if (left.Count == 0)
        {
            return player;
        }
        int step = seats >= 0 ? 1 : -1;
        int seat = player;
        for (int i = 0; i < Math.Abs(seats); i++)
        {
            seat = PositiveModulo(seat - 1 + step, Rules.Players) + 1;
            while (!left.Contains(seat))
            {
                seat = PositiveModulo(seat - 1 + step, Rules.Players) + 1;
            }
        }
        return seat;
    }

    static int PositiveModulo(int value, int divisor) => (value % divisor + divisor) % divisor;

    /// Each player's value of a player variable or condition, worked out as if it were their turn: for most(...),
    /// players_with_most(...) and players_where(...).
    public List<object?> PlayerValues(string name)
    {
        bool hadTurn = Variables.TryGetValue(PlayerVariable, out object? turn);
        List<object?> values = [];
        for (int player = 1; player <= Rules.Players; player++)
        {
            Variables[PlayerVariable] = (long)player;
            values.Add(Evaluate(name));
        }
        if (hadTurn)
        {
            Variables[PlayerVariable] = turn;
        }
        else
        {
            Variables.Remove(PlayerVariable);
        }
        return values;
    }

    /// Whether turns should go on: the "turn" steps start again once nothing else is running, asked or being chosen.
    public bool NextTurnDue() => Rules.Turn.Count > 0 && !Over && Choice.Asked.Count == 0 && !Choice.IsOpen;

    public void FinishChoice()
    {
        if (!Choice.CardsOffered || Choice.Exactly)
        {
            return;
        }
        Snapshot? before = UndoPoint();
        Choice.Complete(Choice.Selected.Cast<object?>().ToList());
        FinishInput(before);
    }

    /// Like Values.Format, but a card the viewer can't see is "a hidden card", so the log doesn't give away hands.
    public string Describe(object? value) => value switch
    {
        Card card => ShowsFace(card) ? card.Title : HiddenCard,
        List<object?> items => string.Join(", ", items.Select(Describe)),
        _ => Values.Format(value),
    };

    void FinishInput(Snapshot? before)
    {
        GameLog.Flush();
        Choice.PerformAsked();
        Remember(before);
        AnnounceStateChanged();
    }

    /// A card dragged onto a zone: done by the first drag action that allows it (see Plays.DragAction). With no drag
    /// actions in the game at all, any drag is allowed.
    public bool Drag(Card card, Zone destination, int index = -1)
    {
        if (Choice.MustBeMade && !Sandbox)
        {
            LogLine($"[waiting] {PlayerName(CurrentPlayer())} must finish \"{Choice.Title}\" first");
            GameLog.Flush();
            return false;
        }
        Choice.Cancel();
        Zone source = ZoneOf(card);
        string actionName = Sandbox ? "" : Plays.DragAction(card, source, destination);
        if (actionName.Length == 0 && !Plays.CanMove(card, destination))
        {
            LogLine($"[not allowed] {card.Title}: {source.Name} → {destination.Name} ({Plays.RefusalReason(source, destination)})");
            GameLog.Flush();
            return false;
        }
        Snapshot? before = UndoPoint();
        DragBy(actionName, card, destination, index);
        FinishInput(before);
        return true;
    }

    /// Moves the card (and any carried with it), then runs the action's steps; card effects run first, as each card
    /// arrives. They're all held back until the cards have moved, so an effect that waits for a choice holds up the rest.
    void DragBy(string actionName, Card card, Zone destination, int index = -1)
    {
        if (actionName.Length > 0)
        {
            LogLine("[action] " + actionName);
            Plays.AcceptOffer(actionName);
        }
        Runner.Hold();
        WithMove(ZoneOf(card), destination, () =>
        {
            foreach (Card carriedCard in Carried(card))
            {
                Move(carriedCard, destination);
            }
            if (index >= 0 && destination.SortColumns.Count == 0 && destination.Cards.Contains(card))
            {
                destination.Reorder(card, Math.Min(index, destination.Cards.Count - 1));
            }
            if (actionName.Length > 0)
            {
                Run(Plays.StepsOf(Rules.Actions[actionName]), card);
            }
            return true;
        });
        Runner.Release();
    }

    public List<Card> Movable(List<Card> cards, List<Zone> destinations)
    {
        if (checkingMoves)
        {
            checkedItself = true;
            return [];
        }
        checkingMoves = true;
        List<Card> result = cards.Where(card => destinations.Any(destination => destination != ZoneOf(card) && Plays.CanMove(card, destination))).ToList();
        checkingMoves = false;
        if (checkedItself)
        {
            checkedItself = false;
            ReportFailure("movable() was used inside a move rule that movable() was checking; that inner check counts as no moves");
        }
        return result;
    }

    /// The card plus, in zones that drag stacks, every card on top of it.
    public List<Card> Carried(Card card)
    {
        Zone zone = ZoneOf(card);
        return zone.DragsStack ? zone.Cards[zone.Cards.IndexOf(card)..] : [card];
    }

    public T WithMove<T>(Zone? source, Zone? destination, Func<T> work)
    {
        (Zone? outerFrom, Zone? outerTo) = (moveFrom, moveTo);
        moveFrom = source;
        moveTo = destination;
        T result = work();
        moveFrom = outerFrom;
        moveTo = outerTo;
        return result;
    }

    public void Reorder(Card card, int index)
    {
        Snapshot? before = UndoPoint();
        Zone zone = ZoneOf(card);
        zone.Reorder(card, index);
        Remember(before);
        LogLine($"  {card.Title}: position {index + 1} in {zone.Name} (manual order)");
        GameLog.Flush();
        AnnounceStateChanged();
    }

    /// A trigger called from a step runs with that step's card, so it can say "if it's a King, remove it".
    public void Trigger(string @event, Card? card = null)
    {
        if (Rules.Triggers.TryGetValue(@event, out object? steps))
        {
            LogLine("[trigger] " + @event);
            Run((List<object?>)steps!, card);
        }
    }

    /// Fires "<zone>_empty"; for a per-player zone with no trigger of its own, fires "<kind>_empty" as its owner,
    /// so "deck_empty" can say move(discard, deck) for whoever ran out. It runs at once, in the middle of the draw
    /// that found the zone empty.
    public void ZoneEmpty(Zone zone)
    {
        string ownTrigger = zone.Name + "_empty";
        bool asOwner = zone.Owner != 0 && !Rules.Triggers.ContainsKey(ownTrigger);
        string @event = asOwner ? zone.Kind + "_empty" : ownTrigger;
        if (!Rules.Triggers.TryGetValue(@event, out object? steps))
        {
            return;
        }
        LogLine("[trigger] " + @event);
        object? turn = Variables.GetValueOrDefault(PlayerVariable);
        if (asOwner)
        {
            Variables[PlayerVariable] = (long)zone.Owner;
        }
        Runner.RunNow((List<object?>)steps!, null);
        if (asOwner)
        {
            Variables[PlayerVariable] = turn;
        }
    }

    /// Whether any of the copies listed in the zone's "covered_by" still holds a card (Pyramid, TriPeaks).
    public bool Covered(Zone zone) =>
        zone.CoveredBy.Any(number => Zones.TryGetValue($"{zone.Kind}_{(int)Values.ToDouble(number)}", out Zone? covering) && !covering.IsEmpty);

    public List<Card> Matching(List<Card> cards, string condition)
    {
        if (!Rules.Conditions.TryGetValue(condition, out object? expression) || Rules.ConditionParameters[condition].Count > 0)
        {
            ReportFailure($"No condition named \"{condition}\" (without parameters) in \"conditions\"");
            return [];
        }
        return cards.Where(card => Holds((string)expression!, card)).ToList();
    }

    public int CurrentPlayer() => (int)Values.ToDouble(Variables.GetValueOrDefault(PlayerVariable, 1L));

    public int NextPlayer() => SeatAfter(CurrentPlayer(), 1);

    public int TeamOf(int player)
    {
        for (int i = 0; i < Rules.Teams.Count; i++)
        {
            if (Rules.Teams[i] is List<object?> members && members.Any(member => Values.IsNumber(member) && Values.ToDouble(member) == player))
            {
                return i + 1;
            }
        }
        return 0;
    }

    public void EndTurn()
    {
        SetVariable(PlayerVariable, (long)NextPlayer());
        Trigger("turn_start");
    }

    public void Click(Card card)
    {
        Snapshot? before = UndoPoint();
        Zone zone = ZoneOf(card);
        if (Choice.CardsOffered && Choice.Options.Contains(card))
        {
            Choice.Select(card);
        }
        else if (Choice.Options.Contains(zone))
        {
            Choice.Complete(zone);
        }
        else if (Choice.IsOpen)
        {
            Choice.Cancel();
        }
        else
        {
            ClickBy(Plays.ClickActionFor(zone, card, Plays.ClickKey), zone, card);
        }
        FinishInput(before);
    }

    public void DoubleClick(Card card)
    {
        Snapshot? before = UndoPoint();
        Choice.Cancel();
        Zone zone = ZoneOf(card);
        if (!Choice.MustBeMade)
        {
            ClickBy(Plays.ClickActionFor(zone, card, Plays.DoubleClickKey), zone, card);
        }
        FinishInput(before);
    }

    public void ClickEmpty(Zone zone)
    {
        Snapshot? before = UndoPoint();
        if (Choice.Options.Contains(zone))
        {
            Choice.Complete(zone);
        }
        else if (!Choice.MustBeMade)
        {
            Choice.Cancel();
            ClickBy(Plays.ClickActionFor(zone, null, Plays.ClickKey), zone, null);
        }
        FinishInput(before);
    }

    /// A click found an action (see Plays.ClickActionFor): a drag action drags the card to its destination; any
    /// other action runs its steps, with card and clicked_zone.
    void ClickBy(ClickAction? found, Zone zone, Card? card)
    {
        if (found == null)
        {
            return;
        }
        if (found.Destination != null)
        {
            DragBy(found.Action, card!, found.Destination);
            return;
        }
        LogLine("[action] " + found.Action);
        Plays.AcceptOffer(found.Action);
        WithClickedZone(zone, () =>
        {
            Run(Plays.StepsOf(Rules.Actions[found.Action]), card);
            return true;
        });
    }

    public void Undo()
    {
        if (History.Count == 0)
        {
            return;
        }
        RestoreLast();
        while (IsBotTurn() && History.Count > 0)
        {
            RestoreLast();
        }
        LogLine("[undo]");
        GameLog.Flush();
        AnnounceVariablesChanged();
        AnnounceStateChanged();
    }

    void RestoreLast()
    {
        Snapshot last = History[^1];
        History.RemoveAt(History.Count - 1);
        last.Restore(this);
    }

    public bool IsBotTurn() => IsBot(CurrentPlayer());

    /// Whether the person at this screen may pick up the card: online, only from shared zones and their own.
    public bool MayHandle(Card card) => LocalSeat == 0 || Sandbox || ZoneOf(card).Owner == 0 || ZoneOf(card).Owner == LocalSeat;

    /// Whether the person at this screen acts now: online, only on their seat's turn; sharing a screen, on any person's.
    public bool IsLocalTurn() => LocalSeat != 0 ? CurrentPlayer() == LocalSeat : !IsBotTurn();

    public string PlayerName(int player) => SeatName(player) + (IsBot(player) ? " (bot)" : "");

    /// The name in "player_names" (online, the name each person joined with).
    public string SeatName(int player) =>
        player >= 1 && player <= Rules.PlayerNames.Count ? Values.Text(Rules.PlayerNames[player - 1]) : $"Player {player}";

    public bool IsBot(int player) =>
        (Rules.Bot.GetValueOrDefault("players") as List<object?> ?? []).Any(botPlayer => (int)Values.ToDouble(botPlayer) == player);

    public void SetQuiet(bool quiet)
    {
        Quiet = quiet;
        GameLog.Quiet = quiet;
        foreach (Zone zone in Zones.Values)
        {
            zone.Quiet = quiet;
        }
    }

    /// The state before an input, to go back to with undo; nothing while a bot tries inputs out (see Remembering).
    Snapshot? UndoPoint() => Remembering ? Snapshot.Take(this) : null;

    void Remember(Snapshot? before)
    {
        if (Remembering && !Runner.Running && before != null && !before.SameAs(Snapshot.Take(this)))
        {
            History.Add(before);
        }
    }

    /// Steps started from outside (an input) run at once, up to any choose(...) that waits; steps started by a step
    /// (a trigger, a card's effect) run right after it. See StepRunner.
    public void Run(List<object?> steps, Card? card = null)
    {
        Runner.Schedule(steps, card);
        Runner.Drive();
    }

    /// What "from", "to", "clicked_zone" and "choice" mean for the steps being started, so they keep their meaning
    /// if those steps wait for a choice.
    public MoveContext MoveContext() => new(moveFrom, moveTo, clickedZone, Chosen);

    public void SetMoveContext(MoveContext context)
    {
        moveFrom = context.From;
        moveTo = context.To;
        clickedZone = context.ClickedZone;
        Chosen = context.Choice;
    }

    public bool Holds(string condition, Card? card = null)
    {
        if (condition.Length == 0)
        {
            return true;
        }
        object? result = Evaluate(condition, card);
        return !EvaluationFailed && Values.IsTrue(result);
    }

    /// What per-player names mean changes when players are knocked out (opponent_ skips them), so the answers worked
    /// out from them are forgotten.
    public void ClearPlayerCaches()
    {
        aliasesByPlayer.Clear();
        Plays.ClearRoutes();
    }

    /// The action done with this card from a "button": true press: dragged to its first allowed zone, or clicked.
    public void PlayCardAction(string actionName, Card card)
    {
        if (Plays.DestinationFor(actionName, card) is { } destination)
        {
            DragBy(actionName, card, destination);
        }
        else
        {
            ClickBy(new(actionName), ZoneOf(card), card);
        }
    }

    /// Works something out with "choice" set to what was chosen, as an action's "allow" and steps see it.
    public T WithChoice<T>(object? made, Func<T> work)
    {
        object? outerChoice = Chosen;
        Chosen = made;
        T result = work();
        Chosen = outerChoice;
        return result;
    }

    public void WithChoice(object? made, Action work)
    {
        WithChoice(made, () =>
        {
            work();
            return true;
        });
    }

    /// Works something out with clicked_zone set, as an action's "when" sees it.
    public T WithClickedZone<T>(Zone zone, Func<T> work)
    {
        Zone? outerZone = clickedZone;
        clickedZone = zone;
        T result = work();
        clickedZone = outerZone;
        return result;
    }

    public Dictionary<string, string> StatusValues() =>
        Rules.Status.ToDictionary(pair => pair.Key, pair => Values.Format(Evaluate((string)pair.Value!)));

    public object? Evaluate(string text, Card? card = null)
    {
        StepEvaluator evaluator = new(this, card);
        object? result = evaluator.Evaluate(TreeOf(text));
        EvaluationFailed = evaluator.Failure.Length > 0;
        if (EvaluationFailed)
        {
            ReportFailure($"Step failed: \"{text}\": {evaluator.Failure}");
        }
        return result;
    }

    /// Steps are parsed when the game loads (StepCompiler); one that wasn't (it had a load error) is parsed now.
    public StepNode TreeOf(string text)
    {
        if (!StepTrees.TryGetValue(text, out StepNode? tree))
        {
            StepTrees[text] = tree = StepParser.Parse(text);
        }
        return tree;
    }

    public void Move(Card card, Zone destination)
    {
        Zone source = ZoneOf(card);
        bool seenBefore = ShowsFace(card);
        source.Remove(card);
        destination.Add(card);
        GameLog.Item($"{source.Name} → {destination.Name}", seenBefore || ShowsFace(card) ? card.Title : "");
        Run(CardEffect(card, destination.Kind), card);
    }

    public void Mark(Card card, string key, object? value)
    {
        card.Marks[key] = value;
        GameLog.Item($"mark {key} = {Values.Format(value)}", Describe(card));
    }

    public void Unmark(Card card, string key)
    {
        if (card.Marks.Remove(key))
        {
            GameLog.Item("unmark " + key, Describe(card));
        }
    }

    public void Flip(Card card)
    {
        card.FaceUp = !card.FaceUp;
        Zone zone = ZoneOf(card);
        bool seen = ShowsFace(card) || !card.FaceUp && zone.Face != Zone.FaceDown;
        GameLog.Item($"face {(card.FaceUp ? "up" : "down")} in {zone.Name}", seen ? card.Title : "");
        zone.AnnounceChanged();
    }

    public void LogLine(string text)
    {
        GameLog.Line(text);
    }

    /// A step that fails while the game loads (e.g. working out "columns") is a load error, shown before play starts.
    public void ReportFailure(string text)
    {
        GameLog.Flush();
        if (!Started)
        {
            if (!Errors.Contains(text))
            {
                Errors.Add(text);
            }
            return;
        }
        if (!Quiet)
        {
            Failed?.Invoke(text);
        }
    }

    /// Whether this screen shows the card's face.
    public bool ShowsFace(Card card) => CanSee(Viewer(), card);

    /// Whether that player can see the card's face: not if it's face down, nor in another player's private zone
    /// ("private"), nor in their own zone held facing away from them ("others").
    public bool CanSee(int player, Card card)
    {
        if (!card.FaceUp)
        {
            return false;
        }
        Zone zone = ZoneOf(card);
        return zone.Face switch
        {
            Zone.FacePrivate => zone.Owner == player,
            Zone.FaceOthers => zone.Owner != player,
            _ => true,
        };
    }

    /// Who the table is shown to: this screen's seat when playing online; otherwise the only human at a table
    /// of bots, or whoever's turn it is (sharing one screen).
    public int Viewer()
    {
        if (LocalSeat != 0)
        {
            return LocalSeat;
        }
        List<int> humans = Enumerable.Range(1, Rules.Players).Where(player => !IsBot(player)).ToList();
        return humans.Count == 1 ? humans[0] : CurrentPlayer();
    }

    /// Whether name[n] means player n's (or team n's) zone or variable: hand[2], score[dealer].
    public bool IsPerPlayer(string name) => PerPlayerNames.Contains(name) || PerTeamNames.Contains(name);

    public bool IsPerTeam(string name) => PerTeamNames.Contains(name);

    /// Player (or team) number's own zone or variable: the "<name>_<number>" one, or null if there's no such player.
    public string? NumberedName(string name, int number)
    {
        int limit = PerTeamNames.Contains(name) ? Rules.Teams.Count : Rules.Players;
        return number < 1 || number > limit ? null : $"{name}_{number}";
    }

    /// A step's "name = value": a game variable, or else a name the running steps remember.
    public void Assign(string name, object? value)
    {
        if (Variables.ContainsKey(Resolve(name)))
        {
            SetVariable(name, value);
        }
        else
        {
            Runner.SetLocal(name, value);
        }
    }

    public void SetVariable(string variable, object? value)
    {
        string name = Resolve(variable);
        if (!Variables.ContainsKey(name))
        {
            ReportFailure($"Unknown variable \"{name}\" (declare it in \"variables\")");
            return;
        }
        Variables[name] = value;
        LogLine($"  {name} = {(value is List<object?> ? $"[{Describe(value)}]" : Describe(value))}");
        AnnounceVariablesChanged();
    }

    public Zone ZoneOf(Card card) => Zones[card.ZoneName];

    /// "hand" -> "hand_2" and so on for the current player. Only the current player changes them, so they're
    /// worked out once per player rather than for every step.
    public Dictionary<string, string> Aliases()
    {
        int player = CurrentPlayer();
        if (aliasesByPlayer.TryGetValue(player, out Dictionary<string, string>? known))
        {
            return known;
        }
        Dictionary<string, string> aliases = [];
        foreach (string name in PerPlayerNames)
        {
            aliases[name] = $"{name}_{player}";
            aliases[OpponentPrefix + name] = $"{name}_{NextPlayer()}";
        }
        foreach (string name in PerTeamNames)
        {
            aliases[name] = $"{name}_{TeamOf(player)}";
            aliases[OpponentPrefix + name] = $"{name}_{TeamOf(NextPlayer())}";
        }
        aliasesByPlayer[player] = aliases;
        return aliases;
    }

    /// "team_hand" is the hands of everyone on the current player's team.
    Dictionary<string, List<Zone>> TeamGroups()
    {
        if (Rules.Teams.Count == 0)
        {
            return [];
        }
        int team = TeamOf(CurrentPlayer());
        if (teamGroupsByTeam.TryGetValue(team, out Dictionary<string, List<Zone>>? known))
        {
            return known;
        }
        Dictionary<string, List<Zone>> groups = [];
        teamGroupsByTeam[team] = groups;
        foreach ((string groupName, List<Zone> zones) in ZoneGroups)
        {
            if (groupName.StartsWith(AllPrefix))
            {
                groups[TeamPrefix + groupName[AllPrefix.Length..]] = zones.Where(zone => TeamOf(zone.Owner) == team).ToList();
            }
        }
        return groups;
    }

    public string Resolve(string name) => Aliases().GetValueOrDefault(name, name);

    public List<string> InputNames() =>
    [
        .. Zones.Keys, .. Variables.Keys, .. Aliases().Keys, .. ZoneGroups.Keys, .. TeamGroups().Keys, .. LocalNames, .. Rules.Constants.Keys,
        CardVariable, FromVariable, ToVariable, ClickedZoneVariable, ChoiceVariable, PlayersVariable,
    ];

    /// What a name in a step stands for right now (except card, which belongs to the step): a zone or variable (hand
    /// and score are the current player's), a name the running steps remember (a loop's item, pile = ...), a group of
    /// zones, or what's being moved, clicked or chosen.
    public object? ValueOf(string name)
    {
        string resolved = Resolve(name);
        if (Zones.TryGetValue(resolved, out Zone? zone))
        {
            return zone;
        }
        if (Variables.TryGetValue(resolved, out object? value))
        {
            return value;
        }
        if (Rules.Constants.TryGetValue(name, out object? constant))
        {
            return constant;
        }
        if (LocalNames.Contains(name))
        {
            return Runner.HasLocal(name) ? Runner.Local(name) : null;
        }
        switch (name)
        {
            case FromVariable:
                return moveFrom;
            case ToVariable:
                return moveTo;
            case ClickedZoneVariable:
                return clickedZone;
            case ChoiceVariable:
                return Chosen;
            case PlayersVariable:
                return Enumerable.Range(1, Rules.Players).Select(player => (object?)(long)player).ToList();
        }
        if (ZoneGroups.TryGetValue(name, out List<Zone>? group))
        {
            return group.Cast<object?>().ToList();
        }
        return TeamGroups().TryGetValue(name, out List<Zone>? teamGroup) ? teamGroup.Cast<object?>().ToList() : null;
    }

    void AddZone(Zone zone, Dictionary<string, object?> definition)
    {
        if (definition.GetValueOrDefault("cards") is string path)
        {
            LoadCards(zone, path);
        }
        Zones[zone.Name] = zone;
    }

    void AddNumberedVariables(Dictionary<string, object?> starts, int count, List<string> names)
    {
        foreach ((string name, object? start) in starts)
        {
            names.Add(name);
            for (int number = 1; number <= count; number++)
            {
                string variable = $"{name}_{number}";
                if (Variables.ContainsKey(variable))
                {
                    Errors.Add($"\"{variable}\" is used as more than one zone or variable name");
                }
                Variables[variable] = Values.DeepCopy(start);
            }
        }
    }

    void LoadCards(Zone zone, string path)
    {
        if (!Files.Exists(path))
        {
            Errors.Add("Deck not found: " + path);
            return;
        }
        List<Card> deck = DeckLoader.Load(Files.Read(path));
        foreach (Card card in deck)
        {
            card.Id = Cards.Count;
            Cards.Add(card);
        }
        zone.AddAll(deck);
    }

    /// "columns" next to a zone's "cards": columns added or changed for every card as the game loads, each worked
    /// out from the card (in order, so a later column can use an earlier one), e.g. "points": "min(card.value, 10)".
    void ComputeColumns()
    {
        foreach (Zone zone in Zones.Values)
        {
            Dictionary<string, object?> definition = (Dictionary<string, object?>)Rules.Zones[zone.Kind]!;
            foreach ((string column, object? formula) in definition.GetValueOrDefault("columns") as Dictionary<string, object?> ?? [])
            {
                foreach (Card card in zone.Cards)
                {
                    card.Data[column] = Values.Format(Evaluate((string)formula!, card));
                }
                if (zone.Cards.Count > 0 && zone.Cards.All(card => Card.IsNumber(card.Data[column])))
                {
                    zone.Cards[0].NumberColumns.Add(column);
                }
                else if (zone.Cards.Count > 0)
                {
                    zone.Cards[0].NumberColumns.Remove(column);
                }
            }
        }
    }

    public void RunCardEffect(Card card, string zoneKind)
    {
        Run(CardEffect(card, zoneKind), card);
    }

    /// A card's own steps for arriving in a kind of zone, from its on_<kind> column: steps and trigger names
    /// separated by ";", or a JSON list of steps.
    public List<object?> CardEffect(Card card, string zoneKind)
    {
        string steps = card.Data.GetValueOrDefault(CardEffectPrefix + zoneKind, "").Trim();
        if (steps.StartsWith('['))
        {
            try
            {
                return GameRules.Parse(steps) as List<object?> ?? [steps];
            }
            catch (JsonException)
            {
                return [steps];
            }
        }
        List<object?> stepList = [];
        foreach (string part in steps.Split(CardEffectSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string step = part.Trim();
            if (Rules.Triggers.GetValueOrDefault(step) is List<object?> trigger)
            {
                stepList.AddRange(trigger);
            }
            else
            {
                stepList.Add(step);
            }
        }
        return stepList;
    }

    public void AnnounceStateChanged()
    {
        if (!Quiet)
        {
            StateChanged?.Invoke();
        }
    }

    void AnnounceVariablesChanged()
    {
        if (!Quiet)
        {
            VariablesChanged?.Invoke();
        }
    }

    // What StepEvaluator needs (IStepContext).

    public BuildingBlocks Blocks => buildingBlocks;

    public bool Waiting => Runner.WaitRequest != null;

    public bool TryGetCondition(string name, out Condition condition)
    {
        if (conditionTrees.TryGetValue(name, out condition!))
        {
            return true;
        }
        if (!Rules.Conditions.TryGetValue(name, out object? expression) || expression is not string text)
        {
            return false;
        }
        conditionTrees[name] = condition = new(Rules.ConditionParameters[name], TreeOf(text));
        return true;
    }

    public bool IsUnsetLocal(string name) => LocalNames.Contains(name) && !Runner.HasLocal(name);

    public object CreateActions(Card? card) => new Actions(this, card);
}
