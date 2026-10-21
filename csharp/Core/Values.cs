using System.Collections;
using System.Globalization;
using System.Text;

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

    /// GDScript's == on whole states: numbers by value, lists item by item, dictionaries key by key (in any order),
    /// and anything else (cards, zones) only itself.
    public static bool DeepEquals(object? left, object? right)
    {
        if (IsNumber(left) && IsNumber(right))
        {
            return ToDouble(left) == ToDouble(right);
        }
        if (left is IDictionary leftDictionary && right is IDictionary rightDictionary)
        {
            return leftDictionary.Count == rightDictionary.Count
                && leftDictionary.Keys.Cast<object>().All(key => rightDictionary.Contains(key) && DeepEquals(leftDictionary[key], rightDictionary[key]));
        }
        if (left is IList leftList && right is IList rightList)
        {
            return leftList.Count == rightList.Count && Enumerable.Range(0, leftList.Count).All(i => DeepEquals(leftList[i], rightList[i]));
        }
        return Equals(left, right);
    }

    /// GDScript's duplicate(true): lists and dictionaries copied all the way down, so changing the copy leaves the
    /// original alone (cards and zones stay themselves).
    public static object? DeepCopy(object? value) => value switch
    {
        List<object?> items => items.Select(DeepCopy).ToList(),
        Dictionary<string, object?> dictionary => dictionary.ToDictionary(pair => pair.Key, pair => DeepCopy(pair.Value)),
        _ => value,
    };

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

    /// GDScript's text order (a < b): character by character, by Unicode number.
    public static int CompareText(string left, string right)
    {
        StringRuneEnumerator rightRunes = right.EnumerateRunes();
        foreach (Rune leftRune in left.EnumerateRunes())
        {
            if (!rightRunes.MoveNext())
            {
                return 1;
            }
            int order = leftRune.Value.CompareTo(rightRunes.Current.Value);
            if (order != 0)
            {
                return order;
            }
        }
        return rightRunes.MoveNext() ? -1 : 0;
    }

    /// GDScript's is_valid_float(): digits with an optional sign, decimal point and exponent ("-2", "1.5", "3e4").
    public static bool IsValidFloat(string text)
    {
        int start = text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;
        bool exponentFound = false;
        bool pointFound = false;
        bool exponentSignFound = false;
        bool exponentDigitsFound = false;
        bool digitsFound = false;
        for (int i = start; i < text.Length; i++)
        {
            char character = text[i];
            if (char.IsAsciiDigit(character))
            {
                exponentDigitsFound |= exponentFound;
                digitsFound |= !exponentFound;
            }
            else if (digitsFound && !exponentFound && character == 'e')
            {
                exponentFound = true;
            }
            else if (!pointFound && !exponentFound && character == '.')
            {
                pointFound = true;
            }
            else if (character is '-' or '+' && exponentFound && !exponentDigitsFound && !exponentSignFound)
            {
                exponentSignFound = true;
            }
            else
            {
                return false;
            }
        }
        return digitsFound;
    }

    /// GDScript's to_float(), for text that IsValidFloat allows; anything else is 0.
    public static double ToFloat(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;

    /// GDScript's to_int(): the digits up to any decimal point, ignoring other characters ("2.5" is 2, "1e5" is
    /// 15), with each minus sign before them flipping the sign ("-7" is -7, "--3" is 3).
    public static long ToInt(string text)
    {
        long number = 0;
        bool negative = false;
        foreach (char character in text)
        {
            if (character == '.')
            {
                break;
            }
            if (char.IsAsciiDigit(character))
            {
                number = number * 10 + (character - '0');
            }
            else if (character == '-' && number == 0)
            {
                negative = !negative;
            }
        }
        return negative ? -number : number;
    }

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
