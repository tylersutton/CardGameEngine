namespace Core;

/// The files a game reads, by the res:// paths game files use ("res://decks/standard_deck.csv"): read from the
/// project folder, or from text or bytes given in their place (the editor plays its unsaved edits this way, and an
/// online host sends its game, decks and images).
public class GameFiles(string projectFolder, Dictionary<string, string>? given = null, Dictionary<string, byte[]>? givenBytes = null)
{
    public const string GamesFolder = "res://games/";
    public const string ImagesFolder = "res://images/";
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".svg"];
    /// Game files are .jsonc (JSON with comments); plain .json works too.
    static readonly string[] GameExtensions = [".jsonc", ".json"];
    const string ProjectPrefix = "res://";

    readonly Dictionary<string, string> given = given ?? [];
    readonly Dictionary<string, byte[]> givenBytes = givenBytes ?? [];

    public bool Exists(string path) => given.ContainsKey(path) || givenBytes.ContainsKey(path) || File.Exists(FullPath(path));

    public string Read(string path) => given.TryGetValue(path, out string? text) ? text : File.ReadAllText(FullPath(path));

    public byte[] ReadBytes(string path) => givenBytes.TryGetValue(path, out byte[]? bytes) ? bytes : File.ReadAllBytes(FullPath(path));

    /// Whether a value names an image file (by its extension), rather than a color or a column.
    public static bool IsImage(string value) => ImageExtensions.Contains(Path.GetExtension(value).ToLowerInvariant());

    /// An image's res:// path: game files name images in images/ by their path there ("moat.png", "cards/A_Spades.png").
    public static string ImagePath(string name) => name.StartsWith(ProjectPrefix) ? name : ImagesFolder + name;

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
