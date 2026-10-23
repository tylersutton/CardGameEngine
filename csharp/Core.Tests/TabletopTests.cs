using System.Numerics;
using System.Text.Json;
using Core;
using Editing;

/// Exporting to Tabletop Simulator: each zone's starting cards are drawn on sheets of up to 69 different faces, and
/// the save lays out the table once the game's start has run: piles as decks, other cards one by one, and private
/// zones as hands.
public class TabletopTests
{
    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    /// The table after the game's start, every card placed at its zone's anchor as if the game's table had put it there.
    static List<JsonElement> ObjectsOf(Game game)
    {
        List<TabletopDeck> decks = TabletopDeck.Of(game);
        game.Start();
        string save = TabletopSave.Text(game, decks, card => (game.ZoneOf(card).Anchor * TabletopSave.TableSize, 0),
            (deck, sheet) => $"face {deck} {sheet}", deck => $"back {deck}");
        return JsonDocument.Parse(save).RootElement.GetProperty("ObjectStates").EnumerateArray().ToList();
    }

    static string NameOf(JsonElement element) => element.GetProperty("Name").GetString()!;

    static bool FaceUp(JsonElement element) => element.GetProperty("Transform").GetProperty("rotZ").GetInt32() == 0;

    [Fact]
    public void ADeckOfDifferentCardsFillsOneSheet()
    {
        TabletopDeck deck = Assert.Single(TabletopDeck.Of(new(files, "res://games/solitaire.jsonc")));
        Assert.Equal(52, deck.Faces.Count);
        Assert.Equal(1, deck.SheetCount);
        Assert.Equal((10, 6), TabletopDeck.Grid(52));
    }

    [Fact]
    public void PilesAreDecksAndOtherCardsLieOneByOne()
    {
        List<JsonElement> objects = ObjectsOf(new(files, "res://games/solitaire.jsonc"));
        JsonElement stock = Assert.Single(objects, element => NameOf(element) == "Deck");
        Assert.Equal(24, stock.GetProperty("ContainedObjects").GetArrayLength());
        Assert.False(FaceUp(stock));
        Assert.Equal("face 0 0", stock.GetProperty("CustomDeck").GetProperty("1").GetProperty("FaceURL").GetString());
        List<JsonElement> tableau = objects.Where(element => NameOf(element) == "Card").ToList();
        Assert.Equal(28, tableau.Count);
        Assert.Equal(7, tableau.Count(FaceUp));
    }

    [Fact]
    public void PrivateZonesAreHands()
    {
        List<JsonElement> objects = ObjectsOf(new(files, "res://games/hearts.jsonc"));
        List<JsonElement> hands = objects.Where(element => NameOf(element) == "HandTrigger").ToList();
        Assert.Equal(["White", "Red", "Blue", "Green"], hands.Select(hand => hand.GetProperty("FogColor").GetString()));
        // Each hand faces its player round the table, the one across too, though the game's screen keeps theirs upright.
        Assert.Equal([0, 90, 180, 270], hands.Select(hand => hand.GetProperty("Transform").GetProperty("rotY").GetDouble()));
        List<JsonElement> held = objects.Where(element => NameOf(element) == "Card" && element.GetProperty("Transform").GetProperty("posY").GetSingle() >= 4).ToList();
        Assert.Equal(52, held.Count);
        Assert.All(held, card => Assert.True(FaceUp(card)));
    }

    [Fact]
    public void AlikeCardsShareAFace()
    {
        Game game = new(files, "res://games/reversi.jsonc");
        TabletopDeck box = Assert.Single(TabletopDeck.Of(game));
        Assert.Single(box.Faces);
        Assert.Equal((2, 2), TabletopDeck.Grid(1));
        List<JsonElement> objects = ObjectsOf(game);
        Assert.Equal(4, objects.Count(element => NameOf(element) == "Card"));
        // Each player's pile of discs.
        List<JsonElement> piles = objects.Where(element => NameOf(element) == "Deck").ToList();
        Assert.Equal(2, piles.Count);
        Assert.All(piles.SelectMany(pile => pile.GetProperty("DeckIDs").EnumerateArray()), id => Assert.Equal(100, id.GetInt32()));
    }

    [Fact]
    public void ManyFacesTakeSeveralSheets()
    {
        string rules = """{ "zones": { "deck": { "layout": "pile", "cards": "res://decks/many.csv" } } }""";
        string deck = "name\n" + string.Join("\n", Enumerable.Range(1, 100).Select(number => $"Card {number}"));
        Game game = new(new GameFiles(RepositoryFiles.PathOf(""), new() { ["res://many.jsonc"] = rules, ["res://decks/many.csv"] = deck }), "res://many.jsonc");
        Assert.Equal(2, TabletopDeck.Of(game)[0].SheetCount);
        JsonElement saved = Assert.Single(ObjectsOf(game));
        Assert.Equal("face 0 1", saved.GetProperty("CustomDeck").GetProperty("2").GetProperty("FaceURL").GetString());
        // The top card, the 100th, is the 31st on the second sheet.
        Assert.Equal(230, saved.GetProperty("DeckIDs")[0].GetInt32());
    }
}
