# Card Game Prototyping Engine

Prototype card games in Godot 4.7 (the .NET build) without writing code. The cards come from a spreadsheet (CSV) and the rules from a game file (JSONC). The engine supplies the rest: the table, dragging, turns, choices, scoring, undo, bots, online play and an editor.

- **This page:** how to play, and a tutorial that builds a small game.
- **[docs/reference.md](docs/reference.md):** everything a game file and a deck can contain. Look things up there as you need them.
- **[docs/engine.md](docs/engine.md):** for changing the engine itself: its building blocks, tests and replays.

## Quick start

1. Open the project folder in Godot 4.7 (the .NET build, which runs C#) and press **F5**.
2. Pick a game on the title screen. Every file in `games/` is listed there. For a game that can be played by different numbers of players, choose how many beside **Play**.
3. To make your own game, press **New game...**. Give it a name, a player count and a deck, and it writes a small playable game for you to change. That game deals, lets players draw and discard, and puts bots in the other seats, and it has comments explaining each part. Change the game in the editor (**Open in editor**), or by hand (**Edit file**). Saving reloads the table.

To go straight into one game while you work on it, set **Rules Path** on the **Main** node of `scenes/main.tscn`, then press **F6**.

## Included games

| Game | Look at it for |
|---|---|
| Color Match | The tutorial below: a custom deck, card effects, and a Wild card that asks for a color |
| Gin Rummy | A turn of offers (take the upcard or pass, draw, then discard or knock), melds, a match to 100 |
| Sea Salt + Paper | Duo effects and combination scoring with conditions that take parameters, Stop / Last chance |
| Hearts | Four players, passing cards, following suit, tricks, shooting the moon |
| Spades | Teams, bids, bags and nil bids |
| Cribbage | Scoring combinations: fifteens, pairs, runs, flushes, the crib |
| Dominion | Deckbuilding: supply piles, choices (Cellar, Chapel, Remodel), attacks and reactions, a Big Money bot |
| Kaboom | Responding to other players' cards (Nope chains), knocking players out |
| President | Playing several cards at once, passing, finishing order |
| Goofspiel | Everyone bidding at once, face down |
| Fireworks | Co-op (Hanabi-style): holding your cards facing away, clues marked on cards |
| Pyramid, Solitaire | Solitaire: overlapping cards, cascading columns, dragging stacks, double-click to send a card up |
| Standard deck | A free-play sandbox |

All but Color Match use `decks/standard_deck.csv`, or a deck named after the game.

## Playing

| Control | What it does |
|---|---|
| Drag a card | Moves it to the zone nearest where you drop it. While you drag, the outline turns green if the move is allowed and red if not. A refused drop snaps back, and the log says why. Dragging within your own hand reorders it. |
| Click a card or zone | Does the game's click action there, if it has one (for example, drawing from the deck). |
| Choosing | The cards you can choose are outlined in yellow. Click them; picked cards turn blue. Anything else to pick, like a color, appears as buttons above the table. On your turn, clicking a card can also start a button's choice of several cards with it, as in President's Play. |
| Toolbar buttons | The game's other actions. A button is greyed out while it can't be done. The bar above the table says what you can do now. |
| **Undo** (Ctrl+Z) | Goes back one of your inputs, including one step of a choice. |
| **Reload** | Starts over from the files on disk. Saving a game file or deck reloads it automatically. |
| **Sandbox** | Ignores the rules so you can set up a situation by hand. |
| **Zone names** | Shows each zone's name and card count: the names your rules use. |

The side panel shows the game's variables and readouts, with a log of everything that happened beneath them. Cards you can't see are logged as "hidden card", and errors appear in red. When the game ends, a banner says who won (click it away), and every hand is shown.

**Online:** press **Play online...** on the title screen.

1. The host presses **Host** (port 7777) and picks the game, and how many play if it allows different numbers.
2. The other players press **Join** with the host's address. Use the local address on the same network. Over the internet, the host forwards the port, or everyone joins a virtual network like Tailscale.
3. Everyone is seated as they arrive (**Sit here** moves), the host can put bots in empty seats, and the host presses **Start**.

When a match ends, the host presses **Play again** (same game and seats, a new deal) or **Back to lobby** to pick another game; everyone stays connected and seated. The host's **Lobby** button ends a match early. The screen remembers your name, address and the host's game, and the title screen's **Host again** / **Rejoin** button goes straight back online.

Only the host needs the game's files. Each player sees the table from their own seat and only their own hidden cards. Undo asks the other players first. A player who drops out can rejoin under the same name.

## The editor

**Open in editor** on the title screen edits the same game file you would edit by hand. It keeps your comments and layout, and it only writes the file when you press **Save**.

- **Left:** the outline: every part of the game (zones, the deck, players, variables, actions, the turn, triggers, conditions, scoring, melds). **+** adds a zone, action, trigger or condition; double-click, F2 or right-click renames, copies or deletes one. The icons beside it switch to **Recipes** (ready-made pieces of game, such as a draw-then-discard turn, dealing or bots, from `templates/recipes/`) and **Building blocks** (every block with what it does; double-click one to put it in the step you're typing).
- **Middle:** the **Table** (drag zones to move them; **Play**, or F5, tries the game as it stands, unsaved changes included), the **Deck** as a spreadsheet, and the **Game file** itself, colored and kept in step with everything else.
- **Right:** the inspector: the settings of whatever is selected, with less-used ones folded away. Point at a setting to read what it does at the bottom. Steps are edited as a list, with suggestions and problems shown as you type; the turn also gets a summary in words.
- **Bottom:** problems with the game (Ctrl+J shows or hides them). Click one to go to it.
- **Ctrl+P** finds any command or part of the game. Ctrl+S saves, Ctrl+Z undoes.
- **File > Export to Tabletop Simulator** sets the game's table up in Tabletop Simulator, to play by hand, as it is once the game's `start` has run, so each export is a new deal. Cards are drawn with the game's own card faces; piles become decks, and each player's private zone becomes their hand. In Tabletop Simulator, **Load > Card Game Prototypes** opens it. The pictures stay on this computer, so to play online, upload them first (Tabletop Simulator's **Modding > Cloud Manager**) and put their new addresses in the deck (right-click it, **Custom**).

## Tutorial: making a game

We'll build **Color Match**, a small UNO-like game. It's already in `games/color_match.jsonc`, so you can compare as you go.

> Each player starts with 7 cards. On your turn, play a card matching the top of the pile by color or number, or draw a card. A +2 makes your opponent draw two. The first player to empty their hand wins.

### 1. Make the deck

A deck is a spreadsheet saved as CSV in `decks/`. The first row names the columns, and every other row is a card. Here's the start of `decks/color_match.csv`:

| name | color | number | ink | count | on_pile |
|---|---|---|---|---|---|
| Red 1 | Red | 1 | #c0392b | 2 | |
| Red +2 | Red | +2 | #c0392b | 2 | draw_two |
| Blue 1 | Blue | 1 | #2471a3 | 2 | |

- `name` is the card's title in the log.
- `count` is how many copies of the card to make.
- `on_pile` is a **card effect**: a +2 entering the pile runs `draw_two`, which we'll define in [step 6](#6-define-card-effects).
- The other columns (`color`, `number`, `ink`) are yours to use in rules and on the card face.

### 2. Lay out the table

Create `games/color_match.jsonc` and list the **zones**, the places cards can be:

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

- `anchor` is where the zone sits, as fractions of the table: `[0.5, 0.5]` is the center.
- `layout` is how the zone spreads its cards: `pile`, `row` or `fan`.
- `cards` puts the whole deck there at the start.
- `"per_player": true` makes one hand per player, `hand_1` and `hand_2`. `"face": "private"` shows each hand only to its owner.
- `card_face` picks which columns appear on a card, and which column holds its text color.

Run the project and pick **Color Match**. You'll see a face-down deck, with nothing dealt yet.

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

`all_hand` means every player's hand. `deck_empty` is our own trigger: it shuffles the pile back into the deck, all but the pile's top card. The next step uses it.

### 4. Say what players can do

**Actions** are the things players can do, each with a name:

```json
"variables": { "winner": null },
"actions": {
	"Play a card": {
		"drag": { "from": "hand", "to": "pile" },
		"when": "card.color == top(pile).color or card.number == top(pile).number",
		"steps": [{ "if": "count(hand) == 0", "then": ["winner = player", "end_game(player)"] }]
	},
	"Draw": {
		"drag": { "from": "deck", "to": "hand" },
		"click": true,
		"steps": [{ "if": "count(deck) == 0", "then": ["trigger('deck_empty')"] }]
	}
}
```

- `drag` says how the action is done. `hand` means *the current player's* hand, so you can only play your own cards.
- `"click": true` lets the player click the deck instead of dragging from it.
- `when` must be true for the action to be allowed. `card` is the card being dragged, and `card.color` reads its `color` column.
- `steps` run after the card has moved. If your hand is now empty, `end_game(player)` ends the game with you as the winner. If the deck is now empty, our `deck_empty` trigger refills it.
- `winner` is a **variable**. Variables show in the side panel, and a step like `winner = player` changes one.

### 5. Take turns

The **turn** is what a player does on their turn. It runs again and again, for whoever's turn it is:

```json
"turn": ["offer(['Play a card', 'Draw'])", "end_turn()"]
```

- `offer(...)` waits until the current player does one of those actions. Offered actions can *only* be done while the offer waits, so nobody can play out of turn or play twice.
- `end_turn()` passes the turn to the next player.

### 6. Define card effects

The deck's `on_pile` column says `draw_two`. Define it once, as a trigger, and every card that names it shares it:

```json
"draw_two": ["draw(deck, opponent_hand, 2)"]
```

A card's effect runs as the card arrives, before the action's `steps` and before the turn passes. So `opponent_hand` is still the hand of the player who's about to take their turn.

### 7. Add buttons

An action without `drag` or `click` is a toolbar button. These two aren't offered by the turn, and `any_time` lets either player press them even on the other's turn, sorting their own hand:

```json
"Sort by color": { "any_time": true, "steps": ["sort(hand, ['color', 'number'])"] },
"Sort by number": { "any_time": true, "steps": ["sort(hand, ['number', 'color'])"] }
```

The game is now playable.

### 8. Iterate

Change anything and save, and the game reloads. Mistakes show in the log in red, usually as soon as the game loads:

```
Step "draw(deck, hnad)": unknown zone or variable "hnad"
```

When a drag is refused, the log shows the `when` it failed. Turn on **Sandbox** to set up a situation by hand.

### 9. Add wild cards

The finished game adds four Wild cards, with `wild` in their `on_pile` column, that let the player name the color to match:

```json
"variables": { "winner": null, "active_color": "" },
"triggers": {
	"wild": ["active_color = choose(['Red', 'Blue', 'Green', 'Yellow'], 'Choose a color')"]
}
```

- `choose(...)` stops the steps until the player picks a color, then sets `active_color` to it and carries on.
- Play a card's `when` now matches `active_color` instead of the top card's color, and a Wild is always allowed. Its `steps` start by setting `active_color = card.color`, except for a Wild.
- The first card turned up mustn't have an effect, so `start` shuffles until it doesn't, and then sets `active_color` from that card: `{ "while": "text(top(deck), 'on_pile') != ''", "do": ["shuffle(deck)"] }`.

### Next

- Read a game that has the mechanic you need (see [Included games](#included-games)).
- Look up any key or building block in [docs/reference.md](docs/reference.md).
