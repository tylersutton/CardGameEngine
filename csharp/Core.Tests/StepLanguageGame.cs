using Core;

/// The game file of the step language test (tests/step_language_test.gd): two players, a standard deck, a pile and
/// a hand each, variables, a player variable, a constant and conditions.
public static class StepLanguageGame
{
    const string Path = "res://step_language_test.jsonc";
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

    /// The game, with changes (JSON) replacing its top-level sections.
    public static Game Create(string changes = "{}")
    {
        Dictionary<string, object?> rules = (Dictionary<string, object?>)GameRules.Parse(Rules)!;
        foreach ((string key, object? value) in (Dictionary<string, object?>)GameRules.Parse(changes)!)
        {
            rules[key] = value;
        }
        GameFiles files = new(RepositoryFiles.PathOf(""), new() { [Path] = System.Text.Json.JsonSerializer.Serialize(rules) });
        return new(files, Path, 1);
    }
}
