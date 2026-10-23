# Changing the engine

This page is for changing the engine itself. To make games, you only need the [README](../README.md) and the [reference](reference.md).

The engine is C#. Open the project in the Godot 4.7 .NET build, and the C# in any C# editor: `CardGamePrototyping.csproj` is the game, and `csharp/Core.sln` is the engine with its tests.

## Adding a building block

Building blocks are the public methods of `csharp/Core/Actions.cs`. Each one can be used in every game's steps, named the way steps write it (`DiscardHand` is `discard_hand`), with its arguments checked and its errors reported for free. To add one, write a method with typed parameters:

```csharp
public void DiscardHand(Zone zone, Zone to)
{
    Move(zone, to);
}
```

Then add it to the [reference](reference.md#building-blocks).

## Where things are

- **`csharp/Core/`** holds the game logic. It's a plain C# library: it never touches Godot or the UI.
  - `Game` holds the state and carries out inputs.
  - `Plays` knows what players can do.
  - `Choice` is the choice being made.
  - Steps are read by `StepParser`, checked at load by `StepCompiler`, worked out by `StepEvaluator`, and run by `StepRunner`, which pauses at a `choose` or `offer` and starts each turn.
  - `Bot` picks moves, and `Snapshot` saves the state for undo.
- **`csharp/Editing/`** holds the editor's logic: `GameDocument` edits a game file's text in place, and `Recipes`, `TurnSummary`, `BlockHelp` and `EditorFields` serve its panels. `TabletopDeck` and `TabletopSave` lay a game out for Tabletop Simulator; `TabletopExport` (`scripts/editor/`) draws the card sheets and writes the save into Tabletop Simulator's Saves folder.
- **`scripts/ui/`** holds the title screen, the table and the side panel.
- **`scripts/editor/`** holds the game editor. `EditorScreen` ties it together; `GameOutline`, `Inspector`, `GameFileView` and the sidebar browsers are its panels; `EditorTheme` and `EditorIcons` are its look. The building-block help comes from this repository's `docs/reference.md` (`BlockHelp`), so an exported build needs `docs/*` in its export filter for that help.
- **`scripts/net/Net.cs`** holds online play: the lobby, inputs sent through the host, and rejoining.

## Inputs, seeds and replays

- **Inputs:** everything a player does reaches the game as a `PlayerInput` through `Game.Apply(input)`, e.g. `new("drag") { Card = 12, To = "pile" }`. The table, the bots and online play all go through it, and `Game.Inputs` keeps the list.
- **Seeds:** all randomness comes from the seed given to `new Game(files, path, seed, players)` (`players`: how many play, for a game that allows several numbers). Its random numbers are Godot's own (`RandomNumbers`), so a seed deals the same cards it did in the GDScript version.
- **Replays:** `Game.Record()` gives the rules, seed, inputs and number of players, and `Game.Replay(files, record)` rebuilds the match. `Snapshot.StateHash(game)` sums up the whole state as one number, to check that two copies agree.
- **Seats:** `Game.LocalSeat` is the player this screen belongs to (`0` when people share a screen), and `Game.Viewer()` the player the table acts as. An input from a player is refused unless `Game.MayAct` allows it. A refused input mustn't change anything, since it isn't recorded: online, every copy of a match comes out the same from the inputs alone. If a copy drifts, the host replays its own inputs, and when even its own copy differs from that replay, every copy is rebuilt from it.

## Tests

Run the engine and editor tests with `dotnet test` in `csharp/`. They take about ten seconds.

| Tests | Run them after changing |
|---|---|
| `ReplayTests` | Anything. They play two recorded matches of every game, which must log exactly as recorded. |
| `PlaythroughTests` | A game or turns. They play every game to its end with bots, with each number of players it allows. |
| `DeterminismTests` | The engine. They check that every game replays exactly. |
| `RegressionTests` | Anything. Gin, Hearts and Color Match with bots, and situations that once went wrong. |
| `StepLanguageTests` | The step language: operators, `if ... else`, `=` and `+=`, and error messages |
| `TurnTests` | Turns, offers, and actions done by drags, clicks and buttons |
| `ChooseTests` | `StepRunner` or choosing |
| `PlayersTests` | Per-player values, comparisons, knockouts, `reset` and winners |
| `EditingTests` | The editor. Game files keep exactly as written, and the recipes load. |
| `LoadingTests` | Reading games: every game's rules, zones and decks, as recorded. |

The recordings are in `csharp/Core.Tests/Reference/`. Changing a game file, or what the engine does on purpose, changes them: record them again with `dotnet run --project csharp/Record`, and read the differences before committing them.

Two tests are scenes, run with the Godot .NET build:

- `godot --headless res://tests/ui_test.tscn` opens every screen, and every game on the table and in the editor, with bots playing.
- `godot --headless res://tests/net_test.tscn -- host` plays a hand of Hearts over the network. Run it with `-- guest` in a second terminal. The two final state numbers must match.

Both also fail if Godot reports an error while they run.
