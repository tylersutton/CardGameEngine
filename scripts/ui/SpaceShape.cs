using Godot;

/// The shapes a board's paint can give a space (Board.PaintShapes), drawn the same way for the space and for a "fill"
/// piece on it: a dot, a bar across or down reaching the middles of the spaces either side (Dots and Boxes' lines), or
/// the whole space.
public static class SpaceShape
{
    public const string Dot = "dot";
    const string AcrossBar = "hbar";
    const string DownBar = "vbar";
    const float DotSize = 0.4f;
    const float BarThickness = 0.2f;

    /// How big the shape is, for a space of that size, centered on the space's middle.
    public static Vector2 Extent(string shape, float spaceSize) => shape switch
    {
        Dot => Vector2.One * spaceSize * DotSize,
        AcrossBar => new(spaceSize * 2, spaceSize * BarThickness),
        DownBar => new(spaceSize * BarThickness, spaceSize * 2),
        _ => Vector2.One * spaceSize,
    };

    public static void Draw(CanvasItem canvas, Vector2 center, string shape, float spaceSize, Color color)
    {
        Vector2 extent = Extent(shape, spaceSize);
        if (shape == Dot)
        {
            canvas.DrawCircle(center, extent.X / 2, color);
        }
        else
        {
            canvas.DrawRect(new Rect2(center - extent / 2, extent), color);
        }
    }
}
