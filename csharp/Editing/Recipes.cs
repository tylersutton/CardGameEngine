using Core;

namespace Editing;

/// Ready-made pieces of a game (templates/recipes/*.jsonc), added into the game being edited. What the game already
/// has is kept: new zones, variables, actions and settings are added, new trigger steps go after the existing ones,
/// and anything else the game already sets (including its turn) is left as it is.
public static class Recipes
{
    public const string Folder = "res://templates/recipes/";

    public static List<string> Paths(GameFiles files) =>
        Directory.GetFiles(files.FullPath(Folder), "*.jsonc").Select(file => Folder + System.IO.Path.GetFileName(file)).Order(StringComparer.Ordinal).ToList();

    /// "draw_then_discard.jsonc" -> "Draw then discard".
    public static string Title(string path)
    {
        string words = System.IO.Path.GetFileNameWithoutExtension(path).Replace("_", " ");
        return words.Length > 0 ? char.ToUpperInvariant(words[0]) + words[1..] : words;
    }

    /// The // lines at the top of the recipe, which say what it does.
    public static string Description(GameFiles files, string path)
    {
        IEnumerable<string> lines = files.Read(path).Split('\n').Select(line => line.Trim()).TakeWhile(line => line.StartsWith("//"));
        return string.Join(" ", lines.Select(line => line[2..].Trim()));
    }

    public static void AddTo(GameDocument document, GameFiles files, string path)
    {
        Dictionary<string, object?> recipe = (Dictionary<string, object?>)GameRules.Parse(files.Read(path))!;
        foreach ((string key, object? value) in recipe)
        {
            Merge(document, [key], value);
        }
    }

    static void Merge(GameDocument document, List<object> path, object? value)
    {
        if (!document.Has(path))
        {
            document.SetValue(path, value);
            return;
        }
        object? existing = document.GetValue(path);
        if (existing is Dictionary<string, object?> && value is Dictionary<string, object?> members)
        {
            foreach ((string key, object? member) in members)
            {
                Merge(document, [.. path, key], member);
            }
        }
        else if (path.Count == 2 && Equals(path[0], "triggers"))
        {
            if (existing is string single)
            {
                document.SetValue(path, new List<object?> { single });
            }
            foreach (object? item in value as List<object?> ?? [value])
            {
                List<object?> list = (List<object?>)document.GetValue(path)!;
                if (!list.Any(step => Values.DeepEquals(step, item)))
                {
                    document.Insert(path, list.Count, item);
                }
            }
        }
    }
}
