using System.Text;
using System.Text.Json;

/// How a recorded match ended: each zone's cards bottom to top (face-down ones marked "▼"), the variables as the
/// log shows them, and the result.
public record ReferenceEnd(Dictionary<string, List<string>> Zones, Dictionary<string, string> Variables, bool Over, string Result);

/// A match the GDScript engine played, recorded by tests/record_reference.gd: its seed, the inputs, the log lines
/// each one gave (Logs[0] is the game's start, Logs[n] input n's) and how it ended. Given the same game, seed and
/// inputs, the C# engine must give the same log.
public record ReferenceMatch(long Seed, List<JsonElement> Inputs, List<List<string>> Logs, ReferenceEnd End)
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// The game file's name, e.g. "solitaire" for games/solitaire.jsonc.
    public string Game { get; init; } = "";

    public override string ToString() => $"{Game} seed {Seed}";

    /// Every recorded match of every game.
    public static List<ReferenceMatch> All()
    {
        List<ReferenceMatch> matches = [];
        foreach (string path in Directory.GetFiles(RepositoryFiles.PathOf("csharp/Core.Tests/Reference/matches"), "*.json").Order())
        {
            string game = Path.GetFileNameWithoutExtension(path);
            List<ReferenceMatch> recorded = JsonSerializer.Deserialize<List<ReferenceMatch>>(File.ReadAllText(path), JsonOptions)!;
            matches.AddRange(recorded.Select(match => match with { Game = game }));
        }
        return matches;
    }

    /// Where a log first differs from the recording (which input, its lines and the recording's), or null if
    /// they're the same.
    public string? FirstLogDifference(List<List<string>> logs)
    {
        for (int i = 0; i < Logs.Count; i++)
        {
            if (i >= logs.Count)
            {
                return $"{this}: the log stops after {Describe(i - 1)}, but the recording goes on to {Describe(i)}";
            }
            if (!logs[i].SequenceEqual(Logs[i]))
            {
                StringBuilder message = new($"{this}: {Describe(i)} logged\n");
                message.AppendJoin("\n", logs[i].Select(line => "    " + line));
                message.Append("\n  but the recording has\n");
                message.AppendJoin("\n", Logs[i].Select(line => "    " + line));
                return message.ToString();
            }
        }
        if (logs.Count > Logs.Count)
        {
            return $"{this}: the log goes on past the recording's last input";
        }
        return null;
    }

    string Describe(int logIndex) => logIndex == 0 ? "the start" : $"input {logIndex} {Inputs[logIndex - 1]}";
}
