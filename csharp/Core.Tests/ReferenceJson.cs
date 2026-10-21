using System.Collections;
using System.Globalization;
using System.Text.Json;
using Core;

/// Compares JSON recorded from GDScript with C# values: objects key by key in order, lists item by item, and
/// numbers by kind too, since GDScript writes whole numbers as "4" and decimals as "4.0".
public static class ReferenceJson
{
    /// Where the value differs from the recording (a path like rules.zones.stock.anchor[1]), or null if it's the same.
    public static string? Difference(JsonElement expected, object? actual, string where, double tolerance = 0)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                if (actual is not IDictionary dictionary)
                {
                    return $"{where}: wanted an object, got {Describe(actual)}";
                }
                List<string> wantedKeys = expected.EnumerateObject().Select(property => property.Name).ToList();
                List<string> keys = dictionary.Keys.Cast<string>().ToList();
                if (!wantedKeys.SequenceEqual(keys))
                {
                    return $"{where}: wanted keys {string.Join(", ", wantedKeys)}, got {string.Join(", ", keys)}";
                }
                return expected.EnumerateObject().Select(property => Difference(property.Value, dictionary[property.Name], $"{where}.{property.Name}", tolerance)).FirstOrDefault(difference => difference != null);
            case JsonValueKind.Array:
                if (actual is not IList list || list.Count != expected.GetArrayLength())
                {
                    return $"{where}: wanted a list of {expected.GetArrayLength()}, got {Describe(actual)}";
                }
                return expected.EnumerateArray().Select((item, i) => Difference(item, list[i], $"{where}[{i}]", tolerance)).FirstOrDefault(difference => difference != null);
            case JsonValueKind.Number:
                bool wantedWhole = !expected.GetRawText().Any(character => character is '.' or 'e' or 'E');
                bool whole = actual is long or int;
                if (wantedWhole != whole || !(actual is long or int or double or float))
                {
                    return $"{where}: wanted {expected.GetRawText()}, got {Describe(actual)}";
                }
                double number = Convert.ToDouble(actual, CultureInfo.InvariantCulture);
                return Math.Abs(number - expected.GetDouble()) <= tolerance ? null : $"{where}: wanted {expected.GetRawText()}, got {number}";
            case JsonValueKind.String:
                return actual is string text && text == expected.GetString() ? null : $"{where}: wanted \"{expected.GetString()}\", got {Describe(actual)}";
            case JsonValueKind.True or JsonValueKind.False:
                return actual is bool flag && flag == expected.GetBoolean() ? null : $"{where}: wanted {expected.GetRawText()}, got {Describe(actual)}";
            default:
                return actual == null ? null : $"{where}: wanted null, got {Describe(actual)}";
        }
    }

    static string Describe(object? value) => value == null ? "null" : $"{Values.Format(value)} ({value.GetType().Name})";
}
