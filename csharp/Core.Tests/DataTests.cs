using Core;

/// Edge cases of reading numbers, decks and game files that the recorded games don't reach. The expected values
/// are what Godot gives for the same input.
public class DataTests
{
    [Theory]
    [InlineData("", false, true, 0L)]
    [InlineData("7", true, true, 7L)]
    [InlineData("-3", true, true, -3L)]
    [InlineData("+5", true, false, 5L)]
    [InlineData("07", true, false, 7L)]
    [InlineData("2.5", true, true, 2L)]
    [InlineData("2.50", true, false, 2L)]
    [InlineData("2.0", true, true, 2L)]
    [InlineData(".5", true, false, 0L)]
    [InlineData("5.", true, false, 5L)]
    [InlineData("1e5", true, false, 15L)]
    [InlineData("1e+5", true, false, 15L)]
    [InlineData("e5", false, false, 5L)]
    [InlineData("1.2.3", false, false, 1L)]
    [InlineData("abc", false, false, 0L)]
    [InlineData("1-2", false, false, 12L)]
    [InlineData("-", false, false, 0L)]
    [InlineData("3e", true, false, 3L)]
    [InlineData("--3", false, false, 3L)]
    [InlineData("-x7", false, false, -7L)]
    [InlineData("a1b2c3", false, false, 123L)]
    public void NumbersInTextReadAsInGodot(string text, bool validFloat, bool deckNumber, long whole)
    {
        Assert.Equal(validFloat, Values.IsValidFloat(text));
        Assert.Equal(deckNumber, Card.IsNumber(text));
        Assert.Equal(whole, Values.ToInt(text));
    }

    [Fact]
    public void TextComparesByUnicodeNumber()
    {
        // 🂡 (U+1F0A1) comes after � (U+FFFD), though .NET's ordinal order, by its two 16-bit halves, puts it first.
        Assert.True(Values.CompareText("�", "🂡") < 0);
        Assert.True(string.CompareOrdinal("�", "🂡") > 0);
        Assert.True(Values.CompareText("A", "a") < 0);
        Assert.True(Values.CompareText("ab", "abc") < 0);
        Assert.Equal(0, Values.CompareText("10♥", "10♥"));
    }

    [Fact]
    public void CsvValuesMayHoldCommasQuotesAndLineBreaks()
    {
        List<List<string>> rows = DeckLoader.Rows("name,text\r\nA,\"one, two\"\nB,\"say \"\"hi\"\"\"\nC,\"line\nbreak\"\n\n");
        Assert.Equal([["name", "text"], ["A", "one, two"], ["B", "say \"hi\""], ["C", "line\nbreak"]], rows);
        Assert.Equal(rows, DeckLoader.Rows(DeckLoader.ToCsv(rows)));
    }

    [Fact]
    public void CountGivesCopiesAndBlankRowsAreSkipped()
    {
        List<Card> cards = DeckLoader.Load("Count,Kind,Points\n2,Fish,1\n,,\n1,Crab,x\n0,Shell,3\n");
        Assert.Equal(["Fish", "Fish", "Crab"], cards.Select(card => card.Title));
        // No "name" column, and the first is the count, so cards are named by the second.
        Assert.Equal("Kind", cards[0].TitleColumn);
        Assert.Empty(cards[0].NumberColumns);
        Assert.NotSame(cards[0].Data, cards[1].Data);
    }

    [Fact]
    public void NumberColumnsReadAsNumbers()
    {
        Card card = DeckLoader.Load("name,cost,weight,rank\nGold,6,1.5,K\n")[0];
        Assert.True(card.TryRead("Cost", out object? cost));
        Assert.Equal(6L, cost);
        Assert.True(card.TryRead("weight", out object? weight));
        Assert.Equal(1.5, weight);
        Assert.True(card.TryRead("rank", out object? rank));
        Assert.Equal("K", rank);
        Assert.False(card.TryRead("colour", out _));
    }

    [Fact]
    public void ConditionNamesMayTakeParameters()
    {
        GameRules rules = Load("{\"conditions\": {\"ok(a, b)\": \"1\", \"bad(\": \"1\", \"two words\": \"1\", \"p(a,,b)\": \"1\", \"q( a , )\": \"1\"}, \"turn\": 5, \"extra\": 1}");
        Assert.Equal(["ok", "p"], rules.Conditions.Keys);
        Assert.Equal(["a", "b"], rules.ConditionParameters["p"]);
        Assert.Equal(3, rules.ConditionErrors.Count);
        Assert.Contains("\"q( a , )\"", rules.ConditionErrors[2]);
        Assert.Equal(["extra", "turn"], rules.UnknownKeys);
    }

    [Fact]
    public void SingleStepsBecomeLists()
    {
        GameRules rules = Load("{\"actions\": {\"Pass\": \"end_turn()\", \"Draw\": {\"steps\": {\"if\": \"x\", \"then\": \"y()\"}}}, \"triggers\": {\"start\": \"deal()\"}}");
        Assert.Equal(new List<object?> { "end_turn()" }, rules.Actions["Pass"]["steps"]);
        Dictionary<string, object?> step = (Dictionary<string, object?>)((List<object?>)rules.Actions["Draw"]["steps"]!)[0]!;
        Assert.Equal(new List<object?> { "y()" }, step["then"]);
        Assert.Equal(new List<object?> { "deal()" }, rules.Triggers["start"]);
    }

    [Fact]
    public void StartingValuesThatAreWholeBecomeWholeNumbers()
    {
        GameRules rules = Load("{\"variables\": {\"a\": 4, \"b\": 2.5, \"c\": [1, 2]}, \"constants\": {\"points\": [0, 2.5, 4]}}");
        Assert.Equal(4L, rules.Variables["a"]);
        Assert.Equal(2.5, rules.Variables["b"]);
        Assert.Equal(new List<object?> { 1.0, 2.0 }, rules.Variables["c"]);
        Assert.Equal(new List<object?> { 0L, 2.5, 4L }, rules.Constants["points"]);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        GameRules rules = Load("{\n// a comment\n\"title\": \"x\", /* another */\n\"players\": 2,\n}");
        Assert.Equal("", rules.Error);
        Assert.Equal(2, rules.Players);
    }

    [Fact]
    public void MistakesSayWhere()
    {
        Assert.StartsWith("x.jsonc, line 3: ", Load("{\n\"title\": \"x\",\n\"players\" 2\n}").Error);
        Assert.Equal("\"zones\" should be an object, { ... }", Load("{\"zones\": []}").Error);
        Assert.Equal("Game file not found: res://missing.jsonc", GameRules.Load(new("."), "res://missing.jsonc").Error);
    }

    [Theory]
    [InlineData("gin_rummy", "Gin Rummy")]
    [InlineData("sea_salt_+_paper", "Sea Salt + Paper")]
    [InlineData("myGame", "My Game")]
    [InlineData("HTMLParser", "Html Parser")]
    [InlineData("game2", "Game 2")]
    [InlineData("two__words", "Two Words")]
    [InlineData("Node2D", "Node 2d")]
    [InlineData("abc2def", "Abc 2 Def")]
    [InlineData("2game", "2 Game")]
    [InlineData("A1B", "A 1b")]
    [InlineData("x86_64", "X 86 64")]
    [InlineData("aB", "A B")]
    public void UntitledGamesAreNamedAfterTheirFile(string file, string title)
    {
        Assert.Equal(title, GameRules.Load(new(".", new() { [$"res://games/{file}.jsonc"] = "{}" }), $"res://games/{file}.jsonc").Title);
    }

    [Fact]
    public void GivenTextIsReadInsteadOfTheFile()
    {
        GameFiles files = new(RepositoryFiles.PathOf(""), new() { ["res://games/solitaire.jsonc"] = "{\"title\": \"Edited\"}" });
        Assert.Equal("Edited", GameRules.Load(files, "res://games/solitaire.jsonc").Title);
        Assert.Contains("res://games/solitaire.jsonc", files.GamePaths());
    }

    static GameRules Load(string text) => GameRules.Load(new(".", new() { ["res://x.jsonc"] = text }), "res://x.jsonc");
}
