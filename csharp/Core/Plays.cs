namespace Core;

/// What clicking a card does (Plays.ClickAction): an action clicked on its zone, or a drag action that drags the
/// card to Destination.
public record ClickAction(string Action, Zone? Destination = null);

/// Where cards could be dragged from a zone, and the drag actions that go there.
record DragRoute(Zone Destination, List<string> Actions);

/// What players can do: the game's actions, each done by a drag, a click or its toolbar button; which action an
/// input is; which actions are offered right now (offer(...)); and every legal input, for bots. Game carries the
/// inputs out.
public class Plays
{
    /// Ways to do an action other than its toolbar button.
    public const string DragKey = "drag";
    public const string ClickKey = "click";
    public const string DoubleClickKey = "double_click";

    readonly Game game;
    /// The actions done by dragging (with "drag"), and by clicking or double-clicking (by name, or true on a drag
    /// action), in the file's order.
    readonly List<string> dragActionNames = [];
    readonly Dictionary<string, List<string>> clickActionNames = new() { [ClickKey] = [], [DoubleClickKey] = [] };
    /// Per current player, each zone's drag routes (see DragRoutes), which only change with the names' meanings.
    readonly Dictionary<int, Dictionary<string, List<DragRoute>>> dragRoutesByPlayer = [];

    /// The actions a waiting offer(...) lets the current player do.
    public List<string> Offered { get; set; } = [];
    /// Every action named in an offer anywhere (filled in by StepCompiler), which can only be done while offered.
    public HashSet<string> OfferedActions { get; } = [];

    public Plays(Game game, Dictionary<string, Dictionary<string, object?>> actions)
    {
        this.game = game;
        foreach ((string actionName, Dictionary<string, object?> action) in actions)
        {
            if (action.GetValueOrDefault(DragKey) is Dictionary<string, object?>)
            {
                dragActionNames.Add(actionName);
            }
            foreach ((string clickKey, List<string> names) in clickActionNames)
            {
                if (action.ContainsKey(clickKey))
                {
                    names.Add(actionName);
                }
            }
        }
    }

    public static bool IsCardAction(Dictionary<string, object?> action) =>
        action.ContainsKey(DragKey) || action.ContainsKey(ClickKey) || action.ContainsKey(DoubleClickKey);

    /// An action's steps (none if it has none).
    public static List<object?> StepsOf(Dictionary<string, object?> action) => action.GetValueOrDefault("steps") as List<object?> ?? [];

    /// An action's "when" ("" if it has none, so it's always allowed).
    public static string WhenOf(Dictionary<string, object?> action) => action.GetValueOrDefault("when") as string ?? "";

    /// Toolbar buttons: actions done by pressing them (not by a drag or click, unless they also say "button": true).
    public bool ShowsButton(string actionName)
    {
        Dictionary<string, object?> action = game.Rules.Actions[actionName];
        return action.GetValueOrDefault("button") as bool? ?? !IsCardAction(action);
    }

    /// Whether a toolbar button can be pressed now. A drag or click action with "button": true needs a card it
    /// could be done with, which pressing the button then asks for.
    public bool CanPerform(string actionName)
    {
        if (game.Sandbox)
        {
            return true;
        }
        if (game.Choice.MustBeMade || !IsOfferedNow(actionName))
        {
            return false;
        }
        if (IsCardAction(game.Rules.Actions[actionName]))
        {
            return PlayableCards(actionName).Count > 0;
        }
        return game.Holds(WhenOf(game.Rules.Actions[actionName]));
    }

    /// Actions named in an offer(...) can only be done while an offer naming them is waiting; the others whenever
    /// their "when" holds.
    public bool IsOfferedNow(string actionName) => !OfferedActions.Contains(actionName) || Offered.Contains(actionName);

    /// Whether the current player could do the action now if it were offered, by whichever means it has: what
    /// offer(...) checks before waiting.
    public bool CanDo(string actionName)
    {
        if (!game.Rules.Actions.TryGetValue(actionName, out Dictionary<string, object?>? action))
        {
            return false;
        }
        if (IsCardAction(action))
        {
            return PlayableCards(actionName).Count > 0 || ClickableEmptyZones(actionName).Count > 0;
        }
        return game.Holds(WhenOf(action));
    }

    /// An offer(...) step is waiting: the current player may do one of these actions (and anything not offered).
    public void OpenOffer(OfferRequest request)
    {
        Offered = request.Actions;
        game.LogLine($"  {game.PlayerName(game.CurrentPlayer())} may: {string.Join(", ", Offered)}");
    }

    /// The waiting offer gets its answer (the action being done), so the steps carry on once the action's steps run.
    public void AcceptOffer(string actionName)
    {
        if (Offered.Contains(actionName))
        {
            Offered = [];
            game.Runner.Accept(actionName);
        }
    }

    /// A game with no drag actions that never offers an action is a free-play sandbox, so any drag is allowed. One
    /// that offers actions is played by its rules: a drag no action allows would move cards outside them.
    public bool CanMove(Card card, Zone destination) =>
        game.Sandbox || dragActionNames.Count == 0 && OfferedActions.Count == 0 || DragAction(card, game.ZoneOf(card), destination).Length > 0;

    /// The first drag action (in the file's order) that allows this card from source to destination now, or "".
    public string DragAction(Card card, Zone source, Zone destination) =>
        FirstAllowed(DragActionsBetween(source, destination), card, source, destination);

    /// What clicking (or double-clicking) the card does: an action clicked on that zone, or a drag action that says
    /// "click": true, dragging the card to its first allowed zone. The first action in the file that allows it now
    /// wins; null if none.
    public ClickAction? ClickActionFor(Zone zone, Card? card, string clickKey)
    {
        foreach (string actionName in clickActionNames[clickKey])
        {
            Dictionary<string, object?> action = game.Rules.Actions[actionName];
            if (!IsOfferedNow(actionName) || !Binds(action, clickKey, zone))
            {
                continue;
            }
            if (action.ContainsKey(DragKey))
            {
                if (DestinationFor(actionName, card) is { } destination)
                {
                    return new(actionName, destination);
                }
            }
            else if (game.WithClickedZone(zone, () => game.Holds(WhenOf(action), card)))
            {
                return new(actionName);
            }
        }
        return null;
    }

    /// Whether double-clicking a card in the zone does something (otherwise two quick clicks are two clicks).
    public bool HasDoubleClick(Zone zone) =>
        clickActionNames[DoubleClickKey].Any(actionName => Binds(game.Rules.Actions[actionName], DoubleClickKey, zone));

    /// The first zone a drag action would take the card to now, or null.
    public Zone? DestinationFor(string actionName, Card? card)
    {
        if (card == null)
        {
            return null;
        }
        Zone source = game.ZoneOf(card);
        Dictionary<string, object?> action = game.Rules.Actions[actionName];
        Dictionary<string, object?> dragZones = (Dictionary<string, object?>)action[DragKey]!;
        if (!Names(dragZones.GetValueOrDefault("from"), source))
        {
            return null;
        }
        foreach (Zone destination in game.Zones.Values)
        {
            if (destination != source && Names(dragZones.GetValueOrDefault("to"), destination)
                && game.WithMove(source, destination, () => game.Holds(WhenOf(action), card)))
            {
                return destination;
            }
        }
        return null;
    }

    /// The cards the action could be done with now: dragged somewhere it allows, or clicked.
    public List<Card> PlayableCards(string actionName)
    {
        Dictionary<string, object?> action = game.Rules.Actions[actionName];
        List<Card> playable = [];
        foreach (Zone zone in game.Zones.Values)
        {
            foreach (Card card in ReachableCards(zone))
            {
                bool canClick = !action.ContainsKey(DragKey) && (Binds(action, ClickKey, zone) || Binds(action, DoubleClickKey, zone))
                    && game.WithClickedZone(zone, () => game.Holds(WhenOf(action), card));
                if (canClick || action.ContainsKey(DragKey) && DestinationFor(actionName, card) != null)
                {
                    playable.Add(card);
                }
            }
        }
        return playable;
    }

    /// Why a drag isn't allowed, for the log: each drag action between the zones and what it requires.
    public string RefusalReason(Zone source, Zone destination)
    {
        List<string> reasons = DragActionsBetween(source, destination).Select(actionName => IsOfferedNow(actionName)
            ? $"{actionName}: {game.Rules.Actions[actionName].GetValueOrDefault("when") as string ?? "always"}"
            : $"{actionName}: not offered now").ToList();
        if (reasons.Count == 0)
        {
            return $"no action drags from {source.Name} to {destination.Name}";
        }
        return "requires " + string.Join(" OR ", reasons);
    }

    /// Every move the current player could make, for bots. A move is a list of inputs (see Game.Apply), e.g.
    /// pressing an action and then clicking the cards to choose.
    public List<List<PlayerInput>> LegalMoves()
    {
        if (game.Choice.IsOpen)
        {
            return game.Choice.Moves();
        }
        List<List<PlayerInput>> moves = [];
        foreach (Zone source in game.Zones.Values)
        {
            List<DragRoute> routes = DragRoutes(source);
            foreach (Card card in ReachableCards(source))
            {
                foreach ((Zone destination, List<string> actionNames) in routes)
                {
                    if (FirstAllowed(actionNames, card, source, destination).Length > 0)
                    {
                        moves.Add([PlayerInput.Drag(card, destination)]);
                    }
                }
                if (ClickActionFor(source, card, ClickKey) != null)
                {
                    moves.Add([PlayerInput.Click(card)]);
                }
                if (ClickActionFor(source, card, DoubleClickKey) != null)
                {
                    moves.Add([PlayerInput.DoubleClick(card)]);
                }
            }
            if (source.IsEmpty && ClickActionFor(source, null, ClickKey) != null)
            {
                moves.Add([PlayerInput.ClickEmpty(source)]);
            }
        }
        foreach ((string actionName, Dictionary<string, object?> action) in game.Rules.Actions)
        {
            if (IsCardAction(action) || !ShowsButton(actionName) || action.GetValueOrDefault("human_only") is true || !CanPerform(actionName))
            {
                continue;
            }
            PlayerInput press = PlayerInput.Perform(actionName);
            if (!action.ContainsKey("choose"))
            {
                moves.Add([press]);
                continue;
            }
            foreach (List<PlayerInput> choiceMove in game.Choice.MovesForAction(actionName))
            {
                moves.Add([press, .. choiceMove]);
            }
        }
        return moves;
    }

    /// What a player could pick up: only the top card of a pile.
    public static List<Card> ReachableCards(Zone zone)
    {
        if (zone.Layout == "pile")
        {
            return zone.IsEmpty ? [] : [zone.Cards[^1]];
        }
        return zone.Cards;
    }

    /// Whether a drag's "from" or "to" (or a click's zones) names the zone: by name, as a per-player name like hand,
    /// as a group of copies, in a list, or (left out) any zone.
    public bool Names(object? ruleZone, Zone zone) => ruleZone switch
    {
        null => true,
        List<object?> list => list.Any(each => Names(each, zone)),
        string name => game.Resolve(name) == zone.Name || game.ZoneGroups.TryGetValue(name, out List<Zone>? group) && group.Contains(zone),
        _ => false,
    };

    public void ClearRoutes()
    {
        dragRoutesByPlayer.Clear();
    }

    string FirstAllowed(List<string> actionNames, Card card, Zone source, Zone destination) =>
        game.WithMove(source, destination, () =>
            actionNames.FirstOrDefault(actionName => IsOfferedNow(actionName) && game.Holds(WhenOf(game.Rules.Actions[actionName]), card)) ?? "");

    /// Where cards could be dragged from this zone: each destination some drag action names, with those actions
    /// (worked out once for all the zone's cards, for bots' legal moves).
    List<DragRoute> DragRoutes(Zone source)
    {
        if (!dragRoutesByPlayer.TryGetValue(game.CurrentPlayer(), out Dictionary<string, List<DragRoute>>? known))
        {
            dragRoutesByPlayer[game.CurrentPlayer()] = known = [];
        }
        if (known.TryGetValue(source.Name, out List<DragRoute>? found))
        {
            return found;
        }
        List<DragRoute> routes = [];
        foreach (Zone destination in game.Zones.Values)
        {
            if (destination != source)
            {
                List<string> actionNames = DragActionsBetween(source, destination);
                if (actionNames.Count > 0)
                {
                    routes.Add(new(destination, actionNames));
                }
            }
        }
        known[source.Name] = routes;
        return routes;
    }

    List<string> DragActionsBetween(Zone source, Zone destination) =>
        dragActionNames.Where(actionName =>
        {
            Dictionary<string, object?> dragZones = (Dictionary<string, object?>)game.Rules.Actions[actionName][DragKey]!;
            return Names(dragZones.GetValueOrDefault("from"), source) && Names(dragZones.GetValueOrDefault("to"), destination);
        }).ToList();

    /// Whether a click (or double-click) on the zone is one of the action's: its "click" names the zone, or it's a
    /// drag from the zone with "click": true.
    bool Binds(Dictionary<string, object?> action, string clickKey, Zone zone)
    {
        object? binding = action.GetValueOrDefault(clickKey);
        if (binding is bool on)
        {
            return on && action.ContainsKey(DragKey) && Names(((Dictionary<string, object?>)action[DragKey]!).GetValueOrDefault("from"), zone);
        }
        return binding != null && Names(binding, zone);
    }

    /// Empty zones whose click the action is, while its "when" holds with no card.
    List<Zone> ClickableEmptyZones(string actionName)
    {
        Dictionary<string, object?> action = game.Rules.Actions[actionName];
        return game.Zones.Values.Where(zone => zone.IsEmpty && !action.ContainsKey(DragKey) && Binds(action, ClickKey, zone)
            && game.WithClickedZone(zone, () => game.Holds(WhenOf(action), null))).ToList();
    }
}
