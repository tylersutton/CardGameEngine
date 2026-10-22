/// The recorded matches load, and FirstLogDifference finds where a log goes wrong. Once Game is ported, a test
/// replays every match and checks its log with FirstLogDifference.
public class ReferenceMatchTests
{
    [Fact]
    public void EveryGameHasCompleteRecordings()
    {
        List<ReferenceMatch> matches = ReferenceMatch.All();
        Assert.Contains(matches, match => match.Game == "solitaire");
        Assert.All(matches, match =>
        {
            Assert.Equal(match.Inputs.Count + 1, match.Logs.Count);
            Assert.NotEmpty(match.End.Zones);
        });
    }

    [Fact]
    public void TheSameLogHasNoDifference()
    {
        ReferenceMatch match = Solitaire();
        Assert.Null(match.FirstLogDifference(match.Logs));
    }

    [Fact]
    public void ADifferentLineNamesTheInputAndBothLogs()
    {
        ReferenceMatch match = Solitaire();
        List<List<string>> logs = match.Logs.Select(lines => lines.ToList()).ToList();
        logs[3] = ["something else"];
        string difference = match.FirstLogDifference(logs)!;
        Assert.StartsWith("solitaire seed 1: input 3 {", difference);
        Assert.Contains("    something else", difference);
        Assert.Contains("    " + match.Logs[3][0], difference);
    }

    [Fact]
    public void AShortLogSaysWhereItStopped()
    {
        ReferenceMatch match = Solitaire();
        string difference = match.FirstLogDifference(match.Logs.Take(5).ToList())!;
        Assert.Contains("the log stops after input 4", difference);
    }

    static ReferenceMatch Solitaire() => ReferenceMatch.All().First(match => match.Game == "solitaire" && match.Seed == 1);
}
