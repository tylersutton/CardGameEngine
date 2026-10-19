# Card Game Prototyping Engine

A Godot 4.7 tool for prototyping card games without writing code. Cards come from a spreadsheet (CSV), the rules come from a JSON file, and the engine provides the table, dragging, turns, scoring, undo, and a simple bot.

Included games:

| Game | Files | Shows off |
|---|---|---|
| Gin Rummy (default) | `games/gin_rummy.jsonc`, `decks/standard_deck.csv` | Turn phases, melds, scoring, a match to 100, a bot opponent |
| Dominion | `games/dominion.jsonc`, `decks/dominion.csv` | Deckbuilding: a supply of 7 basic and 10 kingdom piles (pick the kingdom from 17 cards in the game file), per-player decks that reshuffle, actions/buys/coins, choices like Cellar, Chapel, Remodel, Mine and Throne Room, attacks (Militia, Witch) answered by the other player, Moat reactions, rules text with hover zoom, and a Big Money bot. The default kingdom is the official First Game set |
| Hearts | `games/hearts.jsonc`, `decks/standard_deck.csv` | Four players (you and three bots), passing three cards in a rotating direction, following suit, winning tricks, shooting the moon, a game to 100 |
| Spades | `games/spades.jsonc`, `decks/standard_deck.csv` | Partnerships (`teams`): bidding, team scores and bags, trump, nil bids, a game to 500 |
| Cribbage | `games/cribbage.jsonc`, `decks/standard_deck.csv` | Scoring combinations: fifteens, pairs and runs in pegging and in the show, the crib, go, nobs and flushes, a game to 121 |
| Kaboom | `games/kaboom.jsonc`, `decks/kaboom_deck.csv` | Responding to other players' cards (`ask_others`): Nope chains, Steal (the target picks what to give), Peek, and a bomb-dodging elimination game |
| Pyramid | `games/pyramid.jsonc`, `decks/standard_deck.csv` | Solitaire with overlapping cards (`covered_by`): pair uncovered cards that add up to 13 by dragging one onto the other |
| Goofspiel | `games/goofspiel.jsonc`, `decks/standard_deck.csv` | Everyone bids at once (`ask_all`): secret face-down bids, a reveal, ties carrying the prize over |
| Fireworks | `games/fireworks.jsonc`, `decks/fireworks_deck.csv` | A cooperative Hanabi-style game: you hold your cards facing away (`face: "others"`), clues `mark` your partner's cards, and the bot partner reasons only from what it's been told |
| President | `games/president.jsonc`, `decks/standard_deck.csv` | Playing several cards at once (`allow` checks they're a valid set), a single 2 that beats anything and clears the table, matching the last play to skip the next player, passing, finishing order and titles, the card exchange between President and Scum |
| Color Match | `games/color_match.jsonc`, `decks/color_match.csv` | A small two-player game with a custom deck, hidden hands, card effects, and wild cards that ask for a color. Built step by step in the tutorial below |
| Solitaire (Klondike) | `games/solitaire.jsonc`, `decks/standard_deck.csv` | Cascading columns, dragging whole stacks, groups of zones (7 columns, 4 foundations), double-click to send a card up, draw one or three |
| Standard deck | `games/standard.jsonc`, `decks/standard_deck.csv` | A free-play sandbox: draw, play, discard, sort |

## Quick start

1. Open the project folder in Godot 4.7.
2. Press **F5** (Run Project).
3. Pick a game from the title screen. Every `.jsonc` file in `games/` is listed there, so a new game appears automatically.

**To start your own game,** press **New game...**: give it a name, a player count and a deck, and it writes a playable game file (draw a card, discard a card, random bots in the other seats) with comments explaining each part. Press **Edit file** to open it in your editor; every save reloads the table.

To jump straight into one game while working on it, open `scenes/main.tscn`, set **Rules Path** on the **Main** node in the Inspector, and press **F6** (Run Current Scene).

## Playing

| Control | What it does |
|---|---|
| Drag a card | Move it to the zone nearest where you drop it. While dragging, that zone and the card are outlined green if the move is allowed, or red if not. A refused drop snaps back and the log says why. |
| Dragging over a hand | The other cards move aside to show where the card will land: under the cursor, or in its sorted spot if the hand is sorted. |
| Drag a card within its own hand/row | Reorder it. This turns that zone's automatic sorting off. |
| Click a card | Does the game's action for clicking it, if there is one (e.g. click the deck to draw). Clicking an empty zone's outline can be an action too. |
| Choosing | When the game asks you to choose cards, the choices are outlined in yellow: click one, or for several, click each (selected cards turn blue; click again to unselect). When it asks for something else, like a color, pick from the bar of buttons above the table. |
| Toolbar buttons | The game's actions that aren't done by dragging or clicking. A button is greyed out while it can't be done. When it's your turn, the bar above the table says what you can do. |
| **Undo** / Ctrl+Z | Step back one input, including steps of a choice in progress. In a game with a bot, it goes back to your last input. |
| **Games** | Back to the title screen to pick another game. |
| **Reload** | Re-read the JSON and CSV files from disk and start over. Saving the game file or one of its decks does this by itself. |
| **Sandbox** | Ignore the rules and move anything anywhere, for testing. Rule follow-ups (like scoring) don't run in sandbox. |
| **Zone names** | Show every zone's name and card count above it: the names to use in rules (e.g. `won_1`). Off by default. |

The right-hand panel shows the game's variables and live readouts at the top, and a log of everything that happened below. The log only names cards you could see, so a card drawn into a bot's hidden hand shows as "1 hidden card". Errors appear in red. Card moves and flips are grouped, one line per step and destination, e.g. `deck → hand_1: 5♦, 4♥, K♦`. Identical lines in a row are shown once with a count, e.g. `if count(pile) == 0 → else (×17)`.

## Playing online

Press **Play online...** on the title screen.

1. **Host:** enter your name and press **Host** (port 7777 unless you change it). Windows may ask to let Godot through the firewall. Pick the game.
2. **Join:** the others enter the host's address and press **Join**. On the same network that's the host computer's local address (e.g. `192.168.1.20`); over the internet the host has to forward the port, or everyone can use a virtual network like Tailscale or ZeroTier. Nobody else needs the game's files: the host sends the rules and decks.
3. **Seats:** everyone presses **Sit here** on a seat. The host can tick **Bot** on any empty seat (if the game has a bot), then press **Start**.

During the match each player sees the table from their own seat: their hand and zones at the bottom, where seat 1's are in the game file, and everyone else moved round the table to match (so in Hearts the next player is always on your left). Each player sees only their own hidden cards, and acts only on their own turn. Bots are named by seat ("Player 2 (bot)"). Bots run on the host's computer. **Undo** asks everyone else at the table first. Reload and Sandbox are off. If someone drops out, their seat shows "(away)" and the game waits; they get their seat back by joining again with the same name. Pressing **Games** leaves the match (if the host leaves, it ends for everyone).

## The game editor

Select a game on the title screen and press **Open in editor** (**New game...** opens its new game there too). The editor changes the same game file you'd edit by hand, keeping your comments and layout, and only writes it when you press **Save**.

- **Left:** the sections of the game file, and the entries in the chosen one (e.g. the zones in **Table**).
- **Middle:** the table. In **Table**, every zone is outlined and named: click one to select it, drag it to move it. A per-player zone moves seat 1's spot (the others follow round the table), and a grid of copies moves as a whole. Switch on **Play** to play the game as it stands, unsaved changes included.
- **Right:** everything about the selected entry, with a line of help under each setting. **×** takes a setting out, back to its default. A zone's **note** is the `//` comment above it in the file.
- **Deck:** the deck as a table, one row per card, with the selected card drawn using the game's card face. Add, copy and remove cards and columns; the card face settings are beside it.
- **Players:** how many, their names, where their name tags sit, teams, and which seats bots play (with `prefer` to make them play well).
- **Variables:** the game's, each player's and each team's variables, with their starting values written as in the file (`5`, `"draw"`, `true`, `[]`).
- **Actions, Triggers, Conditions:** one entry each. An action written as just a list of steps (a button) gets the full form (drag, click, when, choose...) with **More settings**.
- **Turn:** the steps of a turn, with the `offer(...)`s that say what a player may do when.
- **Steps** are edited as a list: plain steps, **if** blocks (with an optional **else**), **for each** and **repeat while**, each foldable with ▾. **⋮** moves or removes a step, **+ Add** adds one, and **Edit as text** shows the whole list as JSONC. While typing, a step box suggests zones, variables, card columns (after `card.`) and building blocks, shows the signature of the building block you're in, and flags problems such as an unknown name.
- **Scoring:** the `status` readouts (a name and what it shows) and which variables the side panel lists. **Melds:** the kinds of meld for rummy-style games.
- **Add recipe** adds a ready-made piece of game: a draw-then-discard turn, dealing to each player, reshuffling the discard, playing a matching card, following suit and taking tricks, or bots. It adds what's missing (zones, variables, actions, the turn, trigger steps) and leaves what the game already has; a note then says what to adjust. Recipes are game files in `templates/recipes/`: add your own there, with `//` lines at the top saying what it does.
- **Bottom:** problems with the game, checked as you edit. Double-click one to go to what it's about.
- **Undo** and **Redo** (Ctrl+Z, Ctrl+Y) step through your edits. **Games** goes back to the title screen (asking first if there are unsaved changes).

## Tutorial: making a game

We'll build **Color Match**, a small UNO-like game. It's in the project already, so you can compare as you go. (For a quicker start on your own idea, **New game...** on the title screen writes a small playable game file to change, using the same pieces.)

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
- `on_<zone>` (optional) is a **card effect**: what happens when the card enters that zone. Here, a +2 entering the pile runs `draw_two`, a trigger we'll define in the game file in [step 6](#6-define-card-effects).

Every other column (`color`, `number`, `ink`) is yours to use in rules, sorting, and the card face.

Save it in `decks/` and that's all. (The `decks/` folder holds an empty `.gdignore` file, which stops Godot treating CSV files as translations. As a result the decks don't appear in Godot's FileSystem panel: edit them in a spreadsheet or text editor. To export the project as a standalone game, add `decks/*` to the export preset's **Filters to export non-resource files**.)

### 2. Lay out the table

Create `games/color_match.jsonc`. Start with the **zones**: the named places cards can be.

```json
{
	"players": 2,
	"zones": {
		"deck": { "label": "Deck", "layout": "pile", "face": "down", "anchor": [0.4, 0.5], "cards": "res://decks/color_match.csv" },
		"pile": { "label": "Pile", "layout": "pile", "anchor": [0.6, 0.5] },
		"hand": { "per_player": true, "layout": "fan", "face": "private", "anchors": [[0.5, 0.83], [0.5, 0.16]] }
	},
	"card_face": { "corner": "number", "title": "number", "body": ["color"], "color": "ink" }
}
```

- `anchor` is where the zone sits, as fractions of the table: `[0.5, 0.5]` is the center, `[0.5, 0.83]` is near the bottom.
- `"label": "Pile"` (optional) is shown faintly inside the zone's outline while it's empty. The zone's real name (`pile`) is shown with the **Zone names** toggle.
- `layout` is how the cards spread out: `pile` (stacked), `row`, or `fan`.
- `"cards"` puts the whole deck in that zone at the start.
- `"per_player": true` makes one hand per player, `hand_1` and `hand_2`, placed at the two `anchors`.
- `"face": "private"` shows each hand only to its owner: when two people share a screen, only on its owner's turn; against bots, only yours; online, only on its owner's screen.
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

`deck_empty` runs when `draw` needs a card from an empty deck. Here it shuffles the pile, minus its top card, back into the deck. Color Match draws by dragging rather than with `draw`, so its Draw action (below) triggers `deck_empty` itself when the last card is taken.

### 4. Say what players can do

**Actions** are the things players can do, each with a name. An action with `"drag"` is done by dragging a card:

```json
"variables": { "winner": null },
"actions": {
	"Play a card": {
		"drag": { "from": "hand", "to": "pile" },
		"when": "card.data.color == top(pile).data.color or card.data.number == top(pile).data.number",
		"steps": [{ "if": "count(hand) == 0", "then": ["winner = player", "end_game()"] }]
	},
	"Draw": {
		"drag": { "from": "deck", "to": "hand" },
		"click": true,
		"steps": [{ "if": "count(deck) == 0", "then": ["trigger('deck_empty')"] }]
	}
}
```

- `"drag"` says how the action is done: by dragging a card from `hand` to `pile`. `hand` means **the current player's** hand, so you can only play your own cards. (`opponent_hand` is the other player's.)
- `"click": true` lets the player click the deck instead of dragging from it.
- `when` must be true for the action to be allowed. `card` is the card being dragged, and `card.data.color` reads its `color` column (`card.color` works too; see [Steps](#steps)).
- `steps` run after the card has moved. Here: if your hand is now empty, you win and the game is over. Taking the last card from the deck refills it from the pile.
- `winner` is a **variable** we declared. Variables show in the side panel, and a step like `winner = player` changes one.

Once a game has drag actions, only the drags they allow can be made.

### 5. Take turns

The **turn** says what a player does on their turn, in order. It runs again and again, for whoever's turn it is:

```json
"turn": [
	"offer(['Play a card', 'Draw'])",
	{ "if": "winner == null", "then": ["end_turn()"] }
]
```

- `offer(...)` waits until the current player does one of those actions, by dragging or clicking. Until then, nothing else in the turn happens. Play a card and Draw can *only* be done while they're offered, so nobody can play out of turn or twice.
- `end_turn()` passes the turn to the next player, and the turn starts again for them.

### 6. Define card effects

The deck's `on_pile` column says `draw_two`. Define what that means once, as a trigger in the game file, and every card that names it shares it:

```json
"triggers": {
	"draw_two": ["draw(deck, opponent_hand, 2)"],
	...
}
```

A card's effect runs when the card enters the zone, before the action's `steps` (and so before the turn passes). So at that point `opponent_hand` is still the player who's about to take their turn.

### 7. Add buttons

```json
"actions": {
	"Sort by color": ["sort(hand, ['color', 'number'])"],
	"Sort by number": ["sort(hand, ['number', 'color'])"]
}
```

An action that isn't done by a drag or click is a toolbar button that runs its steps. These two are never offered, so they can be pressed at any time. Reload, and the game is playable.

### 8. Iterate

Change anything and save: the game reloads by itself. If something is wrong, the log tells you in red, usually as soon as the game loads:

```
Step "draw(deck, hnad)": unknown zone or variable "hnad"
```

When a drag is refused, the log shows the rule's condition so you can see which part failed. Turn on **Sandbox** to set up a situation by hand.

### 9. Add wild cards

The finished `color_match.jsonc` goes one step further: Wild cards that let you choose the color to match. Four `Wild` rows in the deck have `wild` in their `on_pile` column, and the game file adds:

```json
"variables": { "winner": null, "active_color": "" },
"triggers": {
	"wild": ["active_color = choose(['Red', 'Blue', 'Green', 'Yellow'], 'Choose a color')"],
	...
}
```

- `choose(...)` stops the steps and waits for the player to pick one of the colors (buttons above the table, under the prompt "Choose a color"); then `active_color` is set to it and the steps carry on.
- The card's effect runs, choice and all, before Play a card's `steps`, which now keep the chosen color: they only copy the card's color when the card isn't a Wild (`{ "if": "card.data.color != 'Wild'", "then": ["active_color = card.data.color"] }`), and Play a card's `when` matches `active_color` instead of the top card's color.
- So the first card turned up can't be a Wild or a +2 (whose effects would fire before anyone has played), `start` shuffles until the top card has no effect: `{ "while": "text(top(deck), 'on_pile') != ''", "do": ["shuffle(deck)"] }`.

## Game file reference

Everything a game file can contain. Only `zones` is required. A misspelled key (`"trigers"`, `"anchr"`) is reported when the game loads, rather than quietly ignored.

**File format:** game files are **JSONC** (`.jsonc`): JSON that also allows comments and trailing commas, so most editors and linters understand them. `//` starts a comment that runs to the end of the line, e.g. `"players": 2, // you and a bot`, and `/* ... */` can span lines. (Inside quoted text it's just text, so `"res://decks/..."` is fine.) A comma after the last item of a list or object is allowed. Plain `.json` files work too. To export the project as a standalone game, add `games/*.jsonc` to the export filter, as for decks.

**Lists of steps:** wherever a list of steps goes (`then`, `else`, `do`, `steps`, `turn`, triggers), a single step will do: `"then": "end_turn()"`.

### `title`, `description`

The name and one-line description shown on the title screen, e.g. `"title": "Gin Rummy"`. Without a title, the file name is used (`gin_rummy.jsonc` → "Gin Rummy").

### `zones`

| Key | Meaning | Default |
|---|---|---|
| `label` | Text shown inside the zone's outline while it's empty, e.g. `"Discard"`, or `"K"` for an empty solitaire column. In per-player zones and copies, `#` becomes the number: `"Tricks #"` shows "Tricks 1", "Tricks 2", ... | none |
| `outline` | `false` draws no outline or label, for zones that are only used in passing (e.g. the deck in Hearts) | `true` |
| `layout` | `pile`, `row`, `fan`, `column` (cascading downward from the anchor, face-down cards overlapping more, squeezing to fit; for solitaire), or `melds` (groups the cards into their melds; for rummy tables) | `row` |
| `face` | `up`, `down`, `private` (seen only by its owner: on their turn when people share a screen, always against bots, on their own screen online), or `others` (face up for everyone except its owner, like holding cards facing out in Hanabi; bots can't see their own either). Cards take this face when they enter the zone. (`owner` and `human` are older names for `private`.) | `up` |
| `anchor` | Position on the table as `[x, y]` fractions. In a `per_player` zone, it's seat 1's spot (usually the bottom of the table), and the other seats' are worked out round the table: with 4 players, seat 2 on the left, seat 3 across the top, seat 4 on the right, with the side seats' cards turned sideways. | `[0.5, 0.5]` |
| `width` | Widest a `row`/`fan` may spread, as a fraction of the table width (of its height, for a zone turned sideways) | 800 pixels |
| `rotation` | Turn the zone, in degrees, e.g. `90` for a hand on the left side of the table | `0` |
| `cards` | A deck to start with, e.g. `"res://decks/my_deck.csv"` | empty |
| `columns` | With `cards`: columns to add or change on every card as the game loads, each an expression about `card`, worked out in order. E.g. Hearts uses the standard deck with `{ "value": "14 if card.rank == 'A' else card.value", "points": "13 if card.name == 'Q♠' else int(card.suit == 'Hearts')" }` (aces high, and the scoring cards) | |
| `spread` | For a `pile`: fan out its top few cards sideways so they can be read, e.g. `3` for a draw-three solitaire waste | `0` |
| `drag` | `"stack"`: dragging a card also carries every card on top of it | one card |
| `per_player` | `true` makes one zone per player: `hand_1`, `hand_2`, ... | `false` |
| `copies` | A number: makes that many zones, e.g. `"copies": 7` makes `tableau_1` ... `tableau_7` | |
| `anchors` | With `per_player` or `copies`, one `[x, y]` position each, to place them yourself | |
| `labels` | With `per_player` or `copies`, one label each, e.g. `["Red", "Yellow", "Green"]` | |
| `rotations` | With `per_player` or `copies`, one rotation each, e.g. `[0, 90, 180, 270]` for four players around a table | |
| `covered_by` | With `copies`, which copies lie on top of each one, as one list of copy numbers per copy, e.g. Pyramid's `[[2, 3], [4, 5], ...]` (copy 1 is covered by 2 and 3). `covered(zone)` is then true while any of those still hold a card | |
| `grid` | With `copies`, instead of `anchors`: lay the copies out in rows from `anchor`, e.g. `{ "columns": 5, "spacing": [0.09, -0.19] }` puts 5 per row, 0.09 apart, each row 0.19 higher than the last | |
| `group` | With `copies`, also put them in a group with this name, shared with other zones that name it. Dominion's `basic_pile` and `kingdom_pile` copies are both in `"group": "supply"`, so `tops(supply)` and `"from": "supply"` cover all 17 piles | |

**Per-player zones and copies** are named the same way but mean different things in rules and steps:
- **Per-player:** `hand` means the current player's hand, and `opponent_hand` the next player's. `all_hand` is every player's hand, as a list (e.g. `count(all_played) == 4`).
- **Copies:** the plain name means the whole group. In an action's `drag`, `"to": "foundation"` matches any of the foundations. In a step, `foundation` is the list of all of them, e.g. `count(foundation)` counts every card on every foundation, and `try_move(card, foundation)` moves to the first one that allows it.

### `players`, `variables`, `player_variables`

- `"players": 2` turns on turns. The `player` variable holds whose turn it is (starting at 1); `end_turn()` passes the turn to the next player. To jump to a specific player, `player = ...`, e.g. the winner of a trick.
- `"seats": [[0.24, 0.86]]` (optional) draws a name tag for each player: give seat 1's spot and the others go round the table like `per_player` zones, or one `[x, y]` per player to place them yourself. The current player's tag is highlighted, and bots' tags say "(bot)".
- `"player_names": ["You", "West", "North", "East"]` (optional) names the players on their tags; otherwise they're "Player 1", "Player 2", ...
- `players` is the list of player numbers (`[1, 2, 3, 4]`). Loop over it with `"for": "player"` to do something for every player as if it were their turn (see [Loops](#loops)).
- `"variables": { "phase": "draw", "taken": null }` declares variables and their starting values.
- `"player_variables": { "score": 0 }` makes one per player (`score_1`, `score_2`). In steps, `score` means the current player's and `opponent_score` the other player's, just like `hand` and `opponent_hand`.
- `"teams": [[1, 3], [2, 4]]` makes partnerships: each list is a team (team 1, team 2, ...), and every player must be on exactly one. Then:
  - `"team_variables": { "team_score": 0, "bags": 0 }` makes one per team (`team_score_1`, `team_score_2`). In steps, `team_score` means the current player's team's and `opponent_team_score` the next player's team's.
  - `team_<zone>` is the per-player zones of everyone on the current player's team, e.g. `count(team_won) / 4` for the tricks a partnership has won.
  - `team(player)` gives a player's team number, e.g. `team(owner(best_card)) == team(player)` for "my side is winning this trick".
  - To do something once per team, loop over one player from each: `{ "for": "player", "in": "[1, 2]", "do": [...] }`.

### `actions`

Everything a player can do, each with a name. How it's done:

- **A drag:** `"drag": { "from": "hand", "to": "pile" }`. Zone names, or the name of a group of `copies` to match any of them, or a list of those (`["pyramid", "waste"]`); leave one out to mean "any zone". In `when` and `steps`, `card` is the dragged card, `from` the zone it came from and `to` where it's going. Add `"click": true` (or `"double_click": true`) to let a click on the card do the drag too, to the first zone it's allowed to go to.
- **A click:** `"click": "deck"` (a zone, a group or a list): clicking a card there (`card`), or the empty zone (`card` is `null`). `clicked_zone` is the zone clicked, which tells copies apart. `"double_click"` likewise.
- **A button:** anything else is a toolbar button, in the order listed. A drag or click action can have one too, with `"button": true`: pressing it asks which card to do it with.

```json
"Shuffle": ["shuffle(deck)"],
"Tableau to foundation": {
	"drag": { "from": "tableau", "to": "foundation" },
	"double_click": true,
	"when": "card == top(from) and number(card, 'value') == count(to) + 1",
	"steps": ["flip(face_down(top(from)))"]
},
"Turn the stock": { "click": "stock", "steps": ["draw(stock, waste)"] }
```

**When it's allowed:** an action named in an [`offer(...)`](#turn) can only be done while that offer is waiting (on that player's turn, at that point in it). Any other action can be done whenever its `when` holds, like sorting your hand. For a drag or click, the first action in the file that allows it is done. With no drag actions at all, every drag is allowed (a sandbox). When a zone drags stacks, the action is checked for the grabbed card and the cards on top of it come along.

| Key | Meaning |
|---|---|
| `steps` | What the action does. |
| `drag` | Done by dragging a card: `{ "from": ..., "to": ... }`. |
| `click`, `double_click` | Done by clicking a card in these zones (or the empty zone). On a drag action, `true`: clicking the card does the drag. |
| `when` | Only allowed while this is true; a button is greyed out. |
| `choose` | For a button: what to choose from. If it gives cards (e.g. `"hand"`), they're highlighted for you to click. If it gives other values (e.g. `"['Red', 'Blue']"` or `"numbers(0, 13)"`), they appear as buttons above the table. The steps then run with the pick as `choice` (and, for a single card, also as `card`). Clicking anything else cancels. |
| `pick` | How many cards to choose, e.g. `3` to pass three cards, or an expression like `"count(hand) - 3"`. `choice` is then a list of cards. |
| `up_to` | Choose any number of cards from none up to this many, then press **Done** in the bar above the table. A number, or an expression like `"count(hand)"` for "any number". `choice` is a list of cards (maybe empty). Use `pick` or `up_to`, not both. |
| `allow` | A condition the whole choice must meet, checked with the pick as `choice` before the steps run, e.g. President's `"count(choice) > 0 and size(groups(choice, 'rank')) == 1"` (one or more cards, all the same rank). A refused choice stays open and the log says why; bots only consider allowed choices. |
| `human_only` | `true` means bots never do it. |
| `button` | `false` hides a button from the toolbar, for actions only started with `ask('name')`; bots never press these directly either. `true` gives a drag or click action a button too. |
| `prefer` | For a bot making this action's choice, what to aim for instead of the game's `bot.prefer`, e.g. `"sum(hand, 'coins')"` to keep treasures when discarding |

### `turn`

The steps of one player's turn, run again and again for whoever's turn it is: `end_turn()` passes the turn on, and the steps start again for the next player. They wait for players with these building blocks:

| Building block | Does |
|---|---|
| `offer(['Action', ...])` | Waits until the current player does one of these actions, and gives its name. Those actions can only be done while offered; other actions any time. With none of them possible right now, it gives `null` straight away. |
| `offer_until(['Action', ...], 'Last')` | Offers the actions again and again until the player does the last one, e.g. play any number of duos, then End turn: `offer_until(['Play duo', 'End turn'], 'End turn')` |
| `end_game()` | The game is over: the turn doesn't start again, and nothing more is offered. |

Sea Salt + Paper's turn:

```json
"turn": [
	"offer(['Draw two', 'Take a discard'])",
	"offer_until(['Play duo', 'End turn'], 'End turn')",
	{ "if": "extra_turn", "then": ["extra_turn = false"], "else": ["end_turn()"] }
]
```

- **Who plays:** whoever's turn it is when the steps start (`player`). Steps can change it (`player = owner(best_card)`) before ending, so the winner of a trick leads.
- **Starting:** after `start`, and again whenever nothing else is running or being chosen.
- **Without a `turn`,** players do whatever their actions allow, whenever they allow it: right for solitaire, or games that handle turns in their actions' steps with `end_turn()`.
- **Safety:** turns that keep ending without waiting for anyone (100 in a row) stop with an error rather than freezing the game, as do turns after a step in them fails.

### `triggers`

Named step lists:

| Name | Runs when |
|---|---|
| `start` | The game begins (and on Reload), before the first `turn` |
| `turn_start` | After every `end_turn()` |
| `<zone>_empty` | A `draw` needs a card from that zone and it's empty, e.g. `deck_empty`. For a per-player zone (`deck_2`), `deck_empty` runs as that zone's owner, so `["move(discard, deck)", "shuffle(deck)"]` refills whoever ran out. |
| any other name | You call it with `trigger('name')`, or a card names it in its `on_<zone>` column (a **card effect**). Handy for routines used in several places, like a `deal`. |

As a card effect, a trigger runs with `card` as the card that moved, e.g. `"wild": [{ "if": "count(hand) > 1", "then": ["phase = 'choose_color'"] }]`. Every trigger is checked when the game loads, even ones nothing uses yet. (Older game files put card effects in a separate `"effects"` section; that still works.)

### `conditions`

Named expressions, so a check used in several places is written once:

```json
"conditions": {
	"top_card": "card == top(from)",
	"fits_tableau": "(count(to) == 0 and number(card, 'value') == 13) or (number(card, 'value') == number(top(to), 'value') - 1 and card.data.color != text(top(to), 'color'))"
},
"actions": {
	"Waste to tableau": { "drag": { "from": "waste", "to": "tableau" }, "when": "fits_tableau" },
	"Tableau to tableau": { "drag": { "from": "tableau", "to": "tableau" }, "when": "card.face_up and fits_tableau" },
	"Foundation to tableau": { "drag": { "from": "foundation", "to": "tableau" }, "when": "top_card and fits_tableau" }
}
```

- **Where they work:** a condition's name can be used in any expression: `when`, `if`, `status`, steps, other conditions.
- **How they work:** it behaves exactly as if its text were written there in brackets, so `card`, `from` and `to` mean whatever they mean at that spot.
- **Refused drags:** the log quotes the names (`requires Tableau to tableau: card.face_up and fits_tableau`), which keeps refusal messages short.
- **Not just true/false:** a condition can be any expression, e.g. `"rank": "number(card, 'value')"`.

### `show_variables`

By default every variable is listed in the side panel. `"show_variables": ["phase", "actions", "buys", "coins"]` lists only those, in that order, and hides bookkeeping variables.

### `status`

Live readouts in the side panel, recalculated after every change:

```json
"status": { "scores": "'%s %d, %s %d' % [name_of(1), score_1, name_of(2), score_2]" }
```

Write readouts so they read right on every screen: name players with `name_of`, and use `viewer()` for anything personal ("your points"). Online each player's screen shows their own seat at the bottom, so text like "You" or "West" baked into a label would be wrong for everyone but seat 1.

### `card_face`

| Key | Meaning |
|---|---|
| `corner` | Column shown small in the top-left |
| `title` | Column shown large in the middle |
| `body` | List of columns shown below the title |
| `text` | Column with rules text, drawn small and word-wrapped under the title (long titles also shrink to fit) |
| `zoom` | e.g. `2.4`: hovering a face-up card shows an enlarged copy beside it, so small text can be read |
| `color` | Text color: a column holding each card's color (a name like `Red` or a code like `#c0392b`), or one color for every card, e.g. `"#333333"` |
| `background` | Card face color, the same way: a column (each card its own, e.g. a `paper` column of soft colors) or one color for every card, e.g. `"#f3e9d2"`. White if not given |

Without a `card_face`, cards show their `name` in the corner and the middle.

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

Computer players. The simplest bot just lists its seats, and plays a random legal move each time, which is enough to play a prototype through:

```json
"bot": { "players": [2, 3, 4] }
```

To make it play well, add `prefer`, an expression for how good a position is from the bot's point of view:

```json
"bot": { "players": [2], "prefer": "score - opponent_score - sum(deadwood(hand), 'points')" }
```

With `prefer`, on a bot's turn, it tries every legal input: each allowed drag, each click that does an action, each enabled button, and each choice for `choose` buttons. It scores the result of each with `prefer`, from its own point of view, and takes the highest. It plays one input at a time so you can watch. When an input opens a choice (playing Chapel, say), it's scored by the best way of making that choice.

Write `prefer` as "how good is this position for me?". Things to know:
- **It only looks one input ahead.**
- **It doesn't peek.** Cards it couldn't see (face down, even its own, like the order of its deck; or in another player's `private` hand) are shuffled among themselves before each try, several times, and the results averaged. So drawing from the deck is judged on what it might get, not what's really there. Its own hidden cards are only shuffled among themselves: it knows what's in its deck, just not the order.
  - `"samples": 4` sets how many shuffles (more is fairer but slower).
  - `"peek": true` lets the bot see everything, which is stronger and faster.
- **It never makes inputs that only reorder cards,** such as sorting.

## Steps

A step is one line of text that does something or calculates something, like `draw(deck, hand, 2)`.

You can use:
- **Zone and variable names** directly.
- **`card`**: the card involved (dragged, clicked, chosen, or whose effect is running). It's `null` when there isn't one.
- **`clicked_zone`**: in an action done by a click, the zone that was clicked (one copy, not the whole group). It's `null` elsewhere.
- **`from`, `to`**: in an action done by a drag, the zone the card came from and the one it's going to.
- **`choice`**: what was picked in a `choose` action: a card, a list of cards, or a value.
- **Names you set,** like `pile = choose(discard_piles)`: a name that isn't one of the game's `variables` is remembered by the list of steps that sets it (the trigger, button or rule, including its `if`s and loops), for later steps in it to use. Nothing else sees it, and it's gone when they finish. A name that's set but never read is reported when the game loads, since it's most likely a misspelled variable.
- **Card columns,** as `card.<column>`, e.g. `card.suit == lead` or `card.value > 5`. Capitals don't matter: `card.color` reads a column headed `Color`. A column where every card has a number (`value`) gives numbers; a column with any text in it (`rank`: 2 ... 10, J, Q, K, A) gives text for every card, so `card.rank == 'A'` and `card.rank == '10'` both work. `card.data.<column>` is always the text as written.
- **Operators:** `==`, `!=`, `<`, `>`, `<=`, `>=`, `+`, `-`, `*`, `/`, `%`, `and`, `or`, `not`, and parentheses. `+` also joins text and lists.
- **Either-or values:** `'out' if count(hand) == 0 else 'in'`.
- **Changing a variable:** a step of its own like `phase = 'discard'`, `score += 10` or `passes -= 1`. `score` is the current player's, as everywhere; `score_2` is player 2's. Only declared [variables](#players-variables-player_variables) can be changed, and only in steps, not in conditions like `when` (where `=` is a mistake for `==`).
- **Text with numbers in it:** `'bid %d, score %d' % [team_bid_1, team_score_1]`, handy in [`status`](#status).
- **Lists:** `[1, 2]`, joined with `+` (`finish_order + [player]`) and read by position from 0 (`ranking[0]`).
- **Math:** `abs`, `min`, `max`, `floor`, `ceil`, `round`, and `int` (which also turns `true`/`false` into `1`/`0`, e.g. `5 * int(team_winning)`).
- **Text in single quotes,** e.g. `'discard'`. The JSON itself uses double quotes. A quote inside text takes a backslash, `'King\'s Court'`, which the game file's JSON doubles: `"where(box, 'name', 'King\\'s Court')"` (in a deck's cell, one backslash is enough).

Every step is checked when the game loads (spelling, unknown names, wrong number of arguments), so most mistakes show up right away. If a step fails while running, the rest of its list is skipped and the log says why.

### Conditional steps

There's no inline "if" in a step, so use a conditional step anywhere a list of steps is allowed:

```json
{ "if": "count(hand) > 7", "then": ["phase = 'discard'"], "else": ["phase = 'draw'"] }
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
- **Each other player chooses:** inside a loop over players, `choose(...)` asks each of them in turn: `{ "for": "player", "in": "players", "do": ["move(choose_exactly(hand, 3, 'Pass three cards'), played)"] }` (Hearts). `ask('Discard down to 3', player)` instead queues an action's choice for each of them, to be made after the current steps finish (Militia).
- **Every player:** `{ "for": "player", "in": "players", "do": [...] }` runs the steps once per player as if it were their turn, so `hand`, `score` and `opponent_hand` follow along: `{ "for": "player", "in": "players", "do": ["score += sum(won, 'points')"] }` scores everyone's won cards. The turn goes back to whoever had it after the loop.

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

### Choosing in the middle of steps

`choose(...)` stops the steps until the current player picks something, then gives what they picked, so a step can use it straight away:

```json
"crab": [
	"pile = choose(discard_piles, 'Look through which pile?')",
	"move(choose(pile, 'Take a card'), hand)"
]
```

- **What can be chosen:** the cards of a zone or list (`hand`, `matching(tops(supply), 'cheap')`), the zones of a list of zones (`discard_piles`, `[deck, discard]`: the player clicks a zone or its button), or values (`['Red', 'Blue']`, `numbers(1, 6)`: buttons above the table). The prompt, if given, is shown above the table.
- **Several cards:** `choose_exactly(hand, 3, 'Pass three')` waits for exactly 3 and gives a list; `choose_up_to(hand, 4, 'Trash up to 4')` lets the player pick any number up to 4 and press **Done** (the list may be empty).
- **Who chooses:** the current player. Inside a loop over players (`"for": "player"`), each player in turn.
- **While waiting** nothing else happens: no buttons, drags or clicks except the choice itself. Undo goes back to before the choice was made.
- **No real choice:** with nothing to pick, `choose` gives `null` and the others `[]`, without asking; `choose_exactly` with no more cards than it asks for gives them all.
- **Steps it started:** a trigger (or a card's effect) that chooses holds up the step list that called it, which carries on once it's done: `trigger('crab')`, then the next step.
- **Not in `when`s, conditions or `status`:** they're checked all the time, and only steps can wait.
- **When an action is better:** a button the player presses (`"choose"` on an action) can be cancelled by clicking elsewhere, can check the whole choice with `allow`, and can give bots a `prefer` of its own. `choose(...)` can't (yet), so bots judge it by the game's `bot.prefer`.

### Building blocks

**Changing things**

| Building block | Does |
|---|---|
| `draw(from, to, amount)` | Move the top card(s) of `from` to `to`. `amount` defaults to 1. Fires `<from>_empty` if it runs out. |
| `deal(from, to, amount)` | Deal `amount` cards to each zone in `to`, one card at a time in order, e.g. `deal(deck, all_hand, 13)`. To start with a particular player, list the hands in that order: `deal(stock, [hand, opponent_hand], 10)`. |
| `move(cards, to)` | Move cards to a zone. |
| `try_move(card, to)` | Move a card only if a drag action allows it, and do that action. `to` can be a group of zones (`foundation`); the first one that allows it is used. Does nothing if `card` is `null`. |
| `shuffle(zone)` | Shuffle a zone. |
| `flip(cards)` | Turn cards over (until they move to another zone). |
| `mark(cards, 'key', value)` | Put a note on cards, e.g. a clue `mark(where(opponent_hand, 'color', 'Red'), 'color', 'Red')`, a counter `mark(card, 'damage', 2)`, or `mark(card, 'tapped', 'tapped')`. Marks are drawn on the card (on its back too) and last until it moves to another zone. A mark named after a column (here `color`) tells bots what that card is, even when they can't see it. |
| `unmark(cards, 'key')` | Remove that mark. |
| `sort(zone, ['column', ...])` | Keep a zone sorted by these columns, first column first. Numbers sort numerically. `sort(zone, [])` turns sorting off. |
| `end_turn()` | Pass the turn to the next player and fire `turn_start`. |
| `trigger('name')` | Run a step list from `triggers`, right after this step (before the next one). It runs with the same `card` as the step that called it. |
| `ask('Action name', player)` | Start that action (usually a `choose`) once the current move finishes. (To choose in the middle of steps, [`choose(...)`](#choosing-in-the-middle-of-steps) is usually simpler; `ask` is for actions with their own `allow` or bot `prefer`.) Several asks wait in line: each starts when the previous choice is finished. With a player number, that player makes the choice: it's their turn until they've chosen (so `hand` means their hand, and a bot answers by itself), then the turn goes back. A choice started by `ask` must be made: clicking elsewhere doesn't cancel it, and other buttons and drags wait. |
| `ask_all('Action name', 'trigger')` | Everyone makes that choice at the same time, e.g. a secret bid: people choose first (so nobody waits on the bots), then each bot, then the trigger runs, e.g. to turn the bids over. Put the choices somewhere face down (`move(choice, bid)` with a face-down `bid` zone) so nobody, bots included, sees them before the reveal. Real-time play (Speed, Slapjack) isn't possible. |
| `ask_others('Action name', 'trigger')` | A chance to respond: every other player, in turn order, is asked that action (players whose `when` is false are skipped), then the trigger runs. If someone responds by calling `ask_others` again, the rest of the first round is called off and a new round starts around them, so the trigger runs once, after a round where nobody responds. That's how Kaboom's Nope works, including a Nope on a Nope: see `games/kaboom.jsonc`. |
| `run_effect(card, 'play')` | Run a card's `on_play` effect again, e.g. Throne Room playing an action twice |

**Choosing** (see [Choosing in the middle of steps](#choosing-in-the-middle-of-steps))

| Building block | Does |
|---|---|
| `choose(options, 'prompt')` | Wait for the current player to pick one card, zone or value from `options`, and give it, e.g. `color = choose(['Red', 'Blue'], 'Choose a color')` |
| `choose_exactly(cards, amount, 'prompt')` | Wait for exactly that many cards, and give them as a list, e.g. `move(choose_exactly(hand, 3, 'Pass three'), played)` |
| `choose_up_to(cards, most, 'prompt')` | Wait for any number of cards up to `most` (the player presses **Done**), and give them as a list, e.g. `trashed = choose_up_to(hand, 4, 'Trash up to 4')` |

**Questions**

| Building block | Gives |
|---|---|
| `all(zone)` | All the cards in a zone |
| `top(cards)` | The top card of a zone (or the last of a list), or `null` if empty |
| `highest(cards, 'column')`, `lowest(cards, 'column')` | The card with the biggest or smallest number in that column, e.g. the winner of a trick: `highest(where(all_played, 'suit', lead), 'value')` |
| `follows_suit(card, hand, lead)` | Whether playing the card follows suit: nothing led yet (`lead` is `''`), it's the suit led, or the hand has none of it. Uses the `suit` column |
| `trick_winner(cards, lead, trump)` | The card taking a trick: the highest trump played, if any, otherwise the highest of the suit led, e.g. `owner(trick_winner(all_played, lead, 'Spades'))` is who won. `trump` is optional. Uses the `suit` and `value` columns |
| `owner(card)` | The number of the player whose zone the card is in (0 for shared zones), e.g. `player = owner(highest(...))` |
| `next_player(seats)` | The player that many seats after the current one, wrapping around: `next_player(1)` is next, `next_player(-1)` is previous |
| `name_of(player)` | A player's name: from `player_names` ("You", "West"), or online the name they joined with. Use it instead of writing names into text, e.g. `result = 'Winner: ' + name_of(best)`, so the text is right on every screen |
| `viewer()` | The seat this screen belongs to (the person playing here), e.g. `"your deadwood": "sum(deadwood(zone('hand', viewer())), 'points')"`. Only allowed in `status`: each screen has its own, so rules can't depend on it |
| `variable_of('name', player)` | Another player's player variable, e.g. `variable_of('alive', next_player())` |
| `zone('kind', player)` | That player's zone, e.g. `zone('hand', next_player(2))` for the player across the table |
| `random(low, high)` | A random whole number from `low` to `high`, e.g. a die roll `random(1, 6)` or a random first player `player = random(1, 4)` |
| `one_of(items)` | A random card from a zone or list (`move(one_of(opponent_hand), hand)` steals a random card), or a random item from a list of values (`one_of(['Red', 'Blue'])`); `null` if there are none |
| `numbers(low, high)` | The list of whole numbers from `low` to `high`, e.g. for bids: `"choose": "numbers(0, 13)"` |
| `count(cards)` | How many cards |
| `number(card, 'column')` | A card's column as a number, for comparing, e.g. `number(card, 'value') > 5` (`0` if `card` is `null` or the column isn't a number) |
| `text(card, 'column')` | A card's column as text, or `''` if `card` is `null`. Use this instead of `card.data.column` when the card might be missing, e.g. `text(top(to), 'color')` |
| `sum(cards, 'column')` | The numeric total of a column |
| `where(cards, 'column', value)` | The cards whose column equals `value` |
| `without(cards, removed)` | The cards, minus the removed ones |
| `movable(cards, to)` | The cards that a drag action currently allows into `to` (a zone, a group like `foundation`, or a list like `[tableau, foundation]`). `count(movable(hand, pile)) == 0` means "can't play"; `"choose": "movable(hand, knock)"` offers only legal cards |
| `tops(zones)` | The top card of each zone in a list or group, e.g. `matching(tops(supply), 'costs_up_to_4')` to choose a card to gain |
| `matching(cards, 'condition')` | The cards for which a named [condition](#conditions) holds, checked with each card as `card`, e.g. `matching(hand, 'is_treasure')` with `"is_treasure": "card.data.type == 'Treasure'"` |
| `face_down(cards)` | The cards that are face down, e.g. `flip(face_down(top(from)))` turns the new top card up only if it's down |
| `covered(zone)` | Whether a card lies on it, by the zone's [`covered_by`](#zones). Also takes a card, meaning the zone it's in: `not covered(card)` in an action's `when` means "only uncovered cards" |
| `mark_of(card, 'key')` | A card's mark, or `null` if it has none, e.g. `mark_of(card, 'damage') >= 3` |
| `last(cards, amount)` | The last `amount` cards of a zone or list (the top of a pile), e.g. the cards just played: `last(pegging, 3)` |
| `team(player)` | A player's team number (needs [`teams`](#players-variables-player_variables)) |

**Combinations**

These find groups of cards, each as a list. `size(...)` counts the groups; `count(...)` counts all the cards in them.

| Building block | Gives |
|---|---|
| `size(list)` | How many items a list has. Unlike `count`, a group of cards counts as one item |
| `groups(cards, 'column')` | The cards grouped by equal values in that column, biggest group first. `size(groups(choice, 'rank')) == 1` means "all the same rank"; `size(groups(hand, 'suit')) == 1` is a flush; `count(groups(hand, 'rank')[0]) == 3` means the biggest set is three of a kind |
| `pairs(cards, 'column')` | Every two cards with the same value. A pair is one pair, three of a kind is three: `2 * size(pairs(cards, 'rank'))` scores pairs the cribbage way |
| `runs(cards, 'column', length)` | Every longest run of consecutive numbers at least `length` long (a straight). A repeated value makes one run per copy, so 3-4-4-5 is two runs of three: `count(runs(cards, 'order', 3))` scores runs the cribbage way, and `size(runs(cards, 'order', 5)) > 0` means there's a straight |
| `sums(cards, 'column', total)` | Every combination of cards whose column adds up to `total`: `2 * size(sums(cards, 'value', 15))` scores cribbage fifteens. It tries every combination, so use it on small hands (up to about 12 cards) |

**Melds** (need `melds` in the game file)

| Building block | Gives |
|---|---|
| `melds(cards)` | The arrangement of melds that leaves the fewest deadwood points |
| `deadwood(cards)` | The cards left out of those melds |
| `layoff(cards, melds)` | Which of `cards` to add onto those melds to leave `cards` the fewest deadwood points |

Wherever a building block takes `cards`, you can pass a zone, a single card, or a list of cards (lists inside lists are fine).

### Tips

- **Is the card there?** `card.value` fails if `card` is `null` (an empty zone's top card). `number(top(pile), 'value')` and `text(top(pile), 'suit')` give `0` and `''` instead.
- **`and`/`or` stop as soon as the answer is known**, so `count(to) > 0 and top(to).data.color == 'Red'` is safe on an empty zone: the second half isn't checked when the first is false.
- **Whole numbers stay whole.** Variables starting at whole numbers (`0`, `4`) are whole numbers, so `dealer % 4` and `['a', 'b'][index]` work. Numbers with decimals (`0.5`) stay decimal.
- **To deal the same way in several places,** put the steps in a custom trigger and call `trigger('deal')`.
- **"Draw only if you can't play"** is a drag action like `{ "drag": { "from": "deck", "to": "hand" }, "when": "count(movable(hand, pile)) == 0" }`. (An action can't use `movable` to check drags into its own destination; that inner check counts as "none" and the log says so.)
- **To repeat a check,** give it a name in [`conditions`](#conditions). **To repeat a step,** use a [loop](#loops), or [repeat until](#repeat-until) something changes.
- **Look at `games/color_match.jsonc` and `games/sea_salt_+_paper.jsonc`** for turns made of offers. `games/gin_rummy.jsonc` is a full game: turn phases, knocking, scoring, and a match to 100. `games/solitaire.jsonc` shows columns, stacks, and groups of zones. `games/cribbage.jsonc` scores combinations, and `games/spades.jsonc` has teams.

## Deck reference

`decks/standard_deck.csv` is an ordinary 52-card deck: `name` (`A♠`), `rank` (`A`, `2` ... `10`, `J`, `Q`, `K`), `suit`, `color` (`Red`/`Black`) and `value` (1 for an ace up to 13 for a king). Most card games can start from it, adding their own numbers with [`columns`](#zones) rather than making a new deck.

- **First row:** column names. Each row after it is one card; every column becomes readable as `card.data.<column>`.
- **`name`:** the card's title in the log.
- **`count`:** how many copies to make (blank = 1).
- **`on_<zone>`:** what happens when the card enters that zone. Usually the name of a trigger from the game file's [`triggers`](#triggers), e.g. `draw_two`.
  - A cell can also hold steps directly, separated by `;`, and mix them with trigger names, e.g. `draw_two; shuffle(deck)`.
  - For per-player zones, use the name without the number: `on_hand`, not `on_hand_1`.
  - A cell starting with `[` is read as a JSON list of steps, e.g. `[{"if": "count(hand) > 2", "then": ["move(card, pile)"]}]`. Usually it's tidier to make it a named trigger instead.
  - An effect that keeps triggering itself is stopped with an error instead of freezing the game.
- **Commas and quotes:** cells containing commas need quotes around them. Spreadsheet programs do this automatically when exporting CSV.
- **Saving:** save the file as UTF-8 so symbols like ♠ survive.

## Extending the engine

Building blocks live in `scripts/core/actions.gd`. Each public method there becomes usable in every game's steps, with argument checking and error messages included. To add one, write a new method with typed parameters, e.g.:

```gdscript
func discard_hand(zone: Zone, to: Zone) -> void:
	move(zone, to)
```

Other places to look:
- `scripts/core/`: the game logic. `Game` runs the rules, `StepCompiler` checks a game file as it loads, `StepParser` reads each step's text, `StepEvaluator` works it out and `StepRunner` runs lists of steps (pausing at a `choose` or `offer`, and starting each `turn`), `Zone` holds cards, `Melds` finds melds, `Bot` picks moves, `Snapshot` saves state for undo, `GameLog` writes the log.
- `scripts/ui/`: the table and side panel.
- `scripts/net/net.gd`: online play (the `Net` autoload): the lobby, sending inputs through the host, undo requests, rejoining.
- `tests/regression_test.gd`: loads every game and plays a few with bots. Run it after changing rules or the engine: `godot --headless -s tests/regression_test.gd`.
- `tests/editor_test.gd`: checks the editor keeps game files exactly as written while editing them, can rebuild every game from an empty file, and that the recipes load. Run `godot --headless -s tests/editor_test.gd`.
- `tests/ui_test.tscn`: opens the title screen, the lobby, and every game on the real table with bots playing. Run `godot --headless res://tests/ui_test.tscn`.
- `tests/turn_test.gd`: checks turns, offers and actions done by drags, clicks and buttons, plus the load errors for older game files. Run it after changing turns or actions: `godot --headless -s tests/turn_test.gd`.
- `tests/choose_test.gd`: checks steps that wait for a choice: cards, zones and values, loops, triggers, undo, bots and replays. Run it after changing `StepRunner` or choices: `godot --headless -s tests/choose_test.gd`.
- `tests/step_language_test.gd`: checks what steps work out to (operators, `x if c else y`, `=` and `+=`) and the messages for mistakes. Run it after changing the step language: `godot --headless -s tests/step_language_test.gd`.
- `tests/determinism_test.gd`: plays every game and checks matches replay exactly. Run it after engine changes: `godot --headless -s tests/determinism_test.gd`.
- `tests/net_test.tscn`: a host and a guest play a hand of Hearts over the network, with an undo, a rejoin and a catch-up. Run `godot --headless res://tests/net_test.tscn -- host` and, in a second terminal, the same with `-- guest`; the two final state numbers must match.

### Inputs, seeds and replays

Everything a player does reaches the game as plain data through `Game.apply(input)`, e.g. `{type = "drag", card = 12, to = "pile"}` or `{type = "perform", action = "Pass"}` (card numbers are the order cards were loaded in). The table, the bots and (later) online play all go through it, and the game keeps the list in `Game.inputs`.

- **Seeds:** `Game.new(path, seed)` makes a match repeatable. Every shuffle, `random` and `one_of` uses that seed's random numbers, so the same seed and inputs always produce the same game.
- **Replays:** `Game.record()` gives `{rules, seed, inputs}`, and `Game.replay(record)` rebuilds the match from it. `Game.state_hash()` sums up the whole state as one number, for checking two copies agree.
- **Turns:** an input with a `player` is refused unless that player may act: it's their turn or their choice, any person may press a `human_only` button, anyone may rearrange their own cards.
- **Seats:** `Game.local_seat` is the player this screen belongs to (0, the default, means people share the screen). With a seat set, `private` zones are only shown to their owner, and only that seat's turn is "your turn".
