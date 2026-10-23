using Godot;

/// Where each card of a zone sits, relative to the zone's anchor, for each layout.
public static class ZoneLayout
{
    const float PileStep = 0.25f;
    const float RowSpacing = 110;
    const float FanSpacing = 50;
    const float FanRadius = 1200;
    public const float MaxWidth = 800;
    const float MeldSpacing = 26;
    const float ColumnSpacing = 30;
    const float FaceDownSpacing = 12;
    const float PileSpread = 25;
    const float MeldGap = 24;
    /// How far from a board space's middle pieces sharing it sit, as a fraction of the space.
    const float ClusterRadius = 0.22f;

    public static Transform2D Place(string layout, int index, int count, float maxSpread = MaxWidth, int spreadTop = 0)
    {
        float stepsFromMiddle = index - (count - 1) / 2f;
        float widestSpacing = (maxSpread - CardView.CardSize.X) / Mathf.Max(count - 1, 1);
        switch (layout)
        {
            case "pile":
                return new(0, new Vector2(Mathf.Max(0, index - count + spreadTop) * PileSpread, -index * PileStep));
            case "fan":
                float fanX = stepsFromMiddle * Mathf.Min(FanSpacing, widestSpacing);
                return new(fanX / FanRadius, new Vector2(fanX, fanX * fanX / (2 * FanRadius)));
            case "melds":
                return new(0, new Vector2(stepsFromMiddle * MeldSpacing, 0));
            default:
                return new(0, new Vector2(stepsFromMiddle * Mathf.Min(RowSpacing, widestSpacing), 0));
        }
    }

    /// Pieces sharing a board space sit round its middle, so each one shows (a pawn on another's square).
    public static Transform2D Cluster(int index, int count, float spaceSize)
    {
        if (count == 1)
        {
            return Transform2D.Identity;
        }
        return new(0, Vector2.Up.Rotated(Mathf.Tau * index / count) * spaceSize * ClusterRadius);
    }

    /// How far down each card of a column sits: face-down cards overlap more, and a long column squeezes to fit.
    public static List<float> ColumnOffsets(List<bool> facesUp, float maxLength)
    {
        List<float> offsets = [0];
        for (int i = 1; i < facesUp.Count; i++)
        {
            offsets.Add(offsets[i - 1] + (facesUp[i - 1] ? ColumnSpacing : FaceDownSpacing));
        }
        float squeeze = offsets[^1] > 0 ? Mathf.Min(1, maxLength / offsets[^1]) : 1;
        return offsets.Select(offset => offset * squeeze).ToList();
    }

    public static List<float> GroupCenters(List<int> groupSizes)
    {
        List<float> widths = groupSizes.Select(cards => (cards - 1) * MeldSpacing + CardView.CardSize.X).ToList();
        float left = -(widths.Sum() + MeldGap * (widths.Count - 1)) / 2;
        List<float> centers = [];
        foreach (float width in widths)
        {
            centers.Add(left + width / 2);
            left += width + MeldGap;
        }
        return centers;
    }
}
