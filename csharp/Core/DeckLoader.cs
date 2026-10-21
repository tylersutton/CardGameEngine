using System.Text;

namespace Core;

/// Decks are CSV: a row of column names, then a row per card ("count" copies of it if the deck has that column).
public static class DeckLoader
{
    public const string CountColumn = "count";

    /// A deck's cards from its CSV text.
    public static List<Card> Load(string csvText)
    {
        List<List<string>> rows = CsvLines(csvText);
        List<string> headers = rows[0];
        string countColumn = ColumnNamed(headers, CountColumn);
        string titleColumn = ColumnNamed(headers, Card.DefaultTitleColumn);
        if (!headers.Contains(titleColumn))
        {
            titleColumn = headers[0] != countColumn || headers.Count == 1 ? headers[0] : headers[1];
        }
        List<Card> cards = [];
        foreach (List<string> values in rows.Skip(1))
        {
            if (values.Count != headers.Count)
            {
                continue;
            }
            Dictionary<string, string> row = [];
            string count = "";
            for (int i = 0; i < headers.Count; i++)
            {
                if (headers[i] == countColumn)
                {
                    count = values[i];
                }
                else
                {
                    row[headers[i]] = values[i];
                }
            }
            // Blank lines and spreadsheet extras like a totals row aren't cards.
            if (row.Values.All(IsBlank))
            {
                continue;
            }
            long copies = count.Length > 0 ? Values.ToInt(count) : 1;
            for (int copy = 0; copy < copies; copy++)
            {
                cards.Add(new(new Dictionary<string, string>(row)) { TitleColumn = titleColumn });
            }
        }
        HashSet<string> numberColumns = [.. headers.Where(column => column != countColumn && cards.All(card => Card.IsNumber(card.Data.GetValueOrDefault(column, ""))))];
        foreach (Card card in cards)
        {
            card.NumberColumns = numberColumns;
        }
        return cards;
    }

    /// A deck's rows as lists of text (the first row is the column names), e.g. for the editor's deck table.
    public static List<List<string>> Rows(string csvText) =>
        CsvLines(csvText).Where(row => !(row.Count == 1 && row[0].Length == 0)).ToList();

    /// Rows back into CSV text, quoting values that need it.
    public static string ToCsv(List<List<string>> table)
    {
        IEnumerable<string> lines = table.Select(row => string.Join(",", row.Select(value =>
            value.Contains(',') || value.Contains('"') || value.Contains('\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value)));
        return string.Join("\n", lines) + "\n";
    }

    /// The special columns ("count", "name") are found whatever their capitals ("Count").
    static string ColumnNamed(List<string> headers, string wanted) =>
        headers.FirstOrDefault(header => header.ToLowerInvariant() == wanted) ?? wanted;

    static bool IsBlank(string value) => value.All(character => character <= ' ');

    /// The rows GDScript's get_csv_line reads, one after another until the file ends: a quoted value may hold
    /// commas, line breaks and doubled quotes (""), carriage returns are dropped, and text ending in a line break
    /// gives a last row of one empty value.
    static List<List<string>> CsvLines(string text)
    {
        string[] lines = text.TrimStart('﻿').Replace("\r", "").Split('\n');
        List<List<string>> rows = [];
        int next = 0;
        while (next < lines.Length)
        {
            StringBuilder line = new();
            do
            {
                line.Append(lines[next]).Append('\n');
                next++;
            }
            while (line.ToString().Count(character => character == '"') % 2 == 1 && next < lines.Length);
            rows.Add(SplitLine(line.ToString(0, line.Length - 1)));
        }
        return rows;
    }

    static List<string> SplitLine(string line)
    {
        List<string> values = [];
        StringBuilder current = new();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];
            if (!inQuotes && character == ',')
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else if (character == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else
            {
                current.Append(character);
            }
        }
        values.Add(current.ToString());
        return values;
    }
}
