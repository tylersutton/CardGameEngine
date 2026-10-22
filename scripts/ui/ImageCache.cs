using Core;
using Godot;

/// The images a game shows, each loaded from the game's files the first time it's drawn. They're read from the files
/// themselves rather than imported by Godot, so the editor's unsaved games and an online host's game show them too.
public class ImageCache(GameFiles files)
{
    /// SVGs are drawn at twice their own size, so they stay sharp on a zoomed card.
    const float SvgScale = 2;

    readonly Dictionary<string, Texture2D?> loaded = [];

    /// The image a card face or board names ("moat.png"), or null if it isn't an image or can't be read.
    public Texture2D? Get(string name)
    {
        if (!GameFiles.IsImage(name))
        {
            return null;
        }
        string path = GameFiles.ImagePath(name);
        if (!loaded.TryGetValue(path, out Texture2D? texture))
        {
            loaded[path] = texture = Load(path);
        }
        return texture;
    }

    Texture2D? Load(string path)
    {
        if (!files.Exists(path))
        {
            return null;
        }
        byte[] bytes = files.ReadBytes(path);
        Image image = new();
        Error error = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => image.LoadPngFromBuffer(bytes),
            ".jpg" or ".jpeg" => image.LoadJpgFromBuffer(bytes),
            ".webp" => image.LoadWebpFromBuffer(bytes),
            _ => image.LoadSvgFromBuffer(bytes, SvgScale),
        };
        if (error != Error.Ok)
        {
            return null;
        }
        // Mipmaps keep a big picture smooth when it's drawn small, as on a card.
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
