using Core;
using Editing;

/// tests/editor_test.gd: GameDocument on every game (opening keeps the text exactly, edits change only what they
/// should, adding then removing things leaves the file as it was, comments and all), the recipes, and the turn
/// summary.
public class EditingTests
{
    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    public static TheoryData<string> Games()
    {
        TheoryData<string> games = [];
        foreach (string path in files.GamePaths())
        {
            games.Add(path);
        }
        return games;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void GameFilesRoundTrip(string path)
    {
        string original = files.Read(path);
        GameDocument document = GameDocument.Open(files, path);
        Assert.Equal("", document.ParseError);
        Assert.Equal(original, document.Text);
        Assert.True(Values.DeepEquals(GameRules.Parse(original), document.Data), "reads differently from the engine");
        // Every value set to itself leaves the text alone.
        foreach (string zone in ((Dictionary<string, object?>)document.GetValue(["zones"], new Dictionary<string, object?>())!).Keys)
        {
            document.SetValue(["zones", zone], document.GetValue(["zones", zone]));
        }
        Assert.Equal(original, document.Text);
        // Adding then removing a zone, a turn step and a trigger leaves the file as it was.
        document.SetValue(["zones", "test_zone"], new Dictionary<string, object?> { ["layout"] = "pile", ["anchor"] = new List<object?> { 0.5, 0.5 } });
        Assert.Equal("pile", document.GetValue(["zones", "test_zone", "layout"]));
        document.Remove(["zones", "test_zone"]);
        AssertSameText(original, document.Text, "add + remove zone");
        if (document.GetValue(["turn"]) is List<object?> turn)
        {
            int count = turn.Count;
            document.Insert(["turn"], count, "end_turn()");
            Assert.Equal(count + 1, ((List<object?>)document.GetValue(["turn"])!).Count);
            document.Remove(["turn", count]);
            AssertSameText(original, document.Text, "add + remove turn step");
        }
        document.SetValue(["triggers", "test_trigger"], new List<object?> { "shuffle(deck)" });
        document.Remove(["triggers", "test_trigger"]);
        AssertSameText(original, document.Text, "add + remove trigger");
    }

    /// Builds the game again from an empty file, one setting at a time as the editor's forms do, and checks the result
    /// holds the same game and loads cleanly.
    [Theory]
    [MemberData(nameof(Games))]
    public void GamesRebuildFromAnEmptyFile(string path)
    {
        object? original = GameRules.Parse(files.Read(path));
        GameDocument document = new() { Path = path };
        document.SetText("{\n}\n");
        SetLeaves(document, [], original);
        Assert.Equal(GameDocument.Format(original), GameDocument.Format(document.Data));
        Game game = new(new(RepositoryFiles.PathOf(""), new() { [path] = document.Text }), path, 1);
        Assert.Empty(game.Errors);
    }

    /// Sets every value that isn't an object, depth first, so objects and lists are made along the way.
    static void SetLeaves(GameDocument document, List<object> at, object? value)
    {
        if (value is Dictionary<string, object?> { Count: > 0 } members)
        {
            foreach ((string key, object? member) in members)
            {
                SetLeaves(document, [.. at, key], member);
            }
        }
        else
        {
            document.SetValue(at, value);
        }
    }

    [Fact]
    public void EditsKeepCommentsAndLayout()
    {
        GameDocument document = new() { Path = "editor_test.jsonc" };
        document.SetText("{\n\t// The table\n\t\"zones\": {\n\t\t\"deck\": {\n\t\t\t\"label\": \"Deck\", // the stock\n\t\t\t\"anchor\": [\n\t\t\t\t0.4,\n\t\t\t\t0.5\n\t\t\t]\n\t\t},\n\t\t\"hand\": {}\n\t},\n\t\"moves\": [\n\t\t{\n\t\t\t\"from\": \"deck\",\n\t\t\t\"to\": \"hand\"\n\t\t}\n\t]\n}");
        string start = document.Text;
        document.SetValue(["zones", "deck", "anchor"], new List<object?> { 0.25, 0.75 });
        Assert.Contains("\"label\": \"Deck\", // the stock\n\t\t\t\"anchor\": [\n\t\t\t\t0.25,\n\t\t\t\t0.75\n\t\t\t]\n", document.Text);
        document.SetValue(["zones", "hand", "layout"], "fan");
        Assert.Contains("\"hand\": {\n\t\t\t\"layout\": \"fan\"\n\t\t}", document.Text);
        document.Remove(["zones", "hand", "layout"]);
        Assert.Contains("\"hand\": {}", document.Text);
        document.SetValue(["zones", "hand", "layout"], "fan");
        document.SetValue(["title"], "Test");
        Assert.Equal("Test", document.GetValue(["title"]));
        Assert.Contains("\"title\": \"Test\"", document.Text);
        document.RenameKey(["zones", "hand"], "cards");
        Assert.True(document.Has(["zones", "cards", "layout"]));
        Assert.Equal("The table", document.Note(["zones"]));
        document.SetNote(["zones"], "Where cards go");
        Assert.Contains("\t// Where cards go\n\t\"zones\"", document.Text);
        document.Insert(["moves"], 0, new Dictionary<string, object?> { ["from"] = "hand", ["to"] = "deck" });
        Assert.Equal("hand", document.GetValue(["moves", 0, "from"]));
        Assert.Equal("deck", document.GetValue(["moves", 1, "from"]));
        document.SetValue(["moves", 1, "phase"], "draw");
        Assert.Contains("\"to\": \"hand\",\n\t\t\t\"phase\": \"draw\"\n\t\t}", document.Text);
        document.SetValue(["zones", "deck", "label"], "Pioche ♠");
        Assert.Contains("\"Pioche ♠\"", document.Text);
        Dictionary<string, object?> nested = new()
        {
            ["a"] = "text", ["b"] = new List<object?>(), ["c"] = new List<object?> { 1L, new Dictionary<string, object?> { ["d"] = true } },
        };
        document.SetValue(["zones", "nested"], nested);
        Assert.Contains("\"nested\": {\n\t\t\t\"a\": \"text\",\n\t\t\t\"b\": [],\n\t\t\t\"c\": [\n\t\t\t\t1,\n\t\t\t\t{\n\t\t\t\t\t\"d\": true\n\t\t\t\t}\n\t\t\t]\n\t\t}", document.Text);
        Assert.Equal(GameDocument.Format(nested), GameDocument.Format(document.GetValue(["zones", "nested"])));
        Assert.NotEqual(start, document.Text);
    }

    /// Every recipe added in turn to an empty game makes a game that loads cleanly; adding them again changes nothing.
    [Fact]
    public void RecipesAddUpToAGame()
    {
        GameDocument document = new() { Path = "res://games/recipe_test.jsonc" };
        document.SetText("{\n}\n");
        List<string> recipes = Recipes.Paths(files);
        Assert.NotEmpty(recipes);
        foreach (string recipe in recipes)
        {
            Recipes.AddTo(document, files, recipe);
            Assert.Equal("", document.ParseError);
            Assert.NotEmpty(Recipes.Description(files, recipe));
        }
        string allAdded = document.Text;
        foreach (string recipe in recipes)
        {
            Recipes.AddTo(document, files, recipe);
        }
        AssertSameText(allAdded, document.Text, "adding the recipes twice");
        Game game = new(new(RepositoryFiles.PathOf(""), new() { [document.Path] = document.Text }), document.Path, 1);
        Assert.Empty(game.Errors);
    }

    /// The Turn section's summary spells out Sea Salt's offers, and the actions possible any time.
    [Fact]
    public void TurnSummarySpellsOutOffers()
    {
        string text = string.Join("\n", TurnSummary.Lines(GameRules.Load(files, "res://games/sea_salt_+_paper.jsonc")));
        Assert.Contains("Offer Draw two (click deck) or Take a discard (drag discard_piles → hand or click it)", text);
        Assert.Contains("Offer Play duo (button) again and again, until End turn (button) or Stop (button) or Last chance (button)", text);
        Assert.Contains("If extra_turn and ending == 'End turn':", text);
        Assert.Contains("Any time: Sort hand (button)", text);
        Assert.StartsWith("No turn", TurnSummary.Lines(GameRules.Load(files, "res://games/solitaire.jsonc"))[0]);
    }

    [Fact]
    public void BuildingBlockHelpComesFromTheReference()
    {
        BlockHelp help = new(files.Read(BlockHelp.Reference));
        Assert.NotEmpty(help.Entries);
        Assert.Contains("draw", help.ByName.Keys);
        Game game = new(files, "res://games/solitaire.jsonc");
        Assert.All(help.ByName.Keys, block => Assert.True(game.Blocks.Has(block), $"the reference lists {block}, which isn't a building block"));
    }

    /// The first differing line of two texts, for failure messages.
    static void AssertSameText(string expected, string actual, string what)
    {
        string[] a = expected.Split('\n');
        string[] b = actual.Split('\n');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            Assert.True(a[i] == b[i], $"{what} changed line {i + 1}:\n    was: {a[i]}\n    now: {b[i]}");
        }
        Assert.True(a.Length == b.Length, $"{what}: lines {a.Length} vs {b.Length}");
    }
}
