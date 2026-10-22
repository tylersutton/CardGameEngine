using System.Text.Json;
using Core;

/// Images: card_face and boards name image files in images/, the game lists every one it shows (an online host sends
/// them), and a missing one is a load error.
public class ImageTests
{
    const string Rules = """
        {
            "zones": {
                "deck": {
                    "layout": "pile",
                    "cards": "res://decks/standard_deck.csv",
                    "columns": { "art": "'test_cards/' + card.rank + '_' + card.suit + '.png'" }
                },
                "square": { "board": { "columns": 2, "rows": 2, "image": "boards/plain.svg" } }
            },
            "card_face": { "image": "art", "card_back": "back.png", "back": "#ffffff" }
        }
        """;

    const string Path = "res://image_test.jsonc";

    static Game GameWith(Dictionary<string, byte[]> images, string changes = "{}")
    {
        Dictionary<string, object?> merged = (Dictionary<string, object?>)GameRules.Parse(Rules)!;
        foreach ((string key, object? value) in (Dictionary<string, object?>)GameRules.Parse(changes)!)
        {
            merged[key] = value;
        }
        GameFiles files = new(RepositoryFiles.PathOf(""), new() { [Path] = JsonSerializer.Serialize(merged) }, images);
        return new(files, Path, 1);
    }

    /// Every image the test game uses, as if a host had sent them.
    static Dictionary<string, byte[]> AllImages() => new[] { "Spades", "Hearts", "Diamonds", "Clubs" }
        .SelectMany(suit => new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }.Select(rank => $"res://images/test_cards/{rank}_{suit}.png"))
        .Concat(["res://images/back.png", "res://images/boards/plain.svg"])
        .ToDictionary(path => path, _ => new byte[] { 1 });

    [Fact]
    public void TheGameListsEveryImageItShows()
    {
        Game game = GameWith(AllImages());
        Assert.Empty(game.Errors);
        List<string> paths = game.ImagePaths();
        Assert.Equal(54, paths.Count);
        Assert.Contains("res://images/test_cards/A_Spades.png", paths);
        Assert.Contains("res://images/back.png", paths);
        Assert.Contains("res://images/boards/plain.svg", paths);
        Assert.DoesNotContain(paths, path => path.Contains("#ffffff"));
    }

    [Fact]
    public void MissingImagesAreLoadErrors()
    {
        Dictionary<string, byte[]> images = AllImages();
        images.Remove("res://images/test_cards/Q_Hearts.png");
        Assert.Equal(["Image not found: res://images/test_cards/Q_Hearts.png"], GameWith(images).Errors);
    }

    [Fact]
    public void ImageNamesOnlyImageFiles()
    {
        Game game = GameWith(AllImages(), """{ "card_face": { "image": "suit" } }""");
        Assert.Equal(4, game.Errors.Count);
        Assert.Contains("card_face: \"image\" gives \"Spades\", which isn't an image file (.png, .jpg, .jpeg, .webp, .svg)", game.Errors);
    }

    [Fact]
    public void LayoutIsFullOrArt()
    {
        Assert.Empty(GameWith(AllImages(), """{ "card_face": { "image": "art", "layout": "art" } }""").Errors);
        Assert.Contains("card_face: \"layout\" should be full or art", GameWith(AllImages(), """{ "card_face": { "layout": "poster" } }""").Errors);
    }

    [Fact]
    public void ImagePathsAreInTheImagesFolder()
    {
        Assert.Equal("res://images/moat.png", GameFiles.ImagePath("moat.png"));
        Assert.Equal("res://art/moat.png", GameFiles.ImagePath("res://art/moat.png"));
        Assert.True(GameFiles.IsImage("cards/A_Spades.PNG"));
        Assert.False(GameFiles.IsImage("#c8443a"));
        Assert.False(GameFiles.IsImage("color"));
    }
}
