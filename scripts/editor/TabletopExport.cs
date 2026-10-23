using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Core;
using Editing;
using Godot;

/// Exports a game to Tabletop Simulator: draws each starting deck's card sheets and back with the game's own card
/// faces, runs the game's start, and writes a save of the table as the game shows it then, which Tabletop Simulator
/// lists under Saves, with the pictures beside it on this computer.
public partial class TabletopExport : Node
{
    public const string SavesFolder = "My Games/Tabletop Simulator/Saves/Card Game Prototypes";
    /// Picture pixels per pixel of the game's table: a sheet 10 cards across stays under 4096 pixels.
    const int Scale = 4;

    /// Writes the save and its pictures (replacing an earlier export of the game) and gives the save's path. The game
    /// mustn't have started: the export starts it, so each export is a new deal.
    public async Task<string> Export(Game game)
    {
        string name = string.Concat(game.Rules.Title.Split(Path.GetInvalidFileNameChars()));
        string folder = Path.Combine(OS.GetSystemDir(OS.SystemDir.Documents), SavesFolder);
        string pictures = Path.Combine(folder, name + " pictures");
        if (Directory.Exists(pictures))
        {
            Directory.Delete(pictures, true);
        }
        Directory.CreateDirectory(pictures);
        ImageCache images = new(game.Files);
        List<TabletopDeck> decks = TabletopDeck.Of(game);
        Dictionary<(int, int), string> faceUrls = [];
        Dictionary<int, string> backUrls = [];
        for (int deckNumber = 0; deckNumber < decks.Count; deckNumber++)
        {
            TabletopDeck deck = decks[deckNumber];
            for (int sheet = 0; sheet < deck.SheetCount; sheet++)
            {
                List<Card> faces = deck.SheetFaces(sheet);
                Image picture = await Draw(game, images, faces, TabletopDeck.Grid(faces.Count), true);
                faceUrls[(deckNumber, sheet)] = Save(picture, pictures, $"{deck.Zone.Name}_{sheet + 1}");
            }
            Image back = await Draw(game, images, [deck.Faces[0]], (1, 1), false);
            backUrls[deckNumber] = Save(back, pictures, $"{deck.Zone.Name}_back");
        }
        game.Start();
        Dictionary<Card, (System.Numerics.Vector2, float)> places = PlacesOnTable(game);
        string savePath = Path.Combine(folder, name + ".json");
        File.WriteAllText(savePath, TabletopSave.Text(game, decks, card => places[card], (deck, sheet) => faceUrls[(deck, sheet)], deck => backUrls[deck]));
        return savePath;
    }

    /// Where the game's own table (unseen, at its usual size) puts each card: its center and how far it's turned.
    Dictionary<Card, (System.Numerics.Vector2, float)> PlacesOnTable(Game game)
    {
        TableView table = new() { Visible = false };
        AddChild(table);
        table.Size = new(TabletopSave.TableSize.X, TabletopSave.TableSize.Y);
        table.ShowGame(game);
        Dictionary<Card, (System.Numerics.Vector2, float)> places = table.CardViews.ToDictionary(pair => pair.Key, pair =>
        {
            Vector2 center = pair.Value.Destination.Position + pair.Value.Size / 2;
            return (new System.Numerics.Vector2(center.X, center.Y), Mathf.RadToDeg(pair.Value.Destination.Rotation));
        });
        table.QueueFree();
        return places;
    }

    /// The cards drawn in a grid, face up or down, with white behind their rounded corners.
    async Task<Image> Draw(Game game, ImageCache images, List<Card> cards, (int Columns, int Rows) grid, bool faceUp)
    {
        Vector2I tableSize = new((int)CardView.CardSize.X * grid.Columns, (int)CardView.CardSize.Y * grid.Rows);
        SubViewport viewport = new()
        {
            Size = tableSize * Scale, Size2DOverride = tableSize, Size2DOverrideStretch = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewport.AddChild(new ColorRect { Color = Colors.White, Size = tableSize });
        for (int index = 0; index < cards.Count; index++)
        {
            CardView view = new(cards[index], game.Rules.CardFace, images) { FaceUp = faceUp };
            Vector2 slot = new(index % grid.Columns * CardView.CardSize.X, index / grid.Columns * CardView.CardSize.Y);
            view.Position = slot + (CardView.CardSize - view.Size) / 2;
            viewport.AddChild(view);
        }
        AddChild(viewport);
        // The first frame draws the cards; the picture is ready once the next has been drawn.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image picture = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        return picture;
    }

    /// Saves the picture and gives its address. The name ends with a hash of the picture, as Tabletop Simulator
    /// keeps pictures by address: a changed picture needs a new one. The address is the plain path, spaces and all,
    /// since Tabletop Simulator doesn't decode %20 in file addresses.
    static string Save(Image picture, string folder, string name)
    {
        byte[] png = picture.SavePngToBuffer();
        string path = Path.Combine(folder, $"{name}_{Convert.ToHexString(SHA256.HashData(png))[..8].ToLowerInvariant()}.png");
        File.WriteAllBytes(path, png);
        return "file:///" + path.Replace('\\', '/');
    }
}
