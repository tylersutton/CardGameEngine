using Godot;

/// The engine's plain values (null, bool, long, double, string, lists and dictionaries) as Godot Variants and back,
/// to send them to other machines.
public static class GodotValues
{
    public static Variant ToVariant(object? value) => value switch
    {
        null => default,
        bool flag => flag,
        long whole => whole,
        int whole => whole,
        double number => number,
        string text => text,
        List<object?> items => new Godot.Collections.Array(items.Select(ToVariant)),
        Dictionary<string, object?> dictionary => ToDictionary(dictionary),
        _ => value.ToString() ?? "",
    };

    public static Godot.Collections.Dictionary ToDictionary(Dictionary<string, object?> dictionary)
    {
        Godot.Collections.Dictionary converted = [];
        foreach ((string key, object? item) in dictionary)
        {
            converted[key] = ToVariant(item);
        }
        return converted;
    }

    public static object? FromVariant(Variant value) => value.VariantType switch
    {
        Variant.Type.Nil => null,
        Variant.Type.Bool => value.AsBool(),
        Variant.Type.Int => value.AsInt64(),
        Variant.Type.Float => value.AsDouble(),
        Variant.Type.String or Variant.Type.StringName => value.AsString(),
        Variant.Type.Array => value.AsGodotArray().Select(FromVariant).ToList(),
        Variant.Type.Dictionary => FromDictionary(value.AsGodotDictionary()),
        _ => value.ToString(),
    };

    public static Dictionary<string, object?> FromDictionary(Godot.Collections.Dictionary dictionary) =>
        dictionary.ToDictionary(pair => pair.Key.AsString(), pair => FromVariant(pair.Value));
}
