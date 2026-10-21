using System.Globalization;

namespace Core;

/// One card: its row from the deck (data), whether it's face up, and marks steps put on it.
public class Card(Dictionary<string, string> data)
{
    public const string DefaultTitleColumn = "name";

    public int Id { get; set; } = -1;

    /// The zone it's in, by name (Zone keeps it up to date): a name rather than the zone, as a zone holds its cards.
    public string ZoneName { get; set; } = "";

    public Dictionary<string, string> Data { get; } = data;

    public bool FaceUp { get; set; } = true;

    public Dictionary<string, object?> Marks { get; } = [];

    /// The deck's columns whose every entry is a number (shared by the deck's cards).
    public HashSet<string> NumberColumns { get; set; } = [];

    /// The column naming the card in the log: "name", or the deck's first column if it has none.
    public string TitleColumn { get; set; } = DefaultTitleColumn;

    public string Title => Data.GetValueOrDefault(TitleColumn, "");

    /// A column's text, whatever the capitals ("kind" reads a "Kind" column), or missing if the card has no such column.
    public string Column(string name, string missing = "") => ColumnMatching(name) is { } column ? Data[column] : missing;

    /// card.suit, card.face_up: a property, or else a column. A column of numbers gives numbers (a blank is 0); a
    /// column that mixes in any text (rank: 2 ... 10, J, Q, K, A) gives text, so card.rank == 'A' and
    /// card.rank == '10' both work. Capitals don't matter (card.color reads a "Color" column).
    public bool TryRead(string name, out object? value)
    {
        value = name switch
        {
            "id" => (long)Id,
            "zone_name" => ZoneName,
            "data" => Data,
            "face_up" => FaceUp,
            "marks" => Marks,
            "title" => Title,
            _ => null,
        };
        if (value != null)
        {
            return true;
        }
        if (ColumnMatching(name) is not { } column)
        {
            return false;
        }
        string text = Data[column];
        if (!NumberColumns.Contains(column))
        {
            value = text;
        }
        else if (text.Contains('.'))
        {
            value = Values.ToFloat(text);
        }
        else
        {
            value = Values.ToInt(text);
        }
        return true;
    }

    /// Whether a deck entry counts as a number: blank, or a number written the way GDScript would write it back
    /// ("7", "2.5", but not "07" or "2.50").
    public static bool IsNumber(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole) && whole.ToString(CultureInfo.InvariantCulture) == text)
        {
            return true;
        }
        return Values.IsValidFloat(text) && Values.Text(Values.ToFloat(text)) == text;
    }

    string? ColumnMatching(string name)
    {
        if (Data.ContainsKey(name))
        {
            return name;
        }
        return Data.Keys.FirstOrDefault(column => column.ToLowerInvariant() == name.ToLowerInvariant());
    }
}
