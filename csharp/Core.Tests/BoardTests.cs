using Core;

/// Boards: a zone's "board" makes one zone per space, which knows its column, row and neighbours, and the building
/// blocks that look along a board (neighbors, space_at, zones_where, flanked, connected, in_a_row).
public class BoardTests
{
    const string Rules = """
        {
            "players": 2,
            "zones": {
                "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
                "square": { "board": { "columns": 4, "rows": 3 } },
                "cell": { "board": { "shape": "hex", "columns": 3, "rows": 3, "size": 40 } }
            },
            "conditions": {
                "taken(space)": "count(space) > 0",
                "red(space)": "text(top(space), 'color') == 'Red'",
                "black(space)": "text(top(space), 'color') == 'Black'",
                "top_edge(space)": "space.row == 1",
                "bottom_edge(space)": "space.row == 3"
            }
        }
        """;

    readonly List<string> failures = [];

    Game GameWith(string changes = "{}")
    {
        Game game = TestGames.Create(Rules, changes, 1, "res://board_test.jsonc");
        Assert.Empty(game.Errors);
        game.Failed += failures.Add;
        game.Start();
        return game;
    }

    /// Puts a card of that color (Red or Black) from the deck on each space.
    static void Place(Game game, string color, params string[] spaces)
    {
        foreach (string space in spaces)
        {
            game.Run([$"move(top(where(deck, 'color', '{color}')), {space})"]);
        }
    }

    static List<string> Names(object? zones) => Actions.ZonesIn(zones).Select(zone => zone.Name).ToList();

    [Fact]
    public void EachSpaceIsAZone()
    {
        Game game = GameWith();
        Assert.Equal(12, game.ZoneGroups["square"].Count);
        Zone middle = game.Zones["square_6"];
        Assert.Equal("pile", middle.Layout);
        Assert.Equal(2L, game.Evaluate("square_6.column"));
        Assert.Equal(2L, game.Evaluate("square_6.row"));
        Assert.Equal(new System.Numerics.Vector2(-90, -60), game.Zones["square_1"].Offset);
        Assert.Equal(new System.Numerics.Vector2(90, 60), game.Zones["square_12"].Offset);
        Assert.Equal("square_12", Names(game.Evaluate("space_at(square, 4, 3)")).Single());
        Assert.Null(game.Evaluate("space_at(square, 5, 1)"));
        Assert.Empty(failures);
    }

    [Fact]
    public void HexRowsShiftHalfASpace()
    {
        Game game = GameWith();
        System.Numerics.Vector2 first = game.Zones["cell_1"].Offset;
        System.Numerics.Vector2 below = game.Zones["cell_4"].Offset;
        Assert.Equal(20, below.X - first.X, 3);
        Assert.Equal(40 * MathF.Sqrt(3) / 2, below.Y - first.Y, 3);
    }

    [Fact]
    public void Neighbors()
    {
        Game game = GameWith();
        Assert.Equal(["square_2", "square_5", "square_6"], Names(game.Evaluate("neighbors(square_1)")).Order());
        Assert.Equal(["square_2", "square_5"], Names(game.Evaluate("neighbors(square_1, 'orthogonal')")).Order());
        Assert.Equal(["square_6"], Names(game.Evaluate("neighbors(square_1, 'diagonal')")));
        Assert.Equal(8, Names(game.Evaluate("neighbors(square_6)")).Count);
        // Each row sits half a space right of the one above, so cell_5's neighbours above are cell_2 and cell_3.
        Assert.Equal(["cell_2", "cell_3", "cell_4", "cell_6", "cell_7", "cell_8"], Names(game.Evaluate("neighbors(cell_5)")).Order());
        Assert.Equal(["cell_2", "cell_4"], Names(game.Evaluate("neighbors(cell_1)")).Order());
        Assert.Empty(failures);
        game.Evaluate("neighbors(deck)");
        game.Evaluate("neighbors(square_1, 'sideways')");
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public void ZonesWhere()
    {
        Game game = GameWith();
        Place(game, "Red", "square_3", "square_7");
        Assert.Equal(["square_3", "square_7"], Names(game.Evaluate("zones_where(square, 'taken')")));
        Assert.Equal(3, Names(game.Evaluate("zones_where(cell, 'top_edge')")).Count);
        Assert.Empty(failures);
        game.Evaluate("zones_where(square, 'no_such_condition')");
        Assert.Single(failures);
    }

    [Fact]
    public void FlankedCatchesRunsClosedAtTheFarEnd()
    {
        Game game = GameWith();
        Place(game, "Black", "square_2", "square_3", "square_6");
        Place(game, "Red", "square_4");
        // Across the top row the blacks are closed by a red; down the diagonal the black runs off the board.
        Assert.Equal(["square_2", "square_3"], Names(game.Evaluate("flanked(square_1, 'black', 'red')")));
        Place(game, "Red", "square_11");
        Assert.Equal(["square_2", "square_3", "square_6"], Names(game.Evaluate("flanked(square_1, 'black', 'red')")).Order());
        Assert.Empty(Names(game.Evaluate("flanked(square_9, 'black', 'red')")));
        Assert.Empty(failures);
    }

    [Fact]
    public void ConnectedFollowsNeighbours()
    {
        Game game = GameWith();
        string topToBottom = "connected(zones_where(cell, 'top_edge'), zones_where(cell, 'bottom_edge'), 'red')";
        Place(game, "Red", "cell_2", "cell_4");
        Assert.Equal(false, game.Evaluate(topToBottom));
        Place(game, "Red", "cell_7");
        Assert.True(game.Evaluate(topToBottom) is true, "cell_2, cell_4 and cell_7 run down the hex board");
        Place(game, "Red", "square_1", "square_6");
        Assert.Equal(true, game.Evaluate("connected(square_1, square_6, 'red')"));
        Assert.Equal(false, game.Evaluate("connected(square_1, square_6, 'red', 'orthogonal')"));
        Assert.Empty(failures);
    }

    [Fact]
    public void InARowCountsTheLongestLine()
    {
        Game game = GameWith();
        Place(game, "Red", "square_1", "square_6", "square_11", "square_7");
        Assert.Equal(3L, game.Evaluate("in_a_row(square_6, 'red')"));
        Assert.Equal(2L, game.Evaluate("in_a_row(square_7, 'red')"));
        Assert.Equal(0L, game.Evaluate("in_a_row(square_2, 'red')"));
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("{ \"columns\": 8 }")]
    [InlineData("{ \"columns\": 8, \"rows\": 8, \"shape\": \"triangle\" }")]
    [InlineData("{ \"columns\": 8, \"rows\": 8, \"spacing\": 60 }")]
    public void MalformedBoardsAreLoadErrors(string board)
    {
        Game game = TestGames.Create(Rules, $$"""{ "zones": { "square": { "board": {{board}} } } }""", 1, "res://board_test.jsonc");
        Assert.Contains(game.Errors, error => error.Contains("\"board\" should look like"));
    }

    [Fact]
    public void BoardsMakeTheirOwnSpaces()
    {
        Game game = TestGames.Create(Rules, """{ "zones": { "square": { "board": { "columns": 2, "rows": 2 }, "copies": 4 } } }""", 1, "res://board_test.jsonc");
        Assert.Contains("Zone \"square\": a \"board\" makes its own spaces, so it can't have \"copies\"", game.Errors);
    }

    [Fact]
    public void BothSidesOfATwoSidedPieceArePublic()
    {
        Game game = GameWith("""{ "card_face": { "shape": "disc", "background": "color", "back": "#ffffff" } }""");
        Place(game, "Black", "square_1");
        Card piece = game.Zones["square_1"].Cards[0];
        game.Run(["flip(square_1)"]);
        Assert.False(piece.FaceUp);
        Assert.True(game.IsTwoSided(piece));
        Assert.Equal(piece.Title, game.Describe(piece));
        Game plain = GameWith();
        Place(plain, "Black", "square_1");
        plain.Run(["flip(square_1)"]);
        Assert.Equal(Game.HiddenCard, plain.Describe(plain.Zones["square_1"].Cards[0]));
    }
}
