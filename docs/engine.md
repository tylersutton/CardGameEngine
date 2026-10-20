# Changing the engine

This page is for changing the engine itself. To make games, you only need the [README](../README.md) and the [reference](reference.md).

## Adding a building block

Building blocks are the public methods of `scripts/core/actions.gd`. Each one can be used in every game's steps, with its arguments checked and its errors reported for free. To add one, write a method with typed parameters:

```gdscript
func discard_hand(zone: Zone, to: Zone) -> void:
	move(zone, to)
```

Then add it to the [reference](reference.md#building-blocks).

## Where things are

- **`scripts/core/`** holds the game logic, and never touches the UI.
  - `Game` holds the state and carries out inputs.
  - `Plays` knows what players can do.
  - `Choice` is the choice being made.
  - Steps are read by `StepParser`, checked at load by `StepCompiler`, worked out by `StepEvaluator`, and run by `StepRunner`, which pauses at a `choose` or `offer` and starts each turn.
  - `Bot` picks moves, and `Snapshot` saves the state for undo.
- **`scripts/ui/`** holds the table and the side panel.
- **`scripts/editor/`** holds the game editor. `EditorScreen` ties it together; `GameOutline`, `Inspector`, `GameFileView` and the sidebar browsers are its panels; `EditorTheme` and `EditorIcons` are its look. The building-block help comes from this repository's `docs/reference.md` (`BlockHelp`), so an exported build needs `docs/*` in its export filter for that help.
- **`scripts/net/net.gd`** holds online play: the lobby, inputs sent through the host, and rejoining.

## Inputs, seeds and replays

- **Inputs:** everything a player does reaches the game as plain data through `Game.apply(input)`, e.g. `{type = "drag", card = 12, to = "pile"}`. The table, the bots and online play all go through it, and `Game.inputs` keeps the list.
- **Seeds:** all randomness comes from `Game.new(path, seed)`'s seed, so the same seed and inputs always give the same game.
- **Replays:** `Game.record()` gives `{rules, seed, inputs}`, and `Game.replay(record)` rebuilds the match. `Game.state_hash()` sums up the whole state as one number, to check that two copies agree.
- **Seats:** `Game.local_seat` is the player this screen belongs to (`0` when people share a screen). An input from a player is refused unless `Game.may_act` allows it.

## Tests

Run a test with `godot --headless -s tests/<name>.gd`. A test also fails if Godot reports a script error while it runs.

| Test | Run it after changing |
|---|---|
| `regression_test.gd` | Anything. It loads every game and plays a few with bots. |
| `playthrough_test.gd` | A game or turns. It plays every game to its end with bots. |
| `determinism_test.gd` | The engine. It checks that every game replays exactly. |
| `step_language_test.gd` | The step language: operators, `if ... else`, `=` and `+=`, and error messages |
| `turn_test.gd` | Turns, offers, and actions done by drags, clicks and buttons |
| `choose_test.gd` | `StepRunner` or choosing |
| `players_test.gd` | Per-player values, comparisons, knockouts, `reset` and winners |
| `editor_test.gd` | The editor. It checks that game files keep exactly as written, and that the recipes load. |

Two tests are scenes:

- `godot --headless res://tests/ui_test.tscn` opens every screen, and every game, with bots playing.
- `godot --headless res://tests/net_test.tscn -- host` plays a hand of Hearts over the network. Run it with `-- guest` in a second terminal. The two final state numbers must match.
