namespace Core;

/// Picks moves for computer players: each legal move is tried out (silently, then undone) and measured with the
/// game's "prefer" expression; with none, any move that changes something will do.
public class Bot
{
    const int MaxInputsPerTurn = 30;
    const int DefaultSamples = 4;

    readonly Game game;
    /// The bot's own random numbers, never the match's, so a bot's thinking never changes the match (or online
    /// copies would differ).
    readonly Random random;
    int turnPlayer;
    int inputsThisTurn;
    int historyAfterLastInput = -1;

    public Bot(Game game, int? seed = null)
    {
        this.game = game;
        random = seed is int fixedSeed ? new(fixedSeed) : new();
    }

    /// The bot whose turn it is makes one move; false if it isn't a bot's turn, or it can't move. forPeople: on a
    /// person's turn too, playing for them.
    public bool TakeInput(bool forPeople = false)
    {
        if (!(forPeople || game.IsBotTurn()) || game.Over)
        {
            return false;
        }
        if (game.CurrentPlayer() != turnPlayer || game.History.Count != historyAfterLastInput)
        {
            turnPlayer = game.CurrentPlayer();
            inputsThisTurn = 0;
        }
        if (inputsThisTurn >= MaxInputsPerTurn)
        {
            string hint = game.Rules.Bot.ContainsKey("prefer") ? " (is \"prefer\" pushing it in circles?)" : "";
            game.ReportFailure($"Bot {turnPlayer} stopped after {MaxInputsPerTurn} inputs this turn{hint}");
            return false;
        }
        string prefer = game.Rules.Bot.GetValueOrDefault("prefer") as string ?? "";
        if (game.Choice.IsOpen)
        {
            prefer = game.Rules.Actions.GetValueOrDefault(game.Choice.Action)?.GetValueOrDefault("prefer") as string ?? prefer;
        }
        List<PlayerInput> bestMove = prefer.Length > 0 ? BestMove(prefer) : AnyMove();
        if (bestMove.Count == 0)
        {
            return false;
        }
        inputsThisTurn++;
        game.LogLine($"[bot {turnPlayer}]");
        game.ApplyMove(bestMove, turnPlayer);
        historyAfterLastInput = game.History.Count;
        return true;
    }

    /// Runs the move silently, measures the result as the current player, then undoes it. Gives null if the move
    /// changed nothing (e.g. only reordered cards). With deals (a guess at each hidden card's data), the move is
    /// tried with each guess and the results averaged, so the measure can't peek. lookAhead: see TryOnce.
    public object? TryOut(List<PlayerInput> move, string measure, List<Dictionary<Card, Dictionary<string, string>>>? deals = null, StartingPoint? start = null, bool lookAhead = false)
    {
        start ??= StartingPoint.Of(game);
        if (deals == null || deals.Count == 0)
        {
            return TryOnce(move, measure, start, 0, lookAhead);
        }
        double total = 0;
        foreach (Dictionary<Card, Dictionary<string, string>> deal in deals)
        {
            Dictionary<Card, Dictionary<string, string>> realData = deal.Keys.ToDictionary(card => card, card => card.Data);
            foreach ((Card card, Dictionary<string, string> guess) in deal)
            {
                card.Data = guess;
            }
            object? value = TryOnce(move, measure, start, 0, lookAhead);
            foreach ((Card card, Dictionary<string, string> data) in realData)
            {
                card.Data = data;
            }
            if (!Values.IsNumber(value))
            {
                return null;
            }
            total += Values.ToDouble(value);
        }
        return total / deals.Count;
    }

    /// Cards whose identity the player can't know: face down (even their own, like their deck's order), or in a
    /// zone hidden from them.
    public List<Card> HiddenFrom(int player) => game.Cards.Where(card => !game.CanSee(player, card)).ToList();

    /// The game before trying moves out, shared by every move compared from it: to go back to, and to tell whether
    /// a move changed anything. A move that only leads on to another offer (a choice answered, then "Go fish") changes
    /// the game too.
    public record StartingPoint(Snapshot State, Dictionary<string, object?> Contents, List<string> Offered)
    {
        public static StartingPoint Of(Game game) => new(Snapshot.Take(game), Snapshot.Describe(game, false), [.. game.Plays.Offered]);
    }

    /// "depth": 2 in the game's bot: also look at the other player's best answer to each move.
    bool LooksAhead => Values.IsNumber(game.Rules.Bot.GetValueOrDefault("depth")) && Values.ToDouble(game.Rules.Bot["depth"]) >= 2;

    /// The move that scores best; among equally good ones, any, so a bot doesn't make the same move every time.
    List<PlayerInput> BestMove(string prefer)
    {
        List<List<PlayerInput>> bestMoves = [];
        double bestValue = double.NegativeInfinity;
        List<Dictionary<Card, Dictionary<string, string>>> deals = Deals();
        StartingPoint start = StartingPoint.Of(game);
        foreach (List<PlayerInput> move in game.Plays.LegalMoves())
        {
            object? value = TryOut(move, prefer, deals, start, LooksAhead);
            if (!Values.IsNumber(value) || Values.ToDouble(value) < bestValue)
            {
                continue;
            }
            if (Values.ToDouble(value) > bestValue)
            {
                bestMoves.Clear();
                bestValue = Values.ToDouble(value);
            }
            bestMoves.Add(move);
        }
        return bestMoves.Count > 0 ? bestMoves[random.Next(bestMoves.Count)] : [];
    }

    /// With no "prefer", any legal move that changes something (not one that only sorts, say): enough to play a
    /// prototype through.
    List<PlayerInput> AnyMove()
    {
        List<List<PlayerInput>> moves = game.Plays.LegalMoves();
        Shuffle(moves);
        return moves.FirstOrDefault(move => TryOut(move, "0") != null) ?? [];
    }

    /// Random identities for the cards this bot can't see and doesn't remember seeing, shared by every move it
    /// compares. Its own face-down cards are only shuffled among themselves: it knows what it owns, just not the
    /// order. (Cards held facing away from it, face "others", could be anything it can't see.)
    List<Dictionary<Card, Dictionary<string, string>>> Deals()
    {
        int player = game.CurrentPlayer();
        List<Card> hidden = HiddenFrom(player).Where(card => !game.HasSeen(player, card)).ToList();
        if (game.Rules.Bot.GetValueOrDefault("peek") is true || hidden.Count == 0)
        {
            return [];
        }
        bool Known(Card card) => game.ZoneOf(card).Owner == player && game.ZoneOf(card).Face != Zone.FaceOthers;
        List<Card> own = hidden.Where(Known).ToList();
        List<Card> others = hidden.Where(card => !Known(card)).ToList();
        int samples = (int)(game.Rules.Bot.GetValueOrDefault("samples") as double? ?? DefaultSamples);
        List<Dictionary<Card, Dictionary<string, string>>> deals = [];
        for (int sample = 0; sample < samples; sample++)
        {
            Dictionary<Card, Dictionary<string, string>> deal = [];
            foreach (List<Card> group in new[] { own, others })
            {
                List<Dictionary<string, string>> identities = group.Select(card => card.Data).ToList();
                Shuffle(identities);
                foreach (Card card in group.OrderByDescending(card => card.Marks.Count))
                {
                    int fitting = identities.FindIndex(data => FitsMarks(data, card));
                    deal[card] = identities[Math.Max(fitting, 0)];
                    identities.RemoveAt(Math.Max(fitting, 0));
                }
            }
            deals.Add(deal);
        }
        return deals;
    }

    /// A mark named after a column (a "color" mark on a card with a color column) is what the bot knows about it.
    static bool FitsMarks(Dictionary<string, string> data, Card card) =>
        card.Marks.All(mark => !data.TryGetValue(mark.Key, out string? value) || value == Values.Format(mark.Value));

    /// forPlayer: whose point of view the result is measured from (the bot's own, even when trying an opponent's
    /// reply). lookAhead: whether to answer a move that passes the turn with the other player's best reply.
    object? TryOnce(List<PlayerInput> move, string measure, StartingPoint start, int forPlayer = 0, bool lookAhead = false)
    {
        int player = game.CurrentPlayer();
        forPlayer = forPlayer != 0 ? forPlayer : player;
        int historySize = game.History.Count;
        bool wasQuiet = game.Quiet;
        bool wasRemembering = game.Remembering;
        game.GameLog.Flush();
        game.SetQuiet(true);
        game.Remembering = false;
        // The bot can't know how dice will roll or a deck will shuffle, so it tries the move with random numbers of
        // its own rather than the ones the match will really use.
        game.Random.State = (ulong)random.NextInt64();
        foreach (PlayerInput input in move)
        {
            game.RunInput(input);
        }
        object? value = null;
        if (game.Choice.IsOpen && start.State.ChoiceTitle == "" && game.CurrentPlayer() == player)
        {
            value = BestChoiceValue(measure, lookAhead);
        }
        else if (!Values.DeepEquals(Snapshot.Describe(game, false), start.Contents) || game.CurrentPlayer() != player || !game.Plays.Offered.SequenceEqual(start.Offered))
        {
            value = lookAhead && !game.Over && game.CurrentPlayer() != forPlayer ? WorstReply(measure, forPlayer) : MeasureAs(measure, forPlayer);
        }
        start.State.Restore(game);
        game.History.RemoveRange(historySize, game.History.Count - historySize);
        game.GameLog.Discard();
        game.SetQuiet(wasQuiet);
        game.Remembering = wasRemembering;
        return value;
    }

    object? MeasureAs(string measure, int player)
    {
        if (game.Variables.ContainsKey(Game.PlayerVariable))
        {
            game.Variables[Game.PlayerVariable] = (long)player;
        }
        return game.Evaluate(measure);
    }

    /// With "depth": 2, a move that passes the turn is worth what the next player's best answer leaves it worth.
    object? WorstReply(string measure, int forPlayer)
    {
        object? worst = null;
        StartingPoint start = StartingPoint.Of(game);
        foreach (List<PlayerInput> reply in game.Plays.LegalMoves())
        {
            object? value = TryOnce(reply, measure, start, forPlayer);
            if (Values.IsNumber(value) && (worst == null || Values.ToDouble(value) < Values.ToDouble(worst)))
            {
                worst = value;
            }
        }
        return worst ?? MeasureAs(measure, forPlayer);
    }

    /// When a move opens a choice (e.g. playing Chapel), it's worth what the best way of making that choice is worth.
    object? BestChoiceValue(string measure, bool lookAhead)
    {
        object? best = null;
        StartingPoint start = StartingPoint.Of(game);
        foreach (List<PlayerInput> choiceMove in game.Plays.LegalMoves())
        {
            object? value = TryOnce(choiceMove, measure, start, 0, lookAhead);
            if (Values.IsNumber(value) && (best == null || Values.ToDouble(value) > Values.ToDouble(best)))
            {
                best = value;
            }
        }
        return best;
    }

    void Shuffle<T>(List<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
