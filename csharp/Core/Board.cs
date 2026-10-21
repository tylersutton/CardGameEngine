using System.Numerics;

namespace Core;

/// A zone's "board": a grid of spaces, square or hex, each space a zone of its own ("square_1", ...) numbered
/// across each row from the top. It knows where each space sits and which spaces are next to it. A hex board is a
/// rhombus, as in the game Hex: each row sits half a space further right than the one above.
public class Board
{
    public const string Square = "square";
    public const string Hex = "hex";
    public static readonly string[] Shapes = [Square, Hex];
    public const string Orthogonal = "orthogonal";
    public const string Diagonal = "diagonal";
    public static readonly string[] DirectionKinds = ["", Orthogonal, Diagonal];
    public const double DefaultSpaceSize = 60;
    static readonly float RowHeight = MathF.Sqrt(3) / 2;

    static readonly (int Across, int Down)[] OrthogonalSteps = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    static readonly (int Across, int Down)[] DiagonalSteps = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
    static readonly (int Across, int Down)[] HexSteps = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];

    public string Shape { get; }
    public int Columns { get; }
    public int Rows { get; }
    /// How far apart neighbouring spaces are, in pixels.
    public float SpaceSize { get; }
    /// Colors for spaces, each by a condition with one parameter (the space), in order: a space takes the first
    /// that holds, e.g. Hex's goal edges, or a checkerboard.
    public Dictionary<string, string> Paint { get; } = [];
    /// A picture drawn under the board's spaces (an image in images/), or "".
    public string Image { get; }

    public Board(Dictionary<string, object?> settings)
    {
        Shape = settings.GetValueOrDefault("shape") as string ?? Square;
        Columns = (int)Values.ToDouble(settings["columns"]);
        Rows = (int)Values.ToDouble(settings["rows"]);
        SpaceSize = (float)(settings.GetValueOrDefault("size") as double? ?? DefaultSpaceSize);
        Image = settings.GetValueOrDefault("image") as string ?? "";
        foreach ((string condition, object? color) in settings.GetValueOrDefault("paint") as Dictionary<string, object?> ?? [])
        {
            Paint[condition] = (string)color!;
        }
    }

    /// The zone's board, if it has one.
    public static Board? Of(Dictionary<string, object?> definition) =>
        definition.GetValueOrDefault("board") is Dictionary<string, object?> settings ? new(settings) : null;

    public int Spaces => Columns * Rows;

    public int Column(int number) => (number - 1) % Columns + 1;

    public int Row(int number) => (number - 1) / Columns + 1;

    /// The space at that column and row, or 0 if it's off the board.
    public int Number(int column, int row) => column >= 1 && column <= Columns && row >= 1 && row <= Rows ? (row - 1) * Columns + column : 0;

    /// Where the space sits, in pixels from the board's center.
    public Vector2 Offset(int number)
    {
        Vector2 place = Spot(Column(number), Row(number));
        Vector2 middle = (Spot(1, 1) + Spot(Columns, Rows)) / 2;
        return (place - middle) * SpaceSize;
    }

    Vector2 Spot(int column, int row) => Shape == Hex
        ? new(column - 1 + (row - 1) / 2f, (row - 1) * RowHeight)
        : new(column - 1, row - 1);

    /// The ways to step to a neighbour: on a square board all eight, or only the orthogonal or diagonal four; on a
    /// hex board always all six.
    public (int Across, int Down)[] Steps(string kind = "") => (Shape, kind) switch
    {
        (Hex, _) => HexSteps,
        (_, Orthogonal) => OrthogonalSteps,
        (_, Diagonal) => DiagonalSteps,
        _ => [.. OrthogonalSteps, .. DiagonalSteps],
    };

    /// The space one step away in that direction, or 0 off the board.
    public int Toward(int number, (int Across, int Down) step) => Number(Column(number) + step.Across, Row(number) + step.Down);

    public List<int> Neighbors(int number, string kind = "") =>
        Steps(kind).Select(step => Toward(number, step)).Where(neighbor => neighbor > 0).ToList();

    /// The spaces caught in straight lines from the space: in each direction, a run of spaces that are "between"
    /// ending at one that's an "end" (Reversi's captures).
    public List<int> Flanked(int number, Func<int, bool> between, Func<int, bool> end)
    {
        List<int> caught = [];
        foreach ((int Across, int Down) step in Steps())
        {
            List<int> run = [];
            int next = Toward(number, step);
            while (next > 0 && between(next))
            {
                run.Add(next);
                next = Toward(next, step);
            }
            if (run.Count > 0 && next > 0 && end(next))
            {
                caught.AddRange(run);
            }
        }
        return caught;
    }

    /// Whether a path of neighbouring spaces, each one that holds, links one of the starts to one of the goals.
    public bool Connected(IEnumerable<int> starts, IEnumerable<int> goals, Func<int, bool> holds, string kind = "")
    {
        HashSet<int> goalSpaces = [.. goals];
        Queue<int> waiting = new(starts.Where(holds));
        HashSet<int> reached = [.. waiting];
        while (waiting.TryDequeue(out int space))
        {
            if (goalSpaces.Contains(space))
            {
                return true;
            }
            foreach (int neighbor in Neighbors(space, kind).Where(neighbor => !reached.Contains(neighbor) && holds(neighbor)))
            {
                reached.Add(neighbor);
                waiting.Enqueue(neighbor);
            }
        }
        return false;
    }

    /// The most spaces in one straight line through the space (counting it) that all hold; 0 if it doesn't.
    public int InARow(int number, Func<int, bool> holds)
    {
        if (!holds(number))
        {
            return 0;
        }
        return Steps().Max(step => 1 + RunLength(number, step, holds) + RunLength(number, (-step.Across, -step.Down), holds));
    }

    int RunLength(int number, (int Across, int Down) step, Func<int, bool> holds)
    {
        int length = 0;
        for (int next = Toward(number, step); next > 0 && holds(next); next = Toward(next, step))
        {
            length++;
        }
        return length;
    }
}
