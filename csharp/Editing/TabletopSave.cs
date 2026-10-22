using System.Numerics;
using System.Text.Json;
using Core;

namespace Editing;

/// A Tabletop Simulator save file (JSON) of a game's table before it starts: each deck at its zone's place, face up
/// or down as the zone shows cards, its cards cut from sheets of pictures at the given addresses.
public static class TabletopSave
{
    /// The game's table in pixels (the project's window), which zone anchors are fractions of.
    static readonly Vector2 TableSize = new(1440, 810);
    /// Table units per pixel: a 100-pixel card comes out about as wide as a Tabletop Simulator card.
    const float UnitsPerPixel = 0.022f;
    const float DropHeight = 1;

    /// faceUrl(deck, sheet) and backUrl(deck) say where each picture is; decks are numbered as in the list.
    public static string Text(Game game, List<TabletopDeck> decks, Func<int, int, string> faceUrl, Func<int, string> backUrl)
    {
        List<object?> objects = [];
        int sheetKey = 0;
        for (int deckNumber = 0; deckNumber < decks.Count; deckNumber++)
        {
            TabletopDeck deck = decks[deckNumber];
            Dictionary<string, object?> sheets = [];
            for (int sheet = 0; sheet < deck.SheetCount; sheet++)
            {
                (int columns, int rows) = TabletopDeck.Grid(deck.SheetFaces(sheet).Count);
                sheets[(++sheetKey).ToString()] = new Dictionary<string, object?>
                {
                    ["FaceURL"] = faceUrl(deckNumber, sheet), ["BackURL"] = backUrl(deckNumber), ["NumWidth"] = columns, ["NumHeight"] = rows,
                    ["BackIsHidden"] = true, ["UniqueBack"] = false, ["Type"] = 0,
                };
            }
            int firstKey = sheetKey - deck.SheetCount + 1;
            Dictionary<string, object?> transform = TransformOf(deck.Zone);
            // Tabletop Simulator lists a deck from the top card down.
            List<Dictionary<string, object?>> cards = [];
            for (int index = deck.FaceOf.Count - 1; index >= 0; index--)
            {
                int face = deck.FaceOf[index];
                string key = (firstKey + face / TabletopDeck.FacesPerSheet).ToString();
                cards.Add(new()
                {
                    ["GUID"] = Guid(objects.Count, cards.Count + 1), ["Name"] = "Card", ["Transform"] = transform, ["Nickname"] = deck.Zone.Cards[index].Title,
                    ["CardID"] = int.Parse(key) * 100 + face % TabletopDeck.FacesPerSheet, ["CustomDeck"] = new Dictionary<string, object?> { [key] = sheets[key] },
                });
            }
            if (cards.Count == 1)
            {
                objects.Add(cards[0]);
                continue;
            }
            objects.Add(new Dictionary<string, object?>
            {
                ["GUID"] = Guid(objects.Count, 0), ["Name"] = "DeckCustom", ["Transform"] = transform, ["Nickname"] = deck.Zone.Name,
                ["DeckIDs"] = cards.Select(card => card["CardID"]).ToList(), ["CustomDeck"] = sheets, ["ContainedObjects"] = cards,
            });
        }
        Dictionary<string, object?> save = new()
        {
            ["SaveName"] = game.Rules.Title, ["GameMode"] = game.Rules.Title, ["Date"] = "", ["VersionNumber"] = "", ["GameType"] = "", ["GameComplexity"] = "",
            ["Tags"] = new List<object?>(), ["Gravity"] = 0.5, ["PlayArea"] = 0.5, ["Table"] = "Table_RPG", ["Sky"] = "Sky_Museum",
            ["Note"] = game.Rules.Description, ["Rules"] = "", ["XmlUI"] = "", ["LuaScript"] = "", ["LuaScriptState"] = "", ["ObjectStates"] = objects,
        };
        return JsonSerializer.Serialize(save, new JsonSerializerOptions { WriteIndented = true });
    }

    /// Where the zone sits on the table, turned as it is, face down if it shows cards face down. Seat 1 is at the
    /// bottom of the game's table and on the near side of Tabletop Simulator's.
    static Dictionary<string, object?> TransformOf(Zone zone)
    {
        Vector2 place = (zone.Anchor * TableSize + zone.Offset - TableSize / 2) * UnitsPerPixel;
        return new()
        {
            ["posX"] = place.X, ["posY"] = DropHeight, ["posZ"] = -place.Y, ["rotX"] = 0, ["rotY"] = 180 + zone.Rotation, ["rotZ"] = zone.Face == Zone.FaceDown ? 180 : 0,
            ["scaleX"] = 1, ["scaleY"] = 1, ["scaleZ"] = 1,
        };
    }

    /// Tabletop Simulator's object ids are six hex digits, unique in the save.
    static string Guid(int deck, int card) => $"{deck + 1:x2}{card:x4}";
}
