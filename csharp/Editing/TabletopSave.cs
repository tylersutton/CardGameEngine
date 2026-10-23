using System.Numerics;
using System.Text.Json;
using Core;

namespace Editing;

/// A Tabletop Simulator save file (JSON) of a game's table once its start has run: a pile is a deck, other zones'
/// cards lie where the game's table shows them, and each player's private zone is their hand. Cards are cut from the
/// starting decks' sheets of pictures, at the given addresses.
public static class TabletopSave
{
    /// The game's table in pixels (the project's window), which zone anchors are fractions of.
    public static readonly Vector2 TableSize = new(1440, 810);
    /// Table units per pixel: a 100-pixel card comes out about as wide as a Tabletop Simulator card.
    const float UnitsPerPixel = 0.022f;
    const float DropHeight = 1;
    /// Overlapping cards start this much above the one under them, so they settle in order instead of pushing apart.
    const float StackStep = 0.1f;
    /// Hands float above the table, as wide as a fanned hand and deep enough to hold a card held up.
    const float HandHeight = 4;
    static readonly Vector3 HandSize = new(800 * UnitsPerPixel, 5, 5);
    /// Tabletop Simulator's player colors, one per player in turn.
    static readonly string[] SeatColors = ["White", "Red", "Blue", "Green", "Yellow", "Orange", "Purple", "Pink", "Teal", "Brown"];

    /// placeOf(card) is where the game's table shows a card (its center in table pixels) and how far it's turned;
    /// faceUrl(deck, sheet) and backUrl(deck) say where each picture is, decks numbered as in the list.
    public static string Text(Game game, List<TabletopDeck> decks, Func<Card, (Vector2 Center, float Degrees)> placeOf,
        Func<int, int, string> faceUrl, Func<int, string> backUrl)
    {
        Dictionary<string, object?> sheets = [];
        Dictionary<Card, (string Sheet, int Id)> pictures = [];
        for (int deckNumber = 0; deckNumber < decks.Count; deckNumber++)
        {
            TabletopDeck deck = decks[deckNumber];
            int firstKey = sheets.Count + 1;
            for (int sheet = 0; sheet < deck.SheetCount; sheet++)
            {
                (int columns, int rows) = TabletopDeck.Grid(deck.SheetFaces(sheet).Count);
                sheets[(firstKey + sheet).ToString()] = new Dictionary<string, object?>
                {
                    ["FaceURL"] = faceUrl(deckNumber, sheet), ["BackURL"] = backUrl(deckNumber), ["NumWidth"] = columns, ["NumHeight"] = rows,
                    ["BackIsHidden"] = true, ["UniqueBack"] = false, ["Type"] = 0,
                };
            }
            for (int index = 0; index < deck.Cards.Count; index++)
            {
                int face = deck.FaceOf[index];
                int key = firstKey + face / TabletopDeck.FacesPerSheet;
                pictures[deck.Cards[index]] = (key.ToString(), key * 100 + face % TabletopDeck.FacesPerSheet);
            }
        }
        int objectCount = 0;
        string NewGuid() => (++objectCount).ToString("x6");
        Dictionary<string, object?> CardObject(Card card, Dictionary<string, object?> transform) => new()
        {
            ["GUID"] = NewGuid(), ["Name"] = "Card", ["Transform"] = transform, ["Nickname"] = card.Title, ["CardID"] = pictures[card].Id,
            ["CustomDeck"] = new Dictionary<string, object?> { [pictures[card].Sheet] = sheets[pictures[card].Sheet] },
        };
        List<object?> objects = [];
        foreach (Zone zone in game.Zones.Values)
        {
            bool hand = zone.Face == Zone.FacePrivate && zone.Owner > 0;
            float extraTurn = TurnAtTable(game, zone);
            if (hand)
            {
                objects.Add(Hand(zone, extraTurn, NewGuid()));
            }
            if (zone.IsEmpty)
            {
                continue;
            }
            if (zone.Layout == "pile" && !hand)
            {
                Dictionary<string, object?> transform = Transform(CenterOf(zone), (float)zone.Rotation + extraTurn, DropHeight, zone.Cards[^1].FaceUp);
                // Tabletop Simulator lists a deck from the top card down.
                List<Dictionary<string, object?>> cards = zone.Cards.AsEnumerable().Reverse().Select(card => CardObject(card, transform)).ToList();
                objects.Add(cards.Count == 1 ? cards[0] : new Dictionary<string, object?>
                {
                    ["GUID"] = NewGuid(), ["Name"] = "Deck", ["Transform"] = transform, ["Nickname"] = zone.Name,
                    ["DeckIDs"] = cards.Select(card => card["CardID"]).ToList(),
                    ["CustomDeck"] = zone.Cards.Select(card => pictures[card].Sheet).Distinct().ToDictionary(key => key, key => sheets[key]),
                    ["ContainedObjects"] = cards,
                });
                continue;
            }
            for (int index = 0; index < zone.Cards.Count; index++)
            {
                (Vector2 center, float degrees) = placeOf(zone.Cards[index]);
                Vector2 turned = CenterOf(zone) + Vector2.Transform(center - CenterOf(zone), Matrix3x2.CreateRotation(extraTurn * MathF.PI / 180));
                float height = (hand ? HandHeight : DropHeight) + index * StackStep;
                objects.Add(CardObject(zone.Cards[index], Transform(turned, degrees + extraTurn, height, hand || zone.Cards[index].FaceUp)));
            }
        }
        Dictionary<string, object?> save = new()
        {
            ["SaveName"] = game.Rules.Title, ["GameMode"] = game.Rules.Title, ["Date"] = "", ["VersionNumber"] = "", ["GameType"] = "", ["GameComplexity"] = "",
            ["Tags"] = new List<object?>(), ["Gravity"] = 0.5, ["PlayArea"] = 0.5, ["Table"] = "Table_RPG", ["Sky"] = "Sky_Museum",
            ["Note"] = game.Rules.Description, ["Rules"] = "", ["XmlUI"] = "", ["LuaScript"] = "", ["LuaScriptState"] = "", ["ObjectStates"] = objects,
        };
        return JsonSerializer.Serialize(save, new JsonSerializerOptions { WriteIndented = true });
    }

    /// How much further a player's zone turns at Tabletop Simulator's table than on the game's screen. The screen
    /// keeps the seat across the table upright, to be read from the near side; at the table, everyone's cards face them.
    static float TurnAtTable(Game game, Zone zone)
    {
        Dictionary<string, object?> definition = (Dictionary<string, object?>)game.Rules.Zones[zone.Kind]!;
        if (zone.Owner < 2 || definition.ContainsKey("anchors"))
        {
            return 0;
        }
        return (float)(360.0 * (zone.Owner - 1) / game.Rules.Players - Zone.SeatTurn(zone.Owner, game.Rules.Players));
    }

    /// The player's hand: an area of their color floating over their private zone, which holds the cards in it.
    static Dictionary<string, object?> Hand(Zone zone, float extraTurn, string guid)
    {
        Dictionary<string, object?> transform = Transform(CenterOf(zone), (float)zone.Rotation + extraTurn, HandHeight, true);
        // Cards are turned to face their player; a hand area isn't, so seat 1's has no turn at all.
        transform["rotY"] = zone.Rotation + extraTurn;
        transform["scaleX"] = HandSize.X;
        transform["scaleY"] = HandSize.Y;
        transform["scaleZ"] = HandSize.Z;
        return new()
        {
            ["GUID"] = guid, ["Name"] = "HandTrigger", ["Transform"] = transform, ["Nickname"] = zone.Name, ["Locked"] = true,
            ["FogColor"] = SeatColors[(zone.Owner - 1) % SeatColors.Length],
        };
    }

    /// A zone's place on the game's table. One the game keeps off its table (cards out of play) goes at its edge,
    /// so the cards don't fall off Tabletop Simulator's.
    static Vector2 CenterOf(Zone zone) => Vector2.Clamp(zone.Anchor, Vector2.Zero, Vector2.One) * TableSize + zone.Offset;

    /// A place on the game's table (in pixels, turned clockwise by degrees) on Tabletop Simulator's: seat 1, at the
    /// bottom of the game's table, sits on its near side.
    static Dictionary<string, object?> Transform(Vector2 center, float degrees, float height, bool faceUp)
    {
        Vector2 place = (center - TableSize / 2) * UnitsPerPixel;
        return new()
        {
            ["posX"] = place.X, ["posY"] = height, ["posZ"] = -place.Y, ["rotX"] = 0, ["rotY"] = 180 + degrees, ["rotZ"] = faceUp ? 0 : 180,
            ["scaleX"] = 1, ["scaleY"] = 1, ["scaleZ"] = 1,
        };
    }
}
