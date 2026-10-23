using System.Collections;

namespace Core;

/// The building blocks steps can call: each public method is one, named the way steps write it (GroupSizes is
/// group_sizes). Static methods are helpers, not building blocks. See docs/reference.md.
public class Actions(Game game, Card? stepCard = null)
{
    public void Draw(Zone from, Zone to, int amount = 1)
    {
        for (int i = 0; i < amount; i++)
        {
            if (from.IsEmpty)
            {
                game.ZoneEmpty(from);
            }
            if (from.IsEmpty)
            {
                return;
            }
            game.Move(from.Cards[^1], to);
        }
    }

    public void Deal(Zone from, object? to, int amount = 1)
    {
        List<Zone> hands = ZonesIn(to);
        for (int round = 0; round < amount; round++)
        {
            foreach (Zone hand in hands)
            {
                Draw(from, hand);
            }
        }
    }

    public void Move(object? cards, Zone to)
    {
        foreach (Card card in CardsIn(cards))
        {
            game.Move(card, to);
        }
    }

    /// Two cards trade places, each taking the other's zone and spot in it: Sorry!'s 11, or turning a two-way card
    /// round by trading it for its other side, kept in a box. A sorted zone stays sorted.
    public void Swap(Card? card, Card? other)
    {
        if (card == null || other == null || card == other)
        {
            return;
        }
        Zone cardZone = game.ZoneOf(card);
        Zone otherZone = game.ZoneOf(other);
        int cardIndex = cardZone.Cards.IndexOf(card);
        int otherIndex = otherZone.Cards.IndexOf(other);
        if (cardZone != otherZone)
        {
            game.Move(card, otherZone);
            game.Move(other, cardZone);
        }
        if (cardZone.SortColumns.Count == 0)
        {
            cardZone.Reorder(other, cardIndex);
        }
        if (otherZone.SortColumns.Count == 0)
        {
            otherZone.Reorder(card, otherIndex);
        }
    }

    /// Where a card lies in its zone, counting from 1 at the bottom (the left of a row), or 0 for no card.
    public int Position(Card? card) => card != null ? game.ZoneOf(card).Cards.IndexOf(card) + 1 : 0;

    public void TryMove(Card? card, object? to)
    {
        if (card == null)
        {
            return;
        }
        if (to is Zone zone)
        {
            game.Drag(card, zone);
            return;
        }
        List<Zone> destinations = ZonesIn(to);
        foreach (Zone destination in destinations)
        {
            if (destination != game.ZoneOf(card) && game.Plays.CanMove(card, destination))
            {
                game.Drag(card, destination);
                return;
            }
        }
        game.LogLine($"[not allowed] {game.Describe(card)}: no move rule allows any of {string.Join(", ", destinations.Select(destination => destination.Name))}");
    }

    public List<Card> All(Zone zone) => [.. zone.Cards];

    public void Mark(object? cards, string key, object? value)
    {
        foreach (Card card in CardsIn(cards))
        {
            game.Mark(card, key, value);
        }
    }

    public void Unmark(object? cards, string key)
    {
        foreach (Card card in CardsIn(cards))
        {
            game.Unmark(card, key);
        }
    }

    public object? MarkOf(Card? card, string key) => card?.Marks.GetValueOrDefault(key);

    public void Flip(object? cards)
    {
        foreach (Card card in CardsIn(cards))
        {
            game.Flip(card);
        }
    }

    public void Reveal(object? cards)
    {
        foreach (Card card in CardsIn(cards).Where(card => !card.Revealed))
        {
            game.Reveal(card);
        }
    }

    public void Shuffle(Zone zone)
    {
        zone.Shuffle(game.Random);
        game.LogLine("  shuffle " + zone.Name);
    }

    public void Sort(Zone zone, List<object?> columns)
    {
        List<string> columnNames = columns.Cast<string>().ToList();
        zone.KeepSortedBy(columnNames);
        game.LogLine(columnNames.Count > 0 ? $"  sort {zone.Name} by {string.Join(", ", columnNames)}" : $"  {zone.Name}: manual order");
    }

    public void EndTurn()
    {
        game.EndTurn();
    }

    public void Ask(string action, int player = 0)
    {
        game.Choice.Ask(action, player);
    }

    public void AskAll(string action, string thenTrigger = "")
    {
        game.Choice.AskAll(action, thenTrigger);
    }

    public void AskOthers(string action, string thenTrigger = "")
    {
        game.Choice.AskOthers(action, thenTrigger);
    }

    /// Waits for the current player to pick one option, and gives it: a card of a zone or list, a zone of a list of
    /// zones (a pile), or a value of a list (a color), e.g. pile = choose(discard_piles, 'Take from which pile?').
    /// With nothing to choose from, it gives null straight away.
    public object? Choose(object? options, string prompt = "") => WaitForChoice(options, 1, true, true, prompt);

    /// Waits for the current player to pick exactly that many cards, and gives them as a list, e.g.
    /// move(choose_exactly(hand, 3, 'Pass three cards'), played). With no more than that many, it gives them all.
    public object? ChooseExactly(object? cards, int amount, string prompt = "") => WaitForChoice(cards, amount, true, false, prompt);

    /// Waits for the current player to pick any number of cards up to most and press Done, and gives them as a list
    /// (maybe empty), e.g. trashed = choose_up_to(hand, 4, 'Trash up to 4 cards').
    public object? ChooseUpTo(object? cards, int most, string prompt = "") => WaitForChoice(cards, most, false, false, prompt);

    /// Waits until the current player does one of these actions (by its drag, click or button), and gives its name,
    /// e.g. offer(['Play a card', 'Draw']). While it waits, those actions can be done, as well as any action that's
    /// never offered. With none of them possible now, it gives null straight away.
    public object? Offer(object? actionNames) => WaitForOffer(NamesOf(actionNames));

    /// Offers the repeated actions and the ending ones together, again and again until the player does an ending
    /// one, and gives its name: offer_until('Play duo', 'End turn'), or several ways to end,
    /// offer_until('Play duo', ['End turn', 'Stop']). It stops early (null) if none of them is possible.
    public object? OfferUntil(object? repeated, object? ending)
    {
        List<string> enders = NamesOf(ending);
        List<string> names = [.. NamesOf(repeated), .. enders];
        while (true)
        {
            object? done = WaitForOffer(names);
            if (done == null || done is string name && enders.Contains(name))
            {
                return done;
            }
        }
    }

    /// The game is over: no more turns, offers give null straight away, and the winners (a player, a list of them
    /// for a tie, or none) are announced, e.g. end_game(players_with_most('score')).
    public void EndGame(object? winners = null)
    {
        game.EndGame(PlayersIn(winners));
    }

    /// Writes a line in the action log for everyone to read, e.g. log('%s scores %d' % [name_of(player), score]).
    public void Log(string text)
    {
        game.LogLine(text);
    }

    /// Players out of the game: turns, next_player() and opponent_ skip them. A number or a list.
    public void KnockOut(object? players)
    {
        game.KnockOut(PlayersIn(players));
    }

    /// Players back in the game after knock_out (e.g. everyone, for a new hand: bring_back(players)).
    public void BringBack(object? players)
    {
        game.BringBack(PlayersIn(players));
    }

    /// The players not knocked out, e.g. end_game(players_left()) when one is left.
    public List<int> PlayersLeft() => game.PlayersLeft();

    /// The highest of a player variable or condition across the players, each worked out as if it were their turn,
    /// e.g. most('score') >= 100.
    public object? Most(string name) => BoolsAsNumbers(game.PlayerValues(name)).MaxBy(Order);

    public object? Least(string name) => BoolsAsNumbers(game.PlayerValues(name)).MinBy(Order);

    /// The players with the highest value (more than one for a tie), e.g. end_game(players_with_most('score')).
    public List<int> PlayersWithMost(string name) => PlayersWith(name, Most(name));

    public List<int> PlayersWithLeast(string name) => PlayersWith(name, Least(name));

    /// The players for whom a player variable or condition is true, e.g. players_where('has_cards').
    public List<int> PlayersWhere(string name)
    {
        List<object?> values = game.PlayerValues(name);
        return Enumerable.Range(1, values.Count).Where(player => Values.IsTrue(values[player - 1])).ToList();
    }

    /// Variables back to their starting values from the game file, e.g. reset(['passes', 'taken', 'lead']). A player
    /// or team variable is reset for everyone.
    public void Reset(object? names)
    {
        foreach (string name in names is List<object?> list ? list.Cast<string>() : [(string)names!])
        {
            if (game.Rules.Variables.TryGetValue(name, out object? start))
            {
                game.SetVariable(name, Values.DeepCopy(start));
            }
            else if (game.Rules.PlayerVariables.TryGetValue(name, out object? playerStart) || game.Rules.TeamVariables.TryGetValue(name, out playerStart))
            {
                int count = game.Rules.PlayerVariables.ContainsKey(name) ? game.Rules.Players : game.Rules.Teams.Count;
                for (int number = 1; number <= count; number++)
                {
                    game.SetVariable($"{name}_{number}", Values.DeepCopy(playerStart));
                }
            }
            else
            {
                game.ReportFailure($"reset: no variable \"{name}\"");
            }
        }
    }

    /// The player that many seats after the current one (before, if negative), skipping players knocked out.
    public int NextPlayer(int seats = 1) => game.SeatAfter(game.CurrentPlayer(), seats);

    public string NameOf(int player) => game.SeatName(player);

    /// The seat this screen shows (the person playing here); for "status" only.
    public int Viewer() => game.Viewer();

    public Zone? Zone(string kind, int player)
    {
        string name = $"{kind}_{player}";
        if (!game.Zones.TryGetValue(name, out Zone? zone))
        {
            game.ReportFailure($"No zone \"{name}\"");
            return null;
        }
        return zone;
    }

    public List<int> Numbers(int low, int high) => [.. Enumerable.Range(low, Math.Max(high - low + 1, 0))];

    public int Random(int low, int high) => game.Random.NextInRange(low, high);

    public object? OneOf(object? items)
    {
        List<object?> list = items is List<object?> values && !values.Any(item => item is Card or Core.Zone) ? values : CardsIn(items).Cast<object?>().ToList();
        return list.Count > 0 ? list[game.Random.NextInRange(0, list.Count - 1)] : null;
    }

    public void Trigger(string @event)
    {
        if (game.Rules.Triggers.ContainsKey(@event))
        {
            game.Trigger(@event, stepCard);
        }
        else
        {
            game.ReportFailure($"No trigger named \"{@event}\" in \"triggers\"");
        }
    }

    public int Count(object? cards) => CardsIn(cards).Count;

    public Card? Top(object? cards) => CardsIn(cards).LastOrDefault();

    /// The total of a column over cards, or of a list of numbers: sum(hand, 'points'), sum([1, 2, 3]).
    public double Sum(object? items, string column = "")
    {
        if (column.Length == 0 && items is List<object?> numbers)
        {
            return numbers.Where(Values.IsNumber).Sum(Values.ToDouble);
        }
        return CardsIn(items).Sum(card => Number(card, column));
    }

    public double Number(Card? card, string column) => card != null ? Values.ToFloat(card.Column(column, "0")) : 0;

    public string Text(Card? card, string column) => card?.Column(column) ?? "";

    public Card? Highest(object? cards, string column)
    {
        Card? best = null;
        foreach (Card candidate in CardsIn(cards))
        {
            if (best == null || Number(candidate, column) > Number(best, column))
            {
                best = candidate;
            }
        }
        return best;
    }

    public Card? Lowest(object? cards, string column)
    {
        Card? best = null;
        foreach (Card candidate in CardsIn(cards))
        {
            if (best == null || Number(candidate, column) < Number(best, column))
            {
                best = candidate;
            }
        }
        return best;
    }

    /// Whether playing the card follows suit: nothing has been led yet, it's the suit led, or the hand has none of it.
    public bool FollowsSuit(Card? card, object? hand, string lead) =>
        lead == "" || Text(card, "suit") == lead || Where(hand, "suit", lead).Count == 0;

    /// The card taking a trick: the highest trump if any were played, otherwise the highest of the suit led
    /// (by the "suit" and "value" columns).
    public Card? TrickWinner(object? cards, string lead, string trump = "")
    {
        List<Card> trumps = trump.Length > 0 ? Where(cards, "suit", trump) : [];
        return Highest(trumps.Count > 0 ? trumps : Where(cards, "suit", lead), "value");
    }

    public int Owner(Card? card) => card != null ? game.ZoneOf(card).Owner : 0;

    public List<Card> Movable(object? cards, object? to) => game.Movable(CardsIn(cards), ZonesIn(to));

    public bool Covered(object? zoneOrCard)
    {
        Zone? zone = zoneOrCard is Card card ? game.ZoneOf(card) : zoneOrCard as Zone;
        return zone != null && game.Covered(zone);
    }

    public List<Card> Matching(object? cards, string condition) => game.Matching(CardsIn(cards), condition);

    /// The zones for which a condition with one parameter holds, e.g. zones_where(square, 'empty') with the condition
    /// "empty(space)": "count(space) == 0".
    public List<Zone> ZonesWhere(object? zones, string condition) =>
        game.ConditionFor(condition) is { } holds ? ZonesIn(zones).Where(zone => holds(zone)).ToList() : [];

    /// The spaces next to a board space: all of them, or on a square board only the 'orthogonal' or 'diagonal' ones.
    public List<Zone> Neighbors(Zone space, string directions = "")
    {
        if (BoardOf(space) is not { } board || !AreDirections(directions))
        {
            return [];
        }
        return board.Neighbors(space.Number, directions).Select(number => SpaceNumbered(space, number)).ToList();
    }

    /// The space at that column and row (counting from 1) of a board, or null off the board, e.g. space_at(square, 4, 4).
    public Zone? SpaceAt(object? board, int column, int row)
    {
        Zone? anySpace = ZonesIn(board).FirstOrDefault();
        if (anySpace == null || BoardOf(anySpace) is not { } found)
        {
            return null;
        }
        int number = found.Number(column, row);
        return number > 0 ? SpaceNumbered(anySpace, number) : null;
    }

    /// The spaces caught in straight lines from the space: in each direction, a run of spaces where between holds,
    /// ending at one where end holds, e.g. Reversi's flanked(clicked_zone, 'theirs', 'mine').
    public List<Zone> Flanked(Zone space, string between, string end)
    {
        if (BoardOf(space) is not { } board || HoldsAt(space, between) is not { } isBetween || HoldsAt(space, end) is not { } isEnd)
        {
            return [];
        }
        return board.Flanked(space.Number, isBetween, isEnd).Select(number => SpaceNumbered(space, number)).ToList();
    }

    /// Whether a path of neighbouring spaces, each where the condition holds, links a space of from to a space of to,
    /// e.g. Hex's connected(zones_where(cell, 'top_edge'), zones_where(cell, 'bottom_edge'), 'mine').
    public bool Connected(object? from, object? to, string condition, string directions = "")
    {
        List<Zone> starts = ZonesIn(from);
        if (starts.Count == 0 || BoardOf(starts[0]) is not { } board || !AreDirections(directions) || HoldsAt(starts[0], condition) is not { } holds)
        {
            return false;
        }
        IEnumerable<int> goals = ZonesIn(to).Where(goal => goal.Kind == starts[0].Kind).Select(goal => goal.Number);
        return board.Connected(starts.Select(start => start.Number), goals, holds, directions);
    }

    /// The most spaces in one straight line through the space (counting it) where the condition holds, e.g. Connect
    /// Four's in_a_row(clicked_zone, 'mine') >= 4.
    public int InARow(Zone space, string condition) =>
        BoardOf(space) is { } board && HoldsAt(space, condition) is { } holds ? board.InARow(space.Number, holds) : 0;

    public void RunEffect(Card? card, string zoneKind)
    {
        if (card != null)
        {
            game.RunCardEffect(card, zoneKind);
        }
    }

    public List<Card> Tops(object? zones) => ZonesIn(zones).Where(zone => !zone.IsEmpty).Select(zone => zone.Cards[^1]).ToList();

    public List<Card> FaceDown(object? cards) => CardsIn(cards).Where(card => !card.FaceUp).ToList();

    public List<Card> Without(object? cards, object? removed)
    {
        List<Card> removedCards = CardsIn(removed);
        return CardsIn(cards).Where(card => !removedCards.Contains(card)).ToList();
    }

    public List<Card> Where(object? cards, string column, object? value) =>
        CardsIn(cards).Where(card => card.Column(column) == Values.Format(value)).ToList();

    /// The last amount items of a zone (its top cards) or of a list.
    public List<object?> Last(object? items, int amount)
    {
        List<object?> list = ListOf(items);
        return list[Math.Max(list.Count - amount, 0)..];
    }

    /// The first amount items of a zone (its bottom cards) or of a list, e.g. first(group_sizes(hand, 'color'), 2).
    public List<object?> First(object? items, int amount) => ListOf(items).Take(Math.Max(amount, 0)).ToList();

    /// A list's item at that position (from 0), or its first or last item when the position is outside it: a table
    /// of points, e.g. lookup([0, 2, 4, 6, 8, 10], count(shells)).
    public object? Lookup(List<object?> list, int position) => list.Count == 0 ? null : list[Math.Clamp(position, 0, list.Count - 1)];

    /// How many cards share each value of a column, biggest first: [3, 2, 1] for three of one color, two of another
    /// and one of a third, e.g. first(group_sizes(hand, 'color'), 1) for the most of one color.
    public List<int> GroupSizes(object? cards, string column) => Groups(cards, column).Select(group => group.Count).ToList();

    public int Size(List<object?> list) => list.Count;

    public List<List<Card>> Groups(object? cards, string column)
    {
        List<List<Card>> byValue = [.. Core.Melds.GroupBy(CardsIn(cards), column).Values];
        GodotSort.Sort(byValue, (a, b) => a.Count > b.Count);
        return byValue;
    }

    public List<List<Card>> Pairs(object? cards, string column)
    {
        List<List<Card>> found = [];
        foreach (List<Card> group in Groups(cards, column))
        {
            for (int i = 0; i < group.Count; i++)
            {
                for (int j = i + 1; j < group.Count; j++)
                {
                    found.Add([group[i], group[j]]);
                }
            }
        }
        return found;
    }

    /// Every set of cards whose column adds up to total. Tries every subset, so keep it to small hands.
    public List<List<Card>> Sums(object? cards, string column, double total) =>
        Core.Melds.Subsets(CardsIn(cards)).Where(subset => subset.Count > 0 && Sum(subset, column) == total).ToList();

    /// Every longest run of consecutive values, at least length long. A repeated value gives one run per copy
    /// (3, 4, 4, 5 is two runs), so count(runs(...)) scores runs the cribbage way.
    public List<List<Card>> Runs(object? cards, string column, int length)
    {
        Dictionary<long, List<Card>> byValue = [];
        foreach (Card card in CardsIn(cards))
        {
            long value = (long)Number(card, column);
            if (!byValue.TryGetValue(value, out List<Card>? same))
            {
                byValue[value] = same = [];
            }
            same.Add(card);
        }
        List<List<Card>> found = [];
        List<long> stretch = [];
        foreach (long? value in byValue.Keys.Order().Select(value => (long?)value).Append(null))
        {
            if (stretch.Count > 0 && (value == null || value != stretch[^1] + 1))
            {
                if (stretch.Count >= length)
                {
                    found.AddRange(OneFromEach(stretch.Select(stretchValue => byValue[stretchValue]).ToList()));
                }
                stretch = [];
            }
            if (value != null)
            {
                stretch.Add(value.Value);
            }
        }
        return found;
    }

    public int Team(int player) => game.TeamOf(player);

    public List<object?> Melds(object? cards) => game.MeldRules.Best(CardsIn(cards));

    public List<Card> Deadwood(object? cards) => game.MeldRules.Deadwood(CardsIn(cards));

    public List<Card> Layoff(object? cards, List<object?> ontoMelds) => game.MeldRules.Layoff(CardsIn(cards), ontoMelds);

    /// Zones, from a zone or (nested) lists of them.
    public static List<Zone> ZonesIn(object? source) => source switch
    {
        Zone zone => [zone],
        IEnumerable items and not string => items.Cast<object?>().SelectMany(ZonesIn).ToList(),
        _ => [],
    };

    /// Cards, from a card, a zone (its cards), or (nested) lists of them.
    public static List<Card> CardsIn(object? source) => source switch
    {
        null => [],
        Zone zone => [.. zone.Cards],
        Card card => [card],
        IEnumerable items and not string and not IDictionary => items.Cast<object?>().SelectMany(CardsIn).ToList(),
        _ => [],
    };

    Board? BoardOf(Zone space)
    {
        if (space.Board == null)
        {
            game.ReportFailure($"{space.Name} isn't a space on a board");
        }
        return space.Board;
    }

    bool AreDirections(string directions)
    {
        if (!Board.DirectionKinds.Contains(directions))
        {
            game.ReportFailure($"directions should be 'orthogonal' or 'diagonal' (or left out for all), not '{directions}'");
            return false;
        }
        return true;
    }

    Zone SpaceNumbered(Zone anySpace, int number) => game.Zones[$"{anySpace.Kind}_{number}"];

    /// The condition as a check on the spaces of a board, by their numbers (null if there's no such condition).
    Func<int, bool>? HoldsAt(Zone anySpace, string condition) =>
        game.ConditionFor(condition) is { } holds ? number => holds(SpaceNumbered(anySpace, number)) : null;

    /// Cards stay cards (a zone gives its cards); any other list is kept as it is.
    static List<object?> ListOf(object? items)
    {
        if (items is List<object?> list && list.Any(item => item is not (Card or Core.Zone or List<object?>)))
        {
            return list;
        }
        return CardsIn(items).Cast<object?>().ToList();
    }

    List<int> PlayersWith(string name, object? value)
    {
        List<object?> values = game.PlayerValues(name);
        return Enumerable.Range(1, values.Count).Where(player => Values.AreEqual(values[player - 1], value)).ToList();
    }

    /// true and false count as 1 and 0 in most and least.
    static List<object?> BoolsAsNumbers(List<object?> values) => values.Select(value => value is bool flag ? (object?)(flag ? 1.0 : 0.0) : value).ToList();

    /// How most and least order values: numbers by size, text alphabetically.
    static IComparable Order(object? value) => Values.IsNumber(value) ? Values.ToDouble(value) : (IComparable)(value?.ToString() ?? "");

    static List<int> PlayersIn(object? players) => players switch
    {
        null => [],
        List<object?> list => list.Select(player => (int)Values.ToDouble(player)).ToList(),
        _ => [(int)Values.ToDouble(players)],
    };

    object? WaitForOffer(List<string> names)
    {
        if (game.Runner.HasAnswer())
        {
            return game.Runner.AnswerOrWait(null);
        }
        if (game.Over || !names.Any(game.Plays.CanDo))
        {
            return null;
        }
        return game.Runner.AnswerOrWait(new OfferRequest(names));
    }

    static List<string> NamesOf(object? actionNames) =>
        actionNames is List<object?> list ? list.Select(name => name as string ?? Values.Format(name)).ToList() : [actionNames as string ?? Values.Format(actionNames)];

    object? WaitForChoice(object? options, int amount, bool exactly, bool single, string prompt)
    {
        if (game.Runner.HasAnswer())
        {
            return game.Runner.AnswerOrWait(null);
        }
        List<object?> offered = Choice.OptionsFrom(options);
        if (!single && offered.Any(option => option is not Card))
        {
            game.ReportFailure("Only cards can be chosen several at a time; choose(...) picks one zone or value");
            return new List<object?>();
        }
        if (offered.Count == 0 || amount <= 0)
        {
            return single ? null : new List<object?>();
        }
        if (exactly && !single && offered.Count <= amount)
        {
            return offered;
        }
        return game.Runner.AnswerOrWait(new ChoiceRequest(offered, Math.Min(amount, offered.Count), exactly, prompt));
    }

    static List<List<Card>> OneFromEach(List<List<Card>> cardGroups)
    {
        List<List<Card>> picks = [[]];
        foreach (List<Card> group in cardGroups)
        {
            picks = picks.SelectMany(pick => group.Select(card => (List<Card>)[.. pick, card])).ToList();
        }
        return picks;
    }
}
