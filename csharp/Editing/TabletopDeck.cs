using Core;

namespace Editing;

/// The cards a zone holds before the game starts, as Tabletop Simulator draws them: their different faces (cards
/// alike in every column share one) on sheets of up to 10 by 7 with one back, and which face each card shows. The
/// cards keep their faces wherever the game's start puts them.
public class TabletopDeck
{
    public const int SheetColumns = 10;
    public const int SheetRows = 7;
    // Tabletop Simulator keeps a sheet's last space for the picture of a hidden card.
    public const int FacesPerSheet = SheetColumns * SheetRows - 1;

    public Zone Zone { get; }
    /// The zone's cards before the game starts, bottom to top.
    public List<Card> Cards { get; }
    public List<Card> Faces { get; } = [];
    /// For each of Cards, its index in Faces.
    public List<int> FaceOf { get; } = [];

    public TabletopDeck(Zone zone)
    {
        Zone = zone;
        Cards = [.. zone.Cards];
        Dictionary<string, int> known = [];
        foreach (Card card in Cards)
        {
            string columns = string.Join("\n", card.Data.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
            if (!known.TryGetValue(columns, out int face))
            {
                face = known[columns] = Faces.Count;
                Faces.Add(card);
            }
            FaceOf.Add(face);
        }
    }

    /// Every zone holding cards in a game that hasn't started yet.
    public static List<TabletopDeck> Of(Game game) => game.Zones.Values.Where(zone => !zone.IsEmpty).Select(zone => new TabletopDeck(zone)).ToList();

    public int SheetCount => (Faces.Count + FacesPerSheet - 1) / FacesPerSheet;

    public List<Card> SheetFaces(int sheet) => Faces.Skip(sheet * FacesPerSheet).Take(FacesPerSheet).ToList();

    /// A sheet's columns and rows: as few as hold its faces, but at least 2 by 2, the smallest Tabletop Simulator takes.
    public static (int Columns, int Rows) Grid(int faces)
    {
        int columns = Math.Clamp(faces, 2, SheetColumns);
        return (columns, Math.Max(2, (faces + columns - 1) / columns));
    }
}
