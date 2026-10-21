namespace Core;

/// The files a game reads, by the res:// paths game files use ("res://decks/standard_deck.csv"): read from the
/// project folder, or from text given in their place (the editor plays its unsaved edits this way).
public class GameFiles(string projectFolder, Dictionary<string, string>? given = null)
{
    public const string GamesFolder = "res://games/";
    /// Game files are .jsonc (JSON with comments); plain .json works too.
    static readonly string[] GameExtensions = [".jsonc", ".json"];
    const string ProjectPrefix = "res://";

    readonly Dictionary<string, string> given = given ?? [];

    public bool Exists(string path) => given.ContainsKey(path) || File.Exists(FullPath(path));

    public string Read(string path) => given.TryGetValue(path, out string? text) ? text : File.ReadAllText(FullPath(path));

    /// Where a res:// path is on disk.
    public string FullPath(string path) => path.StartsWith(ProjectPrefix) ? Path.Combine(projectFolder, path[ProjectPrefix.Length..]) : path;

    /// Every game file in games/, by name.
    public List<string> GamePaths() =>
        Directory.GetFiles(FullPath(GamesFolder))
            .Where(file => GameExtensions.Contains(Path.GetExtension(file)))
            .Select(file => GamesFolder + Path.GetFileName(file))
            .Order(StringComparer.Ordinal)
            .ToList();
}
