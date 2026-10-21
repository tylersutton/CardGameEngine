using System.Collections;
using System.Globalization;

namespace Core;

/// What steps work with: null, bool, long, double, string, List<object?>, a dictionary (card.data), Card and Zone.
/// Whole numbers stay long and other numbers double, as GDScript keeps int and float apart.
public static class Values
{
    public static bool IsNumber(object? value) => value is long or double;

    public static double ToDouble(object? value) => value is long whole ? whole : (double)value!;

    /// GDScript's truthiness: nothing, false, 0, '' and empty lists are false; everything else is true.
    public static bool IsTrue(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        long whole => whole != 0,
        double number => number != 0,
        string text => text.Length > 0,
        ICollection items => items.Count > 0,
        _ => true,
    };

    /// Numbers equal by value (1 == 1.0), lists item by item, cards and zones only themselves.
    public static bool AreEqual(object? left, object? right)
    {
        if (IsNumber(left) && IsNumber(right))
        {
            return ToDouble(left) == ToDouble(right);
        }
        if (left is List<object?> leftItems && right is List<object?> rightItems)
        {
            return leftItems.Count == rightItems.Count && leftItems.Zip(rightItems).All(pair => AreEqual(pair.First, pair.Second));
        }
        return Equals(left, right);
    }

    /// Things of different kinds can't be equal, so comparing them is a mistake worth reporting (card.rank == 10
    /// when rank is text), except with null ("nothing yet") and between numbers.
    public static bool AreComparable(object? left, object? right) =>
        left == null || right == null || IsNumber(left) && IsNumber(right) || KindOf(left) == KindOf(right);

    static string KindOf(object value) => value switch
    {
        bool => "bool",
        string => "text",
        List<object?> => "list",
        IDictionary => "dictionary",
        _ => "object",
    };

    /// How a value is named in error messages: "the text 'A'", "the number 10", "the zone hand_1", ...
    public static string Describe(object? value) => value switch
    {
        null => "nothing",
        bool flag => flag ? "true" : "false",
        string text => $"the text '{text}'",
        long or double => $"the number {Format(value)}",
        Card card => $"the card {card.Title}",
        Zone zone => $"the zone {zone.Name}",
        List<object?> items => $"a list of {items.Count}",
        _ => value.ToString() ?? "",
    };

    /// A value as the log shows it: cards by title, zones by name, whole numbers without ".0".
    public static string Format(object? value) => value switch
    {
        null => "—",
        Card card => card.Title,
        Zone zone => zone.Name,
        List<object?> items => $"[{string.Join(", ", items.Select(Format))}]",
        double number when number == Math.Floor(number) => ((long)number).ToString(CultureInfo.InvariantCulture),
        _ => Text(value),
    };

    /// GDScript's str(): what 'text %s' % value fills in.
    public static string Text(object? value) => value switch
    {
        null => "<null>",
        bool flag => flag ? "true" : "false",
        double number when number == Math.Floor(number) => number.ToString("0.0", CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        long whole => whole.ToString(CultureInfo.InvariantCulture),
        string text => text,
        _ => Format(value),
    };
}
