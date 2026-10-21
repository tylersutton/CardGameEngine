using System.Globalization;

namespace Core;

/// Only what steps read from a card; the full Card comes with the rest of the core.
public class Card(int id, Dictionary<string, string> data, HashSet<string> numberColumns)
{
    const string TitleColumn = "name";

    public int Id { get; } = id;
    public Dictionary<string, string> Data { get; } = data;
    public bool FaceUp { get; set; } = true;
    public string ZoneName { get; set; } = "";
    public string Title => Column(TitleColumn);

    /// A column's text, whatever the capitals ("kind" reads a "Kind" column), or missing if the card has no such column.
    public string Column(string name, string missing = "") => ColumnMatching(name) is { } column ? Data[column] : missing;

    /// card.suit, card.face_up: a property, or else a column. A column of numbers gives numbers (a blank is 0); a
    /// column that mixes in any text (rank: 2 ... 10, J, Q, K, A) gives text, so card.rank == 'A' and
    /// card.rank == '10' both work.
    public bool TryRead(string name, out object? value)
    {
        value = name switch
        {
            "id" => (long)Id,
            "face_up" => FaceUp,
            "zone_name" => ZoneName,
            "title" => Title,
            "data" => Data,
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
        value = !numberColumns.Contains(column) ? text
            : text.Contains('.') ? (object)double.Parse(text, CultureInfo.InvariantCulture)
            : long.TryParse(text, out long whole) ? whole : 0L;
        return true;
    }

    string? ColumnMatching(string name) =>
        Data.Keys.FirstOrDefault(column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));
}
