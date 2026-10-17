# Card Game Prototyping Engine

A Godot 4.7 tool for prototyping card games without writing code. Cards come from a spreadsheet (CSV), the rules come from a JSON file, and the engine provides the table, dragging, turns, scoring, undo, and a simple bot.

Included games:

| Game | Files | Shows off |
|---|---|---|
| Gin Rummy (default) | `games/gin_rummy.json`, `decks/gin_deck.csv` | Turn phases, melds, scoring, a match to 100, a bot opponent |
| Hearts | `games/hearts.json`, `decks/hearts_deck.csv` | Four players (you and three bots), passing three cards, following suit, winning tricks |
| Color Match | `games/color_match.json`, `decks/color_match.csv` | A small two-player game with a custom deck, hidden hands, card effects, and wild cards that ask for a color. Built step by step in the tutorial below |
| Solitaire (Klondike) | `games/solitaire.json`, `decks/gin_deck.csv` | Cascading columns, dragging whole stacks, groups of zones (7 columns, 4 foundations), double-click to send a card up, draw one or three |
| Standard deck | `games/standard.json`, `decks/standard_deck.csv` | A free-play sandbox: draw, play, discard, sort |

## Quick start

1. Open the project folder in Godot 4.7.
2. Press **F5** (Run Project).
3. Pick a game from the title screen. Every `.json` file in `games/` is listed there, so a new game appears automatically.

To jump straight into one game while working on it, open `scenes/main.tscn`, set **Rules Path** on the **Main** node in the Inspector, and press **F6** (Run Current Scene).

## Playing

| Control | What it does |
|---|---|
| Drag a card | Move it to the zone nearest where you drop it. While dragging, that zone and the card are outlined green if the move is allowed, or red if not. A refused drop snaps back and the log says why. |
| Dragging over a hand | The other cards move aside to show where the card will land: under the cursor, or in its sorted spot if the hand is sorted. |
| Drag a card within its own hand/row | Reorder it. This turns that zone's automatic sorting off. |
| Click a card | Runs that zone's click behavior (e.g. click the deck to draw). Clicking an empty zone's outline runs it too, with no `card`. |
| Choosing | When the game asks you to choose cards, the choices are outlined in yellow: click one, or for several, click each (selected cards turn blue; click again to unselect). When it asks for something else, like a color, pick from the bar of buttons above the table. |
| Toolbar buttons | The game's actions. A button is greyed out while its condition is false. |
| **Undo** / Ctrl+Z | Step back one input. In a game with a bot, it goes back to your last input. |
| **Games** | Back to the title screen to pick another game. |
| **Reload** | Re-read the JSON and CSV files from disk and start over. |
| **Sandbox** | Ignore the rules and move anything anywhere, for testing. Rule follow-ups (like scoring) don't run in sandbox. |
| **Zone names** | Show every zone's name and card count above it: the names to use in rules (e.g. `won_1`). Off by default. |

The right-hand panel shows the game's variables and live readouts at the top, and a log of everything that happened below. Errors appear in red. Card moves and flips are grouped, one line per step and destination, e.g. `deck → hand_1: 5♦, 4♥, K♦`. Identical lines in a row are shown once with a count, e.g. `if count(pile) == 0 → else (×17)`.

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
		"deck": { "label": "Deck", "layout": "pile", "face": "down", "anchor": [0.4, 0.5], "cards": "res://decks/color_match.csv" },
		"pile": { "label": "Pile", "layout": "pile", "anchor": [0.6, 0.5] },
		"hand": { "per_player": true, "layout": "fan", "face": "owner", "anchors": [[0.5, 0.83], [0.5, 0.16]] }
	},
	"card_face": { "corner": "number", "title": "number", "body": ["color"], "color": "ink" }
}
```

- `anchor` is where the zone sits, as fractions of the table: `[0.5, 0.5]` is the center, `[0.5, 0.83]` is near the bottom.
- `"label": "Pile"` (optional) is shown faintly inside the zone's outline while it's empty. The zone's real name (`pile`) is shown with the **Zone names** toggle.
- `layout` is how the cards spread out: `pile` (stacked), `row`, or `fan`.
- `"cards"` puts the whole deck in that zone at the start.
- `"per_player": true` makes one hand per player, `hand_1` and `hand_2`, placed at the two `anchors`.
- `"face": "owner"` shows each hand only on its owner's turn, so two people can share a screen.
- `card_face` picks which columns appear on the card. `color` is the column holding each card's text color.

Run the project and pick **Color Match** on the title screen (it's listed because it's in `games/`). You should see a face-down deck. Nothing is dealt yet.

### 3. Set up the game

**Triggers** are lists of steps that run when something happens. `start` runs when the game begins:

```json
"triggers": {
	"start": [
		"sort(hand_1, ['color', 'number'])",
		"sort(hand_2, ['color', 'number'])",
		"shuffle(deck)",
		"deal(deck, all_hand, 7)",
		"move(top(deck), pile)"
	],
	"deck_empty": ["move(without(pile, top(pile)), deck)", "shuffle(deck)"]
}
```

`deal(deck, all_hand, 7)` deals 7 cards to every hand, one card at a time around the table. `all_hand` means every player's hand (`hand_1`, `hand_2`).

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

### 8. Add wild cards

The finished `color_match.json` goes one step further: Wild cards that let you choose the color to match. Four `Wild` rows in the deck have `wild` in their `on_pile` column, and the game file adds:

```json
"variables": { "winner": null, "active_color": "" },
"effects": {
	"wild": ["ask('Choose color')"]
},
"actions": {
	"Choose color": {
		"button": false,
		"when": "winner == null and active_color == 'Wild'",
		"choose": "['Red', 'Blue', 'Green', 'Yellow']",
		"steps": ["assign('active_color', choice)", "end_turn()"]
	}
}
```

- `ask('Choose color')` opens that action's choice as soon as the current move finishes.
- Because `choose` lists values instead of cards, they appear as buttons above the table. The picked one is `choice`.
- `"button": false` keeps the action out of the toolbar, since it only happens when asked.
- The play rule now matches `active_color` instead of the top card's color, and doesn't end the turn after a Wild (choosing the color does).

## Game file reference

Everything a game file can contain. Only `zones` is required.

### `title`, `description`

The name and one-line description shown on the title screen, e.g. `"title": "Gin Rummy"`. Without a title, the file name is used (`gin_rummy.json` → "Gin Rummy").

### `zones`

| Key | Meaning | Default |
|---|---|---|
| `label` | Text shown inside the zone's outline while it's empty, e.g. `"Discard"`, or `"K"` for an empty solitaire column. In per-player zones and copies, `#` becomes the number: `"Tricks #"` shows "Tricks 1", "Tricks 2", ... | none |
| `outline` | `false` draws no outline or label, for zones that are only used in passing (e.g. the deck in Hearts) | `true` |
| `layout` | `pile`, `row`, `fan`, `column` (cascading downward from the anchor, face-down cards overlapping more, squeezing to fit; for solitaire), or `melds` (groups the cards into their melds; for rummy tables) | `row` |
| `face` | `up`, `down`, `owner` (face up only on the owning player's turn), or `human` (face up unless a bot owns it). Cards take this face when they enter the zone. | `up` |
| `anchor` | Position on the table as `[x, y]` fractions | `[0.5, 0.5]` |
| `width` | Widest a `row`/`fan` may spread, as a fraction of the table width (of its height, for a zone turned sideways) | 800 pixels |
| `rotation` | Turn the zone, in degrees, e.g. `90` for a hand on the left side of the table | `0` |
| `cards` | A deck to start with, e.g. `"res://decks/my_deck.csv"` | empty |
| `on_click` | Steps run when a card in this zone is clicked (`card` is the clicked card), or when the empty zone is clicked (`card` is `null`) | none |
| `on_double_click` | Steps run when a card in this zone is double-clicked, e.g. solitaire's `["try_move(card, foundation)"]`. (The first click of a double-click also counts as a single click.) | none |
| `spread` | For a `pile`: fan out its top few cards sideways so they can be read, e.g. `3` for a draw-three solitaire waste | `0` |
| `drag` | `"stack"`: dragging a card also carries every card on top of it | one card |
| `per_player` | `true` makes one zone per player: `hand_1`, `hand_2`, ... | `false` |
| `copies` | A number: makes that many zones, e.g. `"copies": 7` makes `tableau_1` ... `tableau_7` | |
| `anchors` | With `per_player` or `copies`, one `[x, y]` position each | |
| `rotations` | With `per_player` or `copies`, one rotation each, e.g. `[0, 90, 180, 270]` for four players around a table | |

**Per-player zones and copies** are named the same way but mean different things in rules and steps:
- **Per-player:** `hand` means the current player's hand, and `opponent_hand` the next player's. `all_hand` is every player's hand, as a list (e.g. `count(all_played) == 4`).
- **Copies:** the plain name means the whole group. In a move rule, `"to": "foundation"` matches any of the foundations. In a step, `foundation` is the list of all of them, e.g. `count(foundation)` counts every card on every foundation, and `try_move(card, foundation)` moves to the first one that allows it.

### `players`, `variables`, `player_variables`

- `"players": 2` turns on turns. The `player` variable holds whose turn it is (starting at 1); `end_turn()` passes the turn to the next player. To jump to a specific player, `assign('player', ...)`, e.g. the winner of a trick.
- `"seats": [[0.24, 0.71], [0.24, 0.5], ...]` (optional, one `[x, y]` per player) draws a name tag for each player at that spot on the table. The current player's tag is highlighted, and bots' tags say "(bot)".
- `"player_names": ["You", "West", "North", "East"]` (optional) names the players on their tags; otherwise they're "Player 1", "Player 2", ...
- `players` is the list of player numbers (`[1, 2, 3, 4]`). Loop over it with `"for": "player"` to do something for every player as if it were their turn (see [Loops](#loops)).
- `"variables": { "phase": "draw", "taken": null }` declares variables and their starting values.
- `"player_variables": { "score": 0 }` makes one per player (`score_1`, `score_2`). In steps, `score` means the current player's and `opponent_score` the other player's, just like `hand` and `opponent_hand`.

### `moves`

A list of allowed drags. Each rule has:

| Key | Meaning |
|---|---|
| `from`, `to` | Zone names, or the name of a group of `copies` to match any of them. Leave one out to mean "any zone". |
| `when` | Condition that must be true. |
| `then` | Steps to run after the move. |

Inside `when` and `then`, `card` is the dragged card, `from` is the zone it came from, and `to` is where it's going. For example, a solitaire foundation rule:

```json
{
	"from": "tableau", "to": "foundation",
	"when": "card == top(from) and number(card, 'value') == count(to) + 1",
	"then": ["flip(face_down(top(from)))"]
}
```

The first rule that matches wins. With no `moves` list at all, every drag is allowed. When a zone drags stacks, the rule is checked for the grabbed card and the cards on top of it come along.

### `actions`

Toolbar buttons, in the order listed. Either a plain list of steps:

```json
"Shuffle": ["shuffle(deck)"]
```

or an object with extra options:

```json
"Knock": {
	"when": "count(movable(hand, knock)) > 0",
	"choose": "movable(hand, knock)",
	"steps": ["try_move(card, knock)"]
}
```

| Key | Meaning |
|---|---|
| `steps` | What the button does. |
| `when` | The button is disabled while this is false. |
| `choose` | What to choose from. If it gives cards (e.g. `"hand"`), they're highlighted for you to click. If it gives other values (e.g. `"['Red', 'Blue']"` or `"numbers(0, 13)"`), they appear as buttons above the table. The steps then run with the pick as `choice` (and, for a single card, also as `card`). Clicking anything else cancels. |
| `pick` | How many cards to choose, e.g. `3` to pass three cards. `choice` is then a list of cards. |
| `human_only` | `true` means bots never press this button. |
| `button` | `false` hides it from the toolbar, for actions only started with `ask('name')`. |

### `triggers`

Named step lists:

| Name | Runs when |
|---|---|
| `start` | The game begins (and on Reload) |
| `turn_start` | After every `end_turn()` |
| `<zone>_empty` | A `draw` needs a card from that zone and it's empty, e.g. `deck_empty`. For a per-player zone (`deck_2`), `deck_empty` runs as that zone's owner, so `["move(discard, deck)", "shuffle(deck)"]` refills whoever ran out. |
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

### `conditions`

Named expressions, so a check used in several places is written once:

```json
"conditions": {
	"top_card": "card == top(from)",
	"fits_tableau": "(count(to) == 0 and number(card, 'value') == 13) or (number(card, 'value') == number(top(to), 'value') - 1 and card.data.color != text(top(to), 'color'))"
},
"moves": [
	{ "from": "waste", "to": "tableau", "when": "fits_tableau" },
	{ "from": "tableau", "to": "tableau", "when": "card.face_up and fits_tableau" },
	{ "from": "foundation", "to": "tableau", "when": "top_card and fits_tableau" }
]
```

- **Where they work:** a condition's name can be used in any expression: `when`, `if`, `status`, steps, other conditions.
- **How they work:** it behaves exactly as if its text were written there in brackets, so `card`, `from` and `to` mean whatever they mean at that spot.
- **Refused moves:** the log quotes the names (`requires: card.face_up and fits_tableau`), which keeps refusal messages short.
- **Not just true/false:** a condition can be any expression, e.g. `"rank": "number(card, 'value')"`.

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
| `text` | Column with rules text, drawn small and word-wrapped under the title (long titles also shrink to fit) |
| `zoom` | e.g. `2.4`: hovering a face-up card shows an enlarged copy beside it, so small text can be read |
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

On a bot's turn, it tries every legal input: each allowed drag, each click on a zone with `on_click`, each enabled button, and each choice for `choose` buttons. It scores the result of each with `prefer`, from its own point of view, and takes the highest. It plays one input at a time so you can watch.

Write `prefer` as "how good is this position for me?". Things to know:
- **It only looks one input ahead.**
- **It doesn't peek.** Cards it couldn't see (face down, even its own, like the order of its deck; or in another player's `owner`/`human` hand) are shuffled among themselves before each try, several times, and the results averaged. So drawing from the deck is judged on what it might get, not what's really there.
  - `"samples": 4` sets how many shuffles (more is fairer but slower).
  - `"peek": true` lets the bot see everything, which is stronger and faster.
- **It never makes inputs that only reorder cards,** such as sorting.
- **Use `face: "human"` for hands in bot games.** It shows your hand and hides the bots'. (`owner` would reveal each bot's hand on its turn.)

## Steps

A step is one line of text that does something or calculates something, like `draw(deck, hand, 2)`.

You can use:
- **Zone and variable names** directly.
- **`card`**: the card involved (dragged, clicked, chosen, or whose effect is running). It's `null` when there isn't one.
- **`choice`**: what was picked in a `choose` action: a card, a list of cards, or a value.
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

### Loops

To repeat steps, use a loop step anywhere a list of steps is allowed:

```json
{ "for": "column", "in": "tableau", "do": ["draw(stock, column, column_number)", "flip(without(column, top(column)))"] }
```

This deals solitaire: for each of the 7 tableau columns, draw as many cards as its position (1 to 7), then turn all but the top card face down.

- **The loop variable:** `"for"` names it, here `column`. It can be used in the `do` steps, and `<name>_number` is its position, counting from 1.
- **What `"in"` can be:**
  - a zone, to loop over its cards (`"in": "hand"`)
  - a group of zones (`"in": "tableau"`)
  - any list (`"in": "where(hand, 'suit', 'Hearts')"`)
  - a number, to repeat that many times (`"in": "3"`)
- **Changing what you're looping over is safe:** the loop goes through the items that were there when it started, so `{ "for": "c", "in": "hand", "do": ["move(c, discard)"] }` works.
- **Nesting:** loops can be nested, and can contain conditional steps.
- **Every player:** `{ "for": "player", "in": "players", "do": [...] }` runs the steps once per player as if it were their turn, so `hand`, `score` and `opponent_hand` follow along: `{ "for": "player", "in": "players", "do": ["assign('score', score + sum(won, 'points'))"] }` scores everyone's won cards. The turn goes back to whoever had it after the loop.

### Repeat until

To repeat steps while something is true, use a `while` step:

```json
{ "while": "sum(dealer, 'value') < 17", "do": ["draw(deck, dealer)"] }
```

- **When it's checked:** before each repeat, so the steps may run zero times.
- **Safety limit:** after 1,000 repeats it stops with an error, so a condition that never becomes false can't freeze the game.
- **Example:** Solitaire's Auto-finish button uses one to keep moving cards to the foundations while any can go:

```json
{ "while": "count(movable(tableau, foundation)) > 0", "do": ["try_move(top(movable(tableau, foundation)), foundation)"] }
```

### Building blocks

**Changing things**

| Building block | Does |
|---|---|
| `draw(from, to, amount)` | Move the top card(s) of `from` to `to`. `amount` defaults to 1. Fires `<from>_empty` if it runs out. |
| `deal(from, to, amount)` | Deal `amount` cards to each zone in `to`, one card at a time in order, e.g. `deal(deck, all_hand, 13)`. To start with a particular player, list the hands in that order: `deal(stock, [hand, opponent_hand], 10)`. |
| `move(cards, to)` | Move cards to a zone. |
| `try_move(card, to)` | Move a card only if a `moves` rule allows it (and run that rule's `then`). `to` can be a group of zones (`foundation`); the first one that allows it is used. Does nothing if `card` is `null`. |
| `shuffle(zone)` | Shuffle a zone. |
| `flip(cards)` | Turn cards over (until they move to another zone). |
| `sort(zone, ['column', ...])` | Keep a zone sorted by these columns, first column first. Numbers sort numerically. `sort(zone, [])` turns sorting off. |
| `assign('variable', value)` | Change a variable. |
| `end_turn()` | Pass the turn to the next player and fire `turn_start`. |
| `trigger('name')` | Run a step list from `triggers`. |
| `ask('Action name')` | Start that action (usually a `choose`) once the current move finishes, e.g. from a wild card's effect. |

**Questions**

| Building block | Gives |
|---|---|
| `all(zone)` | All the cards in a zone |
| `top(cards)` | The top card of a zone (or the last of a list), or `null` if empty |
| `highest(cards, 'column')`, `lowest(cards, 'column')` | The card with the biggest or smallest number in that column, e.g. the winner of a trick: `highest(where(all_played, 'suit', lead), 'value')` |
| `owner(card)` | The number of the player whose zone the card is in (0 for shared zones), e.g. `assign('player', owner(highest(...)))` |
| `next_player(seats)` | The player that many seats after the current one, wrapping around: `next_player(1)` is next, `next_player(-1)` is previous |
| `zone('kind', player)` | That player's zone, e.g. `zone('hand', next_player(2))` for the player across the table |
| `numbers(low, high)` | The list of whole numbers from `low` to `high`, e.g. for bids: `"choose": "numbers(0, 13)"` |
| `count(cards)` | How many cards |
| `number(card, 'column')` | A card's column as a number, for comparing, e.g. `number(card, 'value') > 5` (`0` if `card` is `null` or the column isn't a number) |
| `text(card, 'column')` | A card's column as text, or `''` if `card` is `null`. Use this instead of `card.data.column` when the card might be missing, e.g. `text(top(to), 'color')` |
| `sum(cards, 'column')` | The numeric total of a column |
| `where(cards, 'column', value)` | The cards whose column equals `value` |
| `without(cards, removed)` | The cards, minus the removed ones |
| `movable(cards, to)` | The cards that a move rule currently allows into `to` (a zone, a group like `foundation`, or a list like `[tableau, foundation]`). `count(movable(hand, pile)) == 0` means "can't play"; `"choose": "movable(hand, knock)"` offers only legal cards |
| `matching(cards, 'condition')` | The cards for which a named [condition](#conditions) holds, checked with each card as `card`, e.g. `matching(hand, 'is_treasure')` with `"is_treasure": "card.data.type == 'Treasure'"` |
| `face_down(cards)` | The cards that are face down, e.g. `flip(face_down(top(from)))` turns the new top card up only if it's down |

**Melds** (need `melds` in the game file)

| Building block | Gives |
|---|---|
| `melds(cards)` | The arrangement of melds that leaves the fewest deadwood points |
| `deadwood(cards)` | The cards left out of those melds |
| `layoff(cards, melds)` | Which of `cards` to add onto those melds to leave `cards` the fewest deadwood points |

Wherever a building block takes `cards`, you can pass a zone, a single card, or a list of cards (lists inside lists are fine).

### Tips

- **Card values are text.** `card.data.number == '3'` works, but for greater/less than use `number`: `number(card, 'value') > 5`.
- **`and`/`or` stop as soon as the answer is known**, so `count(to) > 0 and top(to).data.color == 'Red'` is safe on an empty zone: the second half isn't checked when the first is false.
- **Numbers in the JSON are decimals.** A variable starting at `0` displays as `0` but is stored as `0.0`. Comparisons work normally.
- **To deal the same way in several places,** put the steps in a custom trigger and call `trigger('deal')`.
- **"Draw only if you can't play"** is a move rule like `{ "from": "deck", "to": "hand", "when": "count(movable(hand, pile)) == 0" }`. (A rule can't use `movable` to check moves into its own destination; that inner check counts as "no moves" and the log says so.)
- **To repeat a check,** give it a name in [`conditions`](#conditions). **To repeat a step,** use a [loop](#loops), or [repeat until](#repeat-until) something changes.
- **Look at `games/gin_rummy.json`** for a full game: turn phases, knocking, scoring, and a match to 100. `games/solitaire.json` shows columns, stacks, and groups of zones.

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
