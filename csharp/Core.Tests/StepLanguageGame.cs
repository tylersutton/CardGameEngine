using System.Text.Json;
using Core;

/// Games made from a test's own game file text, with changes (JSON) replacing its top-level sections.
public static class TestGames
{
    public static Game Create(string rules, string changes = "{}", long seed = 1, string path = "res://test.jsonc")
    {
        Dictionary<string, object?> merged = (Dictionary<string, object?>)GameRules.Parse(rules)!;
        foreach ((string key, object? value) in (Dictionary<string, object?>)GameRules.Parse(changes)!)
        {
            merged[key] = value;
        }
        return new(Files(path, JsonSerializer.Serialize(merged)), path, seed);
    }

    /// The repository's files, with this text as the game file.
    public static GameFiles Files(string path, string text) => new(RepositoryFiles.PathOf(""), new() { [path] = text });

    public static Card Named(Game game, string title) => game.Cards.First(card => card.Title == title);
}

/// The game file of the step language test: two players, a standard deck, a pile and a hand each, variables, a
/// player variable, a constant and conditions.
public static class StepLanguageGame
{
    const string Rules = """
        {
            "players": 2,
            "zones": {
                "deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
                "pile": { "layout": "pile" },
                "hand": { "per_player": true }
            },
            "variables": { "phase": "draw", "total": 0, "names": [] },
            "player_variables": { "score": 0 },
            "constants": { "table": [0, 0, 2, 4] },
            "conditions": {
                "is_ace": "card.rank == 'A'",
                "deck_full": "count(deck) == 52",
                "suited(cards, suit)": "count(where(cards, 'SUIT', suit))",
                "hearts_and_spades(cards)": "suited(cards, 'Hearts') + suited(cards, 'Spades')"
            }
        }
        """;

    public static Game Create(string changes = "{}") => TestGames.Create(Rules, changes, 1, "res://step_language_test.jsonc");
}
