namespace Core;

/// Melds (rummy sets and runs) from the game file's "melds": each type is cards sharing a column ("same"), maybe
/// in a run of another ("sequence"), at least "min" of them. Finds the melds that leave the fewest points unmelded.
public class Melds
{
    const int DefaultMinSize = 3;

    readonly List<Dictionary<string, object?>> types;
    readonly string pointsColumn;

    public Melds(Dictionary<string, object?> config)
    {
        types = (config.GetValueOrDefault("types") as List<object?> ?? []).OfType<Dictionary<string, object?>>().ToList();
        pointsColumn = config.GetValueOrDefault("points") as string ?? "points";
    }

    public List<object?> Best(List<Card> cards) => Search(cards, 0, Candidates(cards), []).Melds;

    public List<Card> Deadwood(List<Card> cards)
    {
        HashSet<Card> melded = [.. Best(cards).SelectMany(meld => ((List<object?>)meld!).Cast<Card>())];
        return cards.Where(card => !melded.Contains(card)).ToList();
    }

    public List<Card> Layoff(List<Card> cards, List<object?> melds)
    {
        List<Card> bestChoice = [];
        double fewestPoints = double.PositiveInfinity;
        foreach (List<Card> choice in Subsets(Extensions(cards, melds)))
        {
            if (Extensions(choice, melds).Count != choice.Count)
            {
                continue;
            }
            double points = DeadwoodPoints(cards.Where(card => !choice.Contains(card)).ToList());
            if (points < fewestPoints)
            {
                bestChoice = choice;
                fewestPoints = points;
            }
        }
        return bestChoice;
    }

    public bool IsMeld(List<Card> cards) => types.Any(type => Fits(cards, type));

    public static Dictionary<string, List<Card>> GroupBy(List<Card> cards, string column)
    {
        Dictionary<string, List<Card>> groups = [];
        foreach (Card card in cards)
        {
            string key = card.Column(column);
            if (!groups.TryGetValue(key, out List<Card>? group))
            {
                groups[key] = group = [];
            }
            group.Add(card);
        }
        return groups;
    }

    /// Every subset, in the order GDScript makes them: [], [a], [b], [a, b], [c], ...
    public static List<List<T>> Subsets<T>(List<T> items)
    {
        List<List<T>> subsets = [[]];
        foreach (T item in items)
        {
            subsets.AddRange(subsets.Select(subset => (List<T>)[.. subset, item]).ToList());
        }
        return subsets;
    }

    double DeadwoodPoints(List<Card> cards) => Search(cards, 0, Candidates(cards), []).Deadwood;

    List<Card> Extensions(List<Card> cards, List<object?> melds)
    {
        List<List<Card>> extendedMelds = melds.Select(meld => ((List<object?>)meld!).Cast<Card>().ToList()).ToList();
        List<Card> laidOff = [];
        bool placedACard = true;
        while (placedACard)
        {
            placedACard = false;
            foreach (Card card in cards)
            {
                if (laidOff.Contains(card))
                {
                    continue;
                }
                foreach (List<Card> meld in extendedMelds)
                {
                    if (IsMeld([.. meld, card]))
                    {
                        meld.Add(card);
                        laidOff.Add(card);
                        placedACard = true;
                        break;
                    }
                }
            }
        }
        return laidOff;
    }

    bool Fits(List<Card> cards, Dictionary<string, object?> type)
    {
        if (cards.Count < (type.GetValueOrDefault("min") is double min ? min : DefaultMinSize))
        {
            return false;
        }
        string same = (string)type["same"]!;
        string sharedValue = cards[0].Column(same);
        if (!cards.All(card => card.Column(same) == sharedValue))
        {
            return false;
        }
        if (type.GetValueOrDefault("sequence") is not string sequence)
        {
            return true;
        }
        List<long> values = cards.Select(card => Values.ToInt(card.Column(sequence, "0"))).Order().ToList();
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] + 1)
            {
                return false;
            }
        }
        return true;
    }

    List<List<Card>> Candidates(List<Card> cards)
    {
        List<List<Card>> candidates = [];
        foreach (Dictionary<string, object?> type in types)
        {
            foreach (List<Card> group in GroupBy(cards, (string)type["same"]!).Values)
            {
                List<List<Card>> possibleMelds = [];
                if (type.GetValueOrDefault("sequence") is string sequence)
                {
                    GodotSort.Sort(group, (a, b) => Values.ToInt(a.Column(sequence)) < Values.ToInt(b.Column(sequence)));
                    for (int start = 0; start < group.Count; start++)
                    {
                        for (int end = start + 1; end <= group.Count; end++)
                        {
                            possibleMelds.Add(group[start..end]);
                        }
                    }
                }
                else
                {
                    possibleMelds = Subsets(group);
                }
                candidates.AddRange(possibleMelds.Where(meld => Fits(meld, type)));
            }
        }
        return candidates;
    }

    (List<object?> Melds, double Deadwood) Search(List<Card> cards, int index, List<List<Card>> candidates, HashSet<Card> used)
    {
        if (index == cards.Count)
        {
            return ([], 0);
        }
        Card card = cards[index];
        if (used.Contains(card))
        {
            return Search(cards, index + 1, candidates, used);
        }
        (List<object?> restMelds, double restDeadwood) = Search(cards, index + 1, candidates, used);
        (List<object?> Melds, double Deadwood) best = (restMelds, restDeadwood + Points(card));
        foreach (List<Card> meld in candidates)
        {
            if (!meld.Contains(card) || meld.Any(used.Contains))
            {
                continue;
            }
            used.UnionWith(meld);
            (List<object?> melds, double deadwood) = Search(cards, index + 1, candidates, used);
            used.ExceptWith(meld);
            if (deadwood < best.Deadwood)
            {
                best = ([meld.Cast<object?>().ToList(), .. melds], deadwood);
            }
        }
        return best;
    }

    double Points(Card card) => Values.ToFloat(card.Column(pointsColumn, "0"));
}
