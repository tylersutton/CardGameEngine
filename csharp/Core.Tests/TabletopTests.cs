using System.Text.Json;
using Core;
using Editing;

/// Exporting to Tabletop Simulator: each zone holding cards becomes a deck whose different faces are drawn on sheets
/// of up to 69, and the save file puts the decks where the zones are.
public class TabletopTests
{
    static readonly GameFiles files = new(RepositoryFiles.PathOf(""));

    static JsonElement SaveOf(Game game) => JsonDocument.Parse(TabletopSave.Text(game, TabletopDeck.Of(game),
        (deck, sheet) => $"face {deck} {sheet}", deck => $"back {deck}")).RootElement;

    [Fact]
    public void ADeckOfDifferentCardsFillsOneSheet()
    {
        Game game = new(files, "res://games/solitaire.jsonc");
        TabletopDeck deck = Assert.Single(TabletopDeck.Of(game));
        Assert.Equal("stock", deck.Zone.Name);
        Assert.Equal(52, deck.Faces.Count);
        Assert.Equal(1, deck.SheetCount);
        Assert.Equal((10, 6), TabletopDeck.Grid(52));
        JsonElement stock = SaveOf(game).GetProperty("ObjectStates")[0];
        Assert.Equal("DeckCustom", stock.GetProperty("Name").GetString());
        Assert.Equal(180, stock.GetProperty("Transform").GetProperty("rotZ").GetInt32());
        JsonElement sheet = stock.GetProperty("CustomDeck").GetProperty("1");
        Assert.Equal("face 0 0", sheet.GetProperty("FaceURL").GetString());
        Assert.Equal(6, sheet.GetProperty("NumHeight").GetInt32());
        // The top card (the zone's last) comes first.
        Assert.Equal(151, stock.GetProperty("DeckIDs")[0].GetInt32());
        Assert.Equal(52, stock.GetProperty("ContainedObjects").GetArrayLength());
    }

    [Fact]
    public void AlikeCardsShareAFace()
    {
        Game game = new(files, "res://games/reversi.jsonc");
        TabletopDeck supply = Assert.Single(TabletopDeck.Of(game));
        Assert.Single(supply.Faces);
        Assert.Equal(64, supply.FaceOf.Count);
        Assert.Equal((2, 2), TabletopDeck.Grid(1));
        Assert.All(SaveOf(game).GetProperty("ObjectStates")[0].GetProperty("DeckIDs").EnumerateArray(), id => Assert.Equal(100, id.GetInt32()));
    }

    [Fact]
    public void ManyFacesTakeSeveralSheets()
    {
        string rules = """{ "zones": { "deck": { "layout": "pile", "cards": "res://decks/many.csv" } } }""";
        string deck = "name\n" + string.Join("\n", Enumerable.Range(1, 100).Select(number => $"Card {number}"));
        Game game = new(new GameFiles(RepositoryFiles.PathOf(""), new() { ["res://many.jsonc"] = rules, ["res://decks/many.csv"] = deck }), "res://many.jsonc");
        Assert.Equal(2, TabletopDeck.Of(game)[0].SheetCount);
        JsonElement saved = SaveOf(game).GetProperty("ObjectStates")[0];
        Assert.Equal(2, saved.GetProperty("CustomDeck").EnumerateObject().Count());
        Assert.Equal("face 0 1", saved.GetProperty("CustomDeck").GetProperty("2").GetProperty("FaceURL").GetString());
        // The 100th card is the 31st on the second sheet.
        Assert.Equal(230, saved.GetProperty("DeckIDs")[0].GetInt32());
    }
}
