# Card Game Prototyping Engine

A Godot 4.7 tool for prototyping card games without writing code. Cards come from a spreadsheet (CSV), the rules come from a JSON file, and the engine provides the table, dragging, turns, scoring, undo, and a simple bot.

Included games:

| Game | Files | Shows off |
|---|---|---|
| Gin Rummy (default) | `games/gin_rummy.json`, `decks/gin_deck.csv` | Turn phases, melds, scoring, a match to 100, a bot opponent |
| Color Match | `games/color_match.json`, `decks/color_match.csv` | A small two-player game with a custom deck, hidden hands, and a card effect. Built step by step in the tutorial below |
| Standard deck | `games/standard.json`, `decks/standard_deck.csv` | A free-play sandbox: draw, play, discard, sort |

## Quick start

1. Open the project folder in Godot 4.7.
2. Press **F5** (Run Project).
3. To play a different game, open `scenes/main.tscn`, select the **Main** node, and set **Rules Path** in the Inspector to a file in `games/`.

## Playing

| Control | What it does |
|---|---|
| Drag a card | Move it to the zone nearest where you drop it. If the rules forbid it, it snaps back and the log says why. |
| Drag a card within its own hand/row | Reorder it. This turns that zone's automatic sorting off. |
| Click a card | Runs that zone's click behavior (e.g. click the deck to draw). |
| Toolbar buttons | The game's actions. A button is greyed out while its condition is false. |
| **Undo** / Ctrl+Z | Step back one input. In a game with a bot, it goes back to your last input. |
| **Reload** | Re-read the JSON and CSV files from disk and start over. |
| **Sandbox** | Ignore the rules and move anything anywhere, for testing. Rule follow-ups (like scoring) don't run in sandbox. |

The right-hand panel shows the game's variables and live readouts at the top, and a log of everything that happened below. Errors appear in red.

## Tutorial: making a game

We'll build **Color Match**, a small UNO-like game. It's in the project already, so you can compare as you go.

> Each player starts with 7 cards. On your turn, play a card matching the top of the pile by color or number, or draw a card. Playing a +2 makes your opponent draw two. First to empty their hand wins.

### 1. Make the deck

A deck is a spreadsheet saved as CSV in `decks/`. The first row is column names; every other row is a card. You choose the columns. Here's the start of `decks/color_match.csv`:

| name | color | number | ink | count | on_pile |
|---|---|---|---|---|---|
| Red 1 | Red | 1 | #c0392b | 2 | |
| Red 2 | Red | 2 | #c0392b | 2 | |
| ... | | | | | |
| Red +2 | Red | +2 | #c0392b | 2 | draw_two |
| Blue 1 | Blue | 1 | #2471a3 | 2 | |

Three columns have special meaning:
- `name` is the card's title, used in the log.
- `count` (optional) is how many copies of the card to make; blank means 1.
- `on_<zone>` (optional) is a **card effect**: what happens when the card enters that zone. Here, a +2 entering the pile runs the `draw_two` effect, which we'll define in the game file in [step 5](#5-define-card-effects).

Every other column (`color`, `number`, `ink`) is yours to use in rules, sorting, and the card face.

**One required extra file:** Godot tries to import CSV files as translations. Next to each deck, add a file with the same name plus `.import` (e.g. `color_match.csv.import`) containing:

```
[remap]

importer="keep"
```

Or, in the Godot editor, select the CSV, open the **Import** tab, set **Import As** to **Keep File**, and click **Reimport**.

### 2. Lay out the table

Create `games/color_match.json`. Start with the **zones**: the named places cards can be.

```json
{
	"players": 2,
	"zones": {
		"deck": { "layout": "pile", "face": "down", "anchor": [0.4, 0.5], "cards": "res://decks/color_match.csv" },
		"pile": { "layout": "pile", "anchor": [0.6, 0.5] },
		"hand": { "per_player": true, "layout": "fan", "face": "owner", "anchors": [[0.5, 0.83], [0.5, 0.16]] }
	},
	"card_face": { "corner": "number", "title": "number", "body": ["color"], "color": "ink" }
}
```

- `anchor` is where the zone sits, as fractions of the table: `[0.5, 0.5]` is the center, `[0.5, 0.83]` is near the bottom.
- `layout` is how the cards spread out: `pile` (stacked), `row`, or `fan`.
- `"cards"` puts the whole deck in that zone at the start.
- `"per_player": true` makes one hand per player, `hand_1` and `hand_2`, placed at the two `anchors`.
- `"face": "owner"` shows each hand only on its owner's turn, so two people can share a screen.
- `card_face` picks which columns appear on the card. `color` is the column holding each card's text color.

Point Rules Path at this file and press Reload. You should see a face-down deck. Nothing is dealt yet.

### 3. Set up the game

**Triggers** are lists of steps that run when something happens. `start` runs when the game begins:

```json
"triggers": {
	"start": [
		"sort(hand_1, ['color', 'number'])",
		"sort(hand_2, ['color', 'number'])",
		"shuffle(deck)",
		"draw(deck, hand_1, 7)",
		"draw(deck, hand_2, 7)",
		"move(top(deck), pile)"
	],
	"deck_empty": ["move(without(pile, top(pile)), deck)", "shuffle(deck)"]
}
```

`deck_empty` runs when someone tries to draw from an empty deck. Here it shuffles the pile, minus its top card, back into the deck.

### 4. Write the rules for moves

Without a `moves` list, any drag is allowed. Once you add one, only the drags it lists are allowed:

```json
"variables": { "winner": null },
"moves": [
	{
		"from": "hand", "to": "pile",
		"when": "winner == null and (card.data.color == top(pile).data.color or card.data.number == top(pile).data.number)",
		"then": [{ "if": "count(hand) == 0", "then": ["assign('winner', player)"], "else": ["end_turn()"] }]
	},
	{
		"from": "deck", "to": "hand",
		"when": "winner == null",
		"then": ["end_turn()"]
	}
]
```

- `hand` means **the current player's** hand, so you can only play your own cards. (`opponent_hand` is the other player's.)
- `when` must be true for the move to be allowed. `card` is the card being dragged, and `card.data.color` reads its `color` column.
- `then` runs after the move. Here: if your hand is now empty you win, otherwise the turn passes.
- `winner` is a **variable** we declared. Variables show in the side panel, and are changed with `assign`.

To make clicking the deck draw a card too, add `"on_click": ["try_move(top(deck), hand)"]` to the deck zone. `try_move` follows the same move rules as dragging.

### 5. Define card effects

The deck's `on_pile` column says `draw_two`. Define what that means once, in the game file, and every card that names it shares it:

```json
"effects": {
	"draw_two": ["draw(deck, opponent_hand, 2)"]
}
```

Effects run when the card enters the zone, before the move rule's `then`. So at that point `opponent_hand` is still the player who's about to take their turn.

### 6. Add buttons

```json
"actions": {
	"Sort by color": ["sort(hand, ['color', 'number'])"],
	"Sort by number": ["sort(hand, ['number', 'color'])"]
}
```

Each action becomes a toolbar button that runs its steps. Reload, and the game is playable.

### 7. Iterate

Change anything, save, and press Reload. If something is wrong, the log tells you in red, usually as soon as the game loads:

```
Step "draw(deck, hnad)": unknown zone or variable "hnad"
```

When a drag is refused, the log shows the rule's condition so you can see which part failed. Turn on **Sandbox** to set up a situation by hand.

## Game file reference

Everything a game file can contain. Only `zones` is required.

### `zones`

| Key | Meaning | Default |
|---|---|---|
| `layout` | `pile`, `row`, `fan`, or `melds` (groups the cards into their melds; for rummy tables) | `row` |
| `face` | `up`, `down`, or `owner` (face up only on the owning player's turn). Cards take this face when they enter the zone. | `up` |
| `anchor` | Position on the table as `[x, y]` fractions | `[0.5, 0.5]` |
| `width` | Widest a `row`/`fan` may spread, as a fraction of the table width | 800 pixels |
| `cards` | A deck to start with, e.g. `"res://decks/my_deck.csv"` | empty |
| `on_click` | Steps run when a card in this zone is clicked; `card` is the clicked card | none |
| `per_player` | `true` makes one zone per player: `hand_1`, `hand_2`, ... | `false` |
| `anchors` | With `per_player`, one `[x, y]` position per player | |

### `players`, `variables`, `player_variables`

- `"players": 2` turns on turns. The `player` variable holds whose turn it is (starting at 1); `end_turn()` passes the turn.
- `"variables": { "phase": "draw", "taken": null }` declares variables and their starting values.
- `"player_variables": { "score": 0 }` makes one per player (`score_1`, `score_2`). In steps, `score` means the current player's and `opponent_score` the other player's, just like `hand` and `opponent_hand`.

### `moves`

A list of allowed drags. Each rule has:

| Key | Meaning |
|---|---|
| `from`, `to` | Zone names. Leave one out to mean "any zone". |
| `when` | Condition that must be true. `card` is the dragged card. |
| `then` | Steps to run after the move. |

The first rule that matches wins. With no `moves` list at all, every drag is allowed.

### `actions`

Toolbar buttons, in the order listed. Either a plain list of steps:

```json
"Shuffle": ["shuffle(deck)"]
```

or an object with extra options:

```json
"Knock": {
	"when": "phase == 'discard'",
	"choose": "without(hand, taken)",
	"steps": ["try_move(card, knock)"]
}
```

| Key | Meaning |
|---|---|
| `steps` | What the button does. |
| `when` | The button is disabled while this is false. |
| `choose` | The button first highlights these cards and waits for you to click one. The steps then run with that card as `card`. Clicking anything else cancels. |
| `human_only` | `true` means bots never press this button. |

### `triggers`

Named step lists:

| Name | Runs when |
|---|---|
| `start` | The game begins (and on Reload) |
| `turn_start` | After every `end_turn()` |
| `<zone>_empty` | A `draw` needs a card from that zone and it's empty, e.g. `deck_empty` |
| any other name | You call it with `trigger('name')`. Handy for routines used in several places, like a `deal`. |

### `effects`

Named card effects, used by name in a deck's `on_<zone>` columns:

```json
"effects": {
	"draw_two": ["draw(deck, opponent_hand, 2)"],
	"wild": [{ "if": "count(hand) > 1", "then": ["assign('phase', 'choose_color')"] }]
}
```

Each effect is a list of steps, so conditional steps work. `card` is the card whose effect is running. Every effect is checked when the game loads, even ones no card uses yet.

### `status`

Live readouts in the side panel, recalculated after every change:

```json
"status": { "deadwood in hand": "sum(deadwood(hand), 'points')" }
```

### `card_face`

| Key | Meaning |
|---|---|
| `corner` | Column shown small in the top-left |
| `title` | Column shown large in the middle |
| `body` | List of columns shown below the title |
| `color` | Column holding the text color: a name like `Red` or a hex code like `#c0392b` |

### `melds`

For rummy-style games. Describes which groups of cards count as melds:

```json
"melds": {
	"types": [
		{ "same": "rank", "min": 3 },
		{ "same": "suit", "sequence": "value", "min": 3 }
	],
	"points": "points"
}
```

A meld is at least `min` cards with the same value in the `same` column. With `sequence`, that column's numbers must also run consecutively (a run). `points` is the column counted as deadwood. This enables the meld building blocks below.

### `bot`

Computer players:

```json
"bot": { "players": [2], "prefer": "score - opponent_score - sum(deadwood(hand), 'points')" }
```

On a bot's turn, it tries every legal input: each allowed drag, each enabled button, and each choice for `choose` buttons. It scores the result of each with `prefer`, from its own point of view, and takes the highest. It plays one input at a time so you can watch.

Write `prefer` as "how good is this position for me?". Things to know:
- **It only looks one input ahead.**
- **It can see hidden cards,** including the top of the deck.
- **It never makes inputs that only reorder cards,** such as sorting.
- **`face: "owner"` doesn't suit bot games.** It would reveal the bot's hand on the bot's turn, so use `up` or `down`.

## Steps

A step is one line of text that does something or calculates something, like `draw(deck, hand, 2)`.

You can use:
- **Zone and variable names** directly.
- **`card`**: the card involved (dragged, clicked, chosen, or whose effect is running). It's `null` when there isn't one.
- **Card columns,** as `card.data.<column>`.
- **Operators:** `==`, `!=`, `<`, `>`, `<=`, `>=`, `+`, `-`, `*`, `/`, `and`, `or`, `not`, and parentheses.
- **Text in single quotes,** e.g. `'discard'`. The JSON itself uses double quotes.

Every step is checked when the game loads (spelling, unknown names, wrong number of arguments), so most mistakes show up right away. If a step fails while running, the rest of its list is skipped and the log says why.

### Conditional steps

There's no inline "if" in a step, so use a conditional step anywhere a list of steps is allowed:

```json
{ "if": "count(hand) > 7", "then": ["assign('phase', 'discard')"], "else": ["assign('phase', 'draw')"] }
```

`else` is optional, and conditional steps can be nested.

### Building blocks

**Changing things**

| Building block | Does |
|---|---|
| `draw(from, to, amount)` | Move the top card(s) of `from` to `to`. `amount` defaults to 1. Fires `<from>_empty` if it runs out. |
| `move(cards, to)` | Move cards to a zone. |
| `try_move(card, to)` | Move a card only if a `moves` rule allows it (and run that rule's `then`). Does nothing if `card` is `null`. |
| `shuffle(zone)` | Shuffle a zone. |
| `flip(cards)` | Turn cards over (until they move to another zone). |
| `sort(zone, ['column', ...])` | Keep a zone sorted by these columns, first column first. Numbers sort numerically. `sort(zone, [])` turns sorting off. |
| `assign('variable', value)` | Change a variable. |
| `end_turn()` | Pass the turn to the next player and fire `turn_start`. |
| `trigger('name')` | Run a step list from `triggers`. |

**Questions**

| Building block | Gives |
|---|---|
| `all(zone)` | All the cards in a zone |
| `top(zone)` | The top card, or `null` if empty |
| `count(cards)` | How many cards |
| `number(card, 'column')` | A card's column as a number, for comparing, e.g. `number(card, 'value') > 5` (`0` if `card` is `null` or the column isn't a number) |
| `sum(cards, 'column')` | The numeric total of a column |
| `where(cards, 'column', value)` | The cards whose column equals `value` |
| `without(cards, removed)` | The cards, minus the removed ones |

**Melds** (need `melds` in the game file)

| Building block | Gives |
|---|---|
| `melds(cards)` | The arrangement of melds that leaves the fewest deadwood points |
| `deadwood(cards)` | The cards left out of those melds |
| `layoff(cards, melds)` | Which of `cards` to add onto those melds to leave `cards` the fewest deadwood points |

Wherever a building block takes `cards`, you can pass a zone, a single card, or a list of cards (lists inside lists are fine).

### Tips

- **Card values are text.** `card.data.number == '3'` works, but for greater/less than use `number`: `number(card, 'value') > 5`.
- **Numbers in the JSON are decimals.** A variable starting at `0` displays as `0` but is stored as `0.0`. Comparisons work normally.
- **To deal the same way in several places,** put the steps in a custom trigger and call `trigger('deal')`.
- **Look at `games/gin_rummy.json`** for a full game: turn phases, knocking, scoring, and a match to 100.

## Deck reference

- **First row:** column names. Each row after it is one card; every column becomes readable as `card.data.<column>`.
- **`name`:** the card's title in the log.
- **`count`:** how many copies to make (blank = 1).
- **`on_<zone>`:** what happens when the card enters that zone. Usually the name of an effect from the game file's [`effects`](#effects), e.g. `draw_two`.
  - A cell can also hold steps directly, separated by `;`, and mix them with effect names, e.g. `draw_two; shuffle(deck)`.
  - For per-player zones, use the name without the number: `on_hand`, not `on_hand_1`.
  - A cell starting with `[` is read as a JSON list of steps, e.g. `[{"if": "count(hand) > 2", "then": ["move(card, pile)"]}]`. Usually it's tidier to make it a named effect instead.
  - An effect that keeps triggering itself is stopped with an error instead of freezing the game.
- **Commas and quotes:** cells containing commas need quotes around them. Spreadsheet programs do this automatically when exporting CSV.
- **Saving:** save the file as UTF-8 so symbols like ♠ survive.
- **The `.import` file:** remember it (see [Make the deck](#1-make-the-deck)).

## Extending the engine

Building blocks live in `scripts/core/actions.gd`. Each public method there becomes usable in every game's steps, with argument checking and error messages included. To add one, write a new method with typed parameters, e.g.:

```gdscript
func discard_hand(zone: Zone, to: Zone) -> void:
	move(zone, to)
```

Other places to look:
- `scripts/core/`: the game logic. `Game` runs the rules, `Zone` holds cards, `Melds` finds melds, `Bot` picks moves.
- `scripts/ui/`: the table and side panel.
- `ROADMAP.md`: what's been built and why.
