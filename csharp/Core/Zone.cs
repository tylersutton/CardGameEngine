using System.Numerics;

namespace Core;

/// A place cards can be: a deck, a hand, a pile, a space on a board. Built from its definition in the game file;
/// per-player zones are one zone per player ("hand_1", "hand_2"), placed round the table, and a board is one zone per
/// space.
public class Zone
{
    public const string FaceDown = "down";
    public const string FaceUp = "up";
    public const string FacePrivate = "private";
    public const string FaceOthers = "others";
    public static readonly string[] Faces = [FaceUp, FaceDown, FacePrivate, FaceOthers];
    /// Older names for "private", still accepted.
    static readonly string[] PrivateAliases = ["owner", "human"];
    const string DragStack = "stack";
    const string LabelNumber = "#";

    public string Name { get; }
    public string Label { get; }
    public bool HasOutline { get; }
    public string Kind { get; }
    public int Owner { get; }
    public string Layout { get; }
    public string Face { get; }
    public Vector2 Anchor { get; }
    /// A board space's place, in pixels from the board's anchor (zero for other zones).
    public Vector2 Offset { get; }
    /// The board this zone is a space of, and which space: its copy number.
    public Board? Board { get; }
    public int Number { get; }
    public double Width { get; }
    public double Rotation { get; }
    public bool DragsStack { get; }
    public int Spread { get; }
    /// Whether the table shows how many cards the zone holds (seeds in a Mancala pit).
    public bool ShowsCount { get; }
    /// Whether players may rearrange its cards by dragging them (not Scout's hand, which keeps its order).
    public bool Reorders { get; }
    public List<object?> CoveredBy { get; } = [];
    public List<Card> Cards { get; } = [];
    public List<string> SortColumns { get; private set; } = [];

    public event Action<Zone>? Changed;

    /// While a bot tries moves out, nothing is announced.
    public bool Quiet { get; set; }

    /// seats: how many players there are, for per-player zones placed round the table from seat 1's "anchor".
    public Zone(string kind, Dictionary<string, object?> definition, int number = 0, int seats = 1, Board? board = null)
    {
        bool perPlayer = Read(definition, "per_player", false);
        Kind = kind;
        Number = number;
        Board = board;
        Owner = perPlayer ? number : 0;
        Name = number == 0 ? kind : $"{kind}_{number}";
        List<object?> labels = Read<List<object?>>(definition, "labels", []);
        Label = number > 0 && labels.Count >= number ? (string)labels[number - 1]! : Read(definition, "label", "").Replace(LabelNumber, number.ToString());
        HasOutline = Read(definition, "outline", true);
        Layout = Read(definition, "layout", board != null ? "pile" : "row");
        Face = Read(definition, "face", FaceUp);
        if (PrivateAliases.Contains(Face))
        {
            Face = FacePrivate;
        }
        List<object?> anchorValues = number > 0 && definition.ContainsKey("anchors")
            ? (List<object?>)((List<object?>)definition["anchors"]!)[number - 1]!
            : Read<List<object?>>(definition, "anchor", [0.5, 0.5]);
        Anchor = new((float)(double)anchorValues[0]!, (float)(double)anchorValues[1]!);
        if (number > 0 && definition.TryGetValue("grid", out object? grid))
        {
            Dictionary<string, object?> gridSettings = (Dictionary<string, object?>)grid!;
            int columns = (int)(double)gridSettings["columns"]!;
            List<object?> spacing = (List<object?>)gridSettings["spacing"]!;
            Anchor += new Vector2((number - 1) % columns * (float)(double)spacing[0]!, (number - 1) / columns * (float)(double)spacing[1]!);
        }
        if (board != null)
        {
            Offset = board.Offset(number);
        }
        Width = Read(definition, "width", 0.0);
        List<object?> rotations = Read<List<object?>>(definition, "rotations", []);
        Rotation = number > 0 && rotations.Count >= number ? (double)rotations[number - 1]! : Read(definition, "rotation", 0.0);
        if (perPlayer && number > 1 && !definition.ContainsKey("anchors"))
        {
            Anchor = AroundTable(Anchor, number, seats);
            Rotation += SeatTurn(number, seats);
        }
        DragsStack = Read(definition, "drag", "") == DragStack;
        Spread = (int)Read(definition, "spread", 0.0);
        ShowsCount = Read(definition, "show_count", false);
        Reorders = Read(definition, "reorder", true);
        List<object?> covering = Read<List<object?>>(definition, "covered_by", []);
        if (number > 0 && covering.Count >= number)
        {
            CoveredBy = (List<object?>)covering[number - 1]!;
        }
    }

    /// Where seat 1's spot is for another seat: turned round the table's center, one equal step per seat
    /// (seat 2 on seat 1's left, as play goes clockwise).
    public static Vector2 AroundTable(Vector2 seatOneSpot, int seat, int seats)
    {
        Vector2 center = new(0.5f, 0.5f);
        float angle = MathF.Tau * (seat - 1) / seats;
        Vector2 offset = seatOneSpot - center;
        return center + new Vector2(offset.X * MathF.Cos(angle) - offset.Y * MathF.Sin(angle), offset.X * MathF.Sin(angle) + offset.Y * MathF.Cos(angle));
    }

    /// How far cards turn at that seat: sideways at the sides, but upright across the table, so they stay readable.
    public static double SeatTurn(int seat, int seats)
    {
        int quarterTurns = (int)Math.Round(4.0 * (seat - 1) / seats, MidpointRounding.AwayFromZero) % 4;
        return quarterTurns == 2 ? 0 : 90 * quarterTurns;
    }

    public bool IsEmpty => Cards.Count == 0;

    public void Add(Card card)
    {
        card.FaceUp = Face != FaceDown;
        card.Revealed = false;
        card.Marks.Clear();
        card.ZoneName = Name;
        Cards.Add(card);
        Sort();
        AnnounceChanged();
    }

    public void AddAll(List<Card> newCards)
    {
        foreach (Card card in newCards)
        {
            card.FaceUp = Face != FaceDown;
            card.Revealed = false;
            card.ZoneName = Name;
        }
        Cards.AddRange(newCards);
        Sort();
        AnnounceChanged();
    }

    public void Remove(Card card)
    {
        Cards.Remove(card);
        AnnounceChanged();
    }

    public void Reorder(Card card, int index)
    {
        SortColumns = [];
        Cards.Remove(card);
        Cards.Insert(index, card);
        AnnounceChanged();
    }

    public void Restore(List<Card> savedCards, List<string> savedSortColumns)
    {
        Cards.Clear();
        Cards.AddRange(savedCards);
        foreach (Card card in Cards)
        {
            card.ZoneName = Name;
        }
        SortColumns = [.. savedSortColumns];
        AnnounceChanged();
    }

    /// Where the card would go if it were added, given the zone's sorting.
    public int SortedIndex(Card card) => Cards.Count(other => other != card && !Precedes(card, other));

    /// Shuffles with the match's own random numbers, so a match replays the same from its seed.
    public void Shuffle(RandomNumbers random)
    {
        for (int i = Cards.Count - 1; i > 0; i--)
        {
            int j = random.NextInRange(0, i);
            (Cards[i], Cards[j]) = (Cards[j], Cards[i]);
        }
        AnnounceChanged();
    }

    public void KeepSortedBy(List<string> columns)
    {
        SortColumns = columns;
        Sort();
        AnnounceChanged();
    }

    /// Tells the table the zone changed (a card in it flipped, say).
    public void AnnounceChanged()
    {
        if (!Quiet)
        {
            Changed?.Invoke(this);
        }
    }

    public bool TryRead(string member, out object? value)
    {
        value = member switch
        {
            "name" => Name,
            "cards" => Cards.Cast<object?>().ToList(),
            "number" => (long)Number,
            "owner" => (long)Owner,
            "column" when Board != null => (long)Board.Column(Number),
            "row" when Board != null => (long)Board.Row(Number),
            _ => null,
        };
        return value != null;
    }

    void Sort()
    {
        if (SortColumns.Count > 0)
        {
            GodotSort.Sort(Cards, Precedes);
        }
    }

    bool Precedes(Card a, Card b)
    {
        foreach (string column in SortColumns)
        {
            string aValue = a.Column(column);
            string bValue = b.Column(column);
            if (aValue == bValue)
            {
                continue;
            }
            if (Values.IsValidFloat(aValue) && Values.IsValidFloat(bValue))
            {
                return Values.ToFloat(aValue) < Values.ToFloat(bValue);
            }
            return Values.CompareText(aValue, bValue) < 0;
        }
        return false;
    }

    /// A setting from the definition, or its default when it's missing. The load checks (StepCompiler) have
    /// already made sure each setting is the right kind; JSON numbers arrive as doubles.
    static T Read<T>(Dictionary<string, object?> definition, string key, T fallback) =>
        definition.TryGetValue(key, out object? value) && value is T typed ? typed : fallback;
}
