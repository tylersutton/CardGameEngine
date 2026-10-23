# Game file reference

A look-up page: everything a game file and a deck can contain. For a guided start, see the tutorial in the [README](../README.md#tutorial-making-a-game).

**Contents:** [The game file](#the-game-file) · [zones](#zones) · [players and variables](#players-and-variables) · [actions](#actions) · [turn](#turn) · [triggers](#triggers) · [conditions](#conditions) · [constants](#constants) · [status and show_variables](#status-and-show_variables) · [card_face](#card_face) · [melds](#melds) · [bot](#bot) · [Steps](#steps) · [Building blocks](#building-blocks) · [Tips](#tips) · [Decks](#decks) · [Images](#images)

## The game file

A game file is **JSONC**: JSON that also allows `//` and `/* */` comments, and trailing commas. Put it in `games/`, and it's listed on the title screen. Only `zones` is required. A misspelled key is reported as an error when the game loads.

| Key | What it holds |
|---|---|
| `title`, `description` | Shown on the title screen. Without a title, the file name is used. |
| `how_to_play` | The rules for players, shown by the table's **How to play** button (F1) under the description: a list of lines, one paragraph each. A line starting `# ` is a heading, and one starting `- ` a bullet. Say how each thing is done on this table: "click the deck", "drag a card to the pile". |
| [`zones`](#zones) | The places cards can be: decks, hands, piles, boards. |
| [`players`](#players-and-variables), `player_names`, `seats`, `teams` | How many players there are (or may be), what they're called, where their name tags sit, and partnerships. |
| [`variables`](#players-and-variables), `player_variables`, `team_variables` | Values the game keeps track of, with their starting values. |
| [`actions`](#actions) | What players can do: drag, click, or press a button. |
| [`turn`](#turn) | The steps of one player's turn. |
| [`triggers`](#triggers) | Steps that run at the start, when a deck runs out, or when a card asks for them. |
| [`conditions`](#conditions) | Named expressions, written once and used anywhere. |
| [`constants`](#constants) | Named values that never change, such as points tables. |
| [`status`](#status-and-show_variables), `show_variables` | What the side panel shows. |
| [`card_face`](#card_face) | Which columns are drawn on a card. |
| [`melds`](#melds) | Sets and runs, for rummy games. |
| [`bot`](#bot) | Which seats the computer plays, and how well. |

Wherever a list of steps goes, a single step will also do: `"then": "end_turn()"`.

## zones

```json
"deck": { "layout": "pile", "face": "down", "anchor": [0.4, 0.5], "cards": "res://decks/my_deck.csv" }
```

| Key | Meaning | Default |
|---|---|---|
| `anchor` | Where the zone sits, as `[x, y]` fractions of the table. For a per-player zone, this is seat 1's spot. The other seats' spots are worked out round the table, with the side seats turned sideways. | `[0.5, 0.5]` |
| `layout` | How the cards spread: `pile` (only the top card shows), `row`, `fan`, `column` (cascading downward, as in solitaire), or `melds` (grouped into melds) | `row` |
| `face` | `up`; `down`; `private` (only its owner sees the cards); or `others` (everyone but its owner sees them, like holding cards facing out in Hanabi). Cards take this face as they arrive. Once the game is over, everyone sees every card that isn't face down. | `up` |
| `cards` | A deck to start with, e.g. `"res://decks/my_deck.csv"` | empty |
| `columns` | With `cards`: columns to add or change on every card as the game loads, each an expression about `card`, e.g. Hearts' `{ "value": "14 if card.rank == 'A' else card.value" }` | |
| `label` | Text shown faintly inside the zone while it's empty. `#` becomes the number in per-player zones and copies: `"Tricks #"`. | none |
| `outline` | `false` hides the outline, for zones that are only used in passing | `true` |
| `width` | The widest a `row` or `fan` may spread, as a fraction of the table | 800 px |
| `rotation` | Turns the zone, in degrees | `0` |
| `spread` | For a `pile`: how many top cards to fan out sideways, e.g. `3` for a draw-three waste | `0` |
| `reorder` | `false` stops players rearranging the zone's cards by dragging, for a hand whose order is a rule (Scout). A card dragged in from elsewhere still goes where it's dropped. | `true` |
| `show_count` | `true` shows how many cards the zone holds, under it: seeds in a Mancala pit, cards left in a deck | `false` |
| `drag` | `"stack"`: dragging a card also carries every card on top of it | one card |
| `per_player` | `true` makes one zone per player: `hand_1`, `hand_2`, ... | `false` |
| `copies` | A number: makes that many zones, e.g. `7` gives `tableau_1` to `tableau_7` | |
| `anchors`, `labels`, `rotations` | With `per_player` or `copies`: one value for each zone, to set each one yourself | |
| `grid` | With `copies`: lays them out in rows from `anchor`, e.g. `{ "columns": 5, "spacing": [0.09, -0.19] }` | |
| `group` | With `copies`: also puts them in a named group shared with other zones, e.g. Dominion's `"group": "supply"` | |
| `covered_by` | With `copies`: which copies lie on top of each one, e.g. Pyramid's `[[2, 3], [4, 5], ...]`. `covered(zone)` is then true while any of those holds a card. | |
| `board` | Makes the zone a board of spaces, centered on `anchor`: `{ "shape": "square", "columns": 8, "rows": 8, "size": 60 }`. See [Boards](#boards). | |

**Per-player zones** are seen from the current player's side. `hand` is the current player's hand, `opponent_hand` is the next player's, `hand[2]` is player 2's, and `all_hand` is every player's hand.

**Copies** are taken all together. In a step, `foundation` is the list of every foundation zone. In an action's `drag`, `"to": "foundation"` means any of them.

### Boards

A zone with a `board` is a grid of spaces, and each space is a zone of its own, numbered across each row from the top: an 8 by 8 `square` board is `square_1` to `square_64`, and `square` is all of them, as with copies. Pieces sit in spaces as cards sit in piles, so actions, steps and bots work on boards unchanged. Reversi places a disc with `"click": "square"`, or with a drag from its pile (a second action, offered with the first).

- **`shape`:** `square` (the default) or `hex`. A hex board is a rhombus, as in the game Hex: each row sits half a space right of the one above.
- **`size`:** how far apart neighbouring spaces are, in pixels. Default 60.
- **`image`:** a picture stretched over the whole board, under its spaces, e.g. `"boards/reversi.svg"` (see [Images](#images)). Add `"outline": false` to the zone to show the picture alone.
- **`paint`:** colors for spaces, each by a condition about the space: `{ "top_edge": "#c8443a80", "dark": "#5a3a22" }`. A space takes the first color whose condition holds for it, so Hex shows each player's goal edges and checkers a checkerboard (`"dark(space)": "(space.row + space.column) % 2 == 0"`).
- **A space's place:** `space.column` and `space.row`, counting from 1. `space_at(square, 4, 4)` finds a space by them.
- **Sharing a space:** pieces in the same space sit round its middle, so each one shows (Sorry!'s pawns in Start).
- **Looking along the board:** the [board building blocks](#building-blocks) take conditions with one parameter, the space, e.g. `"empty(space)": "count(space) == 0"` or Hex's `"mine(space)": "text(top(space), 'name') == my_color"`.

## players and variables

```json
"players": 4,
"player_names": ["You", "West", "North", "East"],
"seats": [[0.24, 0.86]],
"variables": { "lead": "", "tricks": 0 },
"player_variables": { "score": 0 },
"teams": [[1, 3], [2, 4]],
"team_variables": { "team_score": 0 }
```

- **Whose turn:** `player` holds whose turn it is, starting at 1. `end_turn()` passes the turn on, and `player = ...` gives it to someone in particular, e.g. the winner of a trick. `players` is the list `[1, 2, 3, 4]`, so `player = one_of(players)` in `start` picks who goes first at random.
- **How many:** `"players": [2, 3, 4]` lets the game be played by any of those numbers, chosen on the title screen or in the online lobby; the first is the usual one. Place per-player zones and name tags round the table from seat 1's spot (or give `anchors` and `seats` for the most players), list bots for every seat they might play, and write readouts without a fixed number of seats, e.g. `score[viewer()]`.
- **Name tags:** `seats` places them. Give seat 1's spot and the others go round the table, or give one `[x, y]` per player. Without `player_names`, players are "Player 1", "Player 2", ...
- **Per-player variables:** these work like per-player zones. `score` is the current player's, `opponent_score` is the next player's, and `score[2]` or `score_2` is player 2's.
- **Teams:** each list in `teams` is one team. `team_score` is the current player's team's score, `team_won` is every won zone on that team, and `team(player)` gives a player's team number.
- **Comparing players:** use `most('score')`, `players_with_most('score')` and `players_where('has_cards')`. See [Building blocks](#building-blocks).
- **Knocking out:** `knock_out(player)` takes a player out, and turns skip them until `bring_back(players)`.

## actions

Everything a player can do, each with a name. How an action is done:

- **By dragging:** `"drag": { "from": "hand", "to": "pile" }`. Each end can be a zone, a group of copies, or a list of those. Leave one end out to mean any zone. In the action, `card` is the dragged card, `from` is where it came from and `to` is where it's going. To let a click do the drag too, add `"click": true` or `"double_click": true`. The card then goes to the first zone it's allowed to go to.
- **By clicking:** `"click": "deck"`, or `"double_click"`. Clicking a card there makes it the `card`. Clicking the empty zone makes `card` be `null`. `clicked_zone` is the zone that was clicked.
- **By a button:** an action with neither is a button under the table, and the buttons appear in the order listed. An action written as just a list of steps is a button: `"Shuffle": ["shuffle(deck)"]`.

```json
"Tableau to foundation": {
	"drag": { "from": "tableau", "to": "foundation" },
	"double_click": true,
	"when": "card == top(from) and card.value == count(to) + 1",
	"steps": ["flip(face_down(top(from)))"]
}
```

| Key | Meaning |
|---|---|
| `steps` | What the action does. For a drag, these run after the card has moved. |
| `when` | The action is only allowed while this is true. A button is greyed out otherwise. |
| `choose` | For a button: what the player chooses before the steps run, e.g. `"hand"` (the cards are highlighted) or `"['Red', 'Blue']"` (buttons under the table). The steps get the pick as `choice`, and a single card as `card` too. The button can't be pressed while there's nothing (or not enough) to choose, or no pick its `allow` accepts. With only one card or zone to choose, it's chosen without asking. Clicking elsewhere cancels the choice. When it picks several cards (`pick` above 1, or `up_to`), clicking one of them while the button can be pressed starts the choice with it. |
| `pick` | With `choose`: exactly how many cards to pick, a number or an expression. `choice` is then a list. |
| `up_to` | With `choose`: any number of cards up to this many, then **Done**. Use `pick` or `up_to`, not both. |
| `allow` | With `choose`: a condition the whole pick must meet, e.g. `"size(groups(choice, 'rank')) == 1"` (all the same rank) |
| `button` | `false` hides a button, for actions that are only started with `ask(...)`. `true` gives a drag or click action a button too, which asks for the card. |
| `human_only` | `true`: bots never do it |
| `any_time` | `true`: anyone may do it, even on someone else's turn or while a choice is being made, and it's done as them, so `hand` is their own hand: a Sort button. Its steps can't wait for a choice, and bots never do it. |
| `prefer` | What a bot aims for when making this action's choice, instead of the game's `bot.prefer` |

**When an action can be done:**

- An action named in an [`offer(...)`](#turn) can only be done while that offer waits.
- Any other action can be done whenever its `when` holds, on the current player's turn. With `any_time`, by anyone at any time.
- For a drag or click, the first action in the file that allows it is the one that's done.
- A game with no drag actions that never offers an action allows every drag, as a free-play sandbox. Any other game refuses a drag no action allows.

## turn

The steps of one player's turn. They run for whoever's turn it is, then start again for the next player, until `end_game(...)`.

```json
"turn": [
	"offer(['Draw two', 'Take a discard'])",
	"ending = offer_until('Play duo', ['End turn', 'Stop', 'Last chance'])",
	{ "if": "extra_turn and ending == 'End turn'", "then": ["extra_turn = false"], "else": ["end_turn()"] }
]
```

| Building block | Does |
|---|---|
| `offer(['Action', ...])` | Waits until the current player does one of these actions, and gives its name. If none of them can be done right now, it gives `null` straight away. |
| `offer_until(repeated, ending)` | Offers both kinds of action again and again until the player does an ending one, then gives its name, e.g. play any number of duos, then End turn |
| `end_game(winners)` | Ends the game and announces the winners: a player, a list for a tie, or nothing. The rest of the turn is skipped. |

- The first turn starts after `start`. After that, a turn starts again whenever nothing is running or waiting for a choice.
- If a whole round of turns passes in which nobody could do anything, it's a stalemate and the game ends.
- Without a `turn`, players do whatever their actions allow, which suits solitaire.

## triggers

Named lists of steps:

| Name | Runs when |
|---|---|
| `start` | The game begins, before the first turn |
| `turn_start` | After every `end_turn()` |
| `<zone>_empty` | `draw` needs a card from that zone and it's empty, e.g. `deck_empty`. For a per-player zone, it runs as the zone's owner. |
| any other name | A step calls it with `trigger('name')`, or a card names it in its `on_<zone>` column ([card effects](#decks)). In a card effect, `card` is the card that moved. |

## conditions

Named expressions, so a check used in several places is written once. A condition's name works in any expression, and behaves as if its text were written there in brackets.

```json
"conditions": {
	"top_card": "card == top(from)",
	"fits_tableau": "(count(to) == 0 and card.value == 13) or (card.value == number(top(to), 'value') - 1 and card.color != text(top(to), 'color'))"
}
```

A condition can take **parameters** and be used like a building block. Sea Salt + Paper scores this way:

```json
"kinds(cards, kind)": "count(where(cards, 'Kind', kind))",
"duo_points(cards)": "kinds(cards, 'Crab') / 2 + kinds(cards, 'Boat') / 2 + kinds(cards, 'Fish') / 2 + min(kinds(cards, 'Shark'), kinds(cards, 'Swimmer'))",
"points(cards)": "duo_points(cards) + collection_points(cards) + multiplier_points(cards) + mermaid_points(cards)",
"my_points": "points([hand, played])"
```

Inside a condition, the parameter names stand for whatever was passed in. Dividing whole numbers gives a whole number, so `kinds(cards, 'Crab') / 2` counts pairs of crabs.

A refused drag is logged with the names of the conditions it failed, which keeps the message short.

## constants

Named values that steps can read but never change:

```json
"constants": { "shell_points": [0, 0, 2, 4, 6, 8, 10] }
```

`lookup(shell_points, count(shells))` gives the points for that many shells.

## status and show_variables

`status` lists readouts for the side panel, recalculated after every change:

```json
"status": { "scores": "'%s %d, %s %d' % [name_of(1), score_1, name_of(2), score_2]" }
```

Name players with `name_of(...)` and use `viewer()` for "you", so a readout is right on every player's screen when playing online.

`"show_variables": ["actions", "buys", "coins"]` lists those variables in the side panel too. With **Designer info** on, every variable is listed.

## card_face

| Key | Meaning |
|---|---|
| `corner` | Column shown small in the top-left |
| `title` | Column shown large in the middle |
| `body` | List of columns shown below the title |
| `text` | Column of rules text, word-wrapped under the title |
| `zoom` | e.g. `2.4`: hovering over a face-up card shows it enlarged |
| `color` | Text color: a column holding each card's color (`Red`, `#c0392b`), or one color for every card |
| `background` | The card's color, given the same way. White if not set. |
| `image` | A picture for the card: a column of [image](#images) file names, or one file for every card. |
| `layout` | `full` (the default): the image covers the whole card, for cards whose picture has everything on it. `art`: the image sits in a box above the title and text. |
| `card_back` | What face-down cards show: an image or a color, for every card or from a column. A plain blue back if not set. |
| `shape` | `disc` or `square` draws cards as pieces, `size` pixels across (a column or one number; 50 if not set), in their `background` color with their `title` on top. A column or one shape for every card. |
| `back` | A piece's other side, as a color or an image: a two-sided piece like Reversi's discs. `flip` turns it over, and both sides are public. |

Without a `card_face`, a card shows its `name`.

## melds

For rummy games: which groups of cards count as melds.

```json
"melds": {
	"types": [{ "same": "rank", "min": 3 }, { "same": "suit", "sequence": "value", "min": 3 }],
	"points": "points"
}
```

A meld is at least `min` cards sharing a value in the `same` column. With `sequence`, that column's numbers must also run in order. `points` is the column counted as deadwood. Melds turn on the `melds`, `deadwood` and `layoff` building blocks.

## bot

```json
"bot": { "players": [2], "prefer": "score - opponent_score - sum(deadwood(hand), 'points')" }
```

- **Without `prefer`,** the bot plays a random legal move, which is enough to play a prototype through.
- **With `prefer`,** the bot tries every legal input, scores the result of each from its own point of view, and takes the best. Write `prefer` as "how good is this position for me?"
- **It looks one input ahead.** Among equally good inputs, it picks one at random.
- **It doesn't peek.** Cards it can't see are shuffled among themselves before each try. `"samples": 4` sets how many shuffles it averages over, and `"peek": true` lets it see everything.
- **It never makes inputs that only reorder cards,** such as sorting.

## Steps

A step is one line of text that does something or works something out, like `draw(deck, hand, 2)` or `score += 10`. Every step is checked when the game loads (spelling, unknown names, the number of arguments), so most mistakes show up straight away.

| You can write | For example |
|---|---|
| Zones, variables, conditions, constants | `count(hand) > 7` |
| The card involved: dragged, clicked, chosen, or whose effect is running | `card` (`null` if there isn't one) |
| A card's columns, with any capitals | `card.suit == lead`, `card.value > 5` |
| A board space's place | `space.column`, `space.row` |
| Which copy, space or player's zone a zone is | `clicked_zone.number` (from 1), `clicked_zone.owner` (the player whose per-player zone it is, `0` for a shared one) |
| Where a drag goes, and what was clicked or chosen | `from`, `to`, `clicked_zone`, `choice` |
| Text in single quotes | `'Hearts'`. A quote inside text takes a backslash, `'King\'s Court'`, which is doubled in the game file's JSON: `'King\\'s Court'`. |
| Lists, read by position from 0 | `[1, 2]`, `ranking[0]`, `finish_order + [player]` |
| Comparing and arithmetic | `==`, `!=`, `<`, `>`, `<=`, `>=`, `+`, `-`, `*`, `/`, `%` |
| Logic | `and`, `or`, `not` |
| Looking in a list, a zone or text | `card.rank in ['J', 'Q', 'K']`, `player not in players_left()` |
| Either-or values | `'out' if count(hand) == 0 else 'in'` |
| Text with numbers in it | `'bid %d' % [bid]` |
| Math | `abs`, `min`, `max`, `floor`, `ceil`, `round`, `int` (`int(true)` is `1`) |
| Changing a value, as a step of its own | `phase = 'buy'`, `score += 10`, `passes -= 1` |

- **Columns:** a column with any text in it gives text for every card, so `card.rank == '10'` works. A column of numbers gives numbers.
- **Remembered names:** a name set with `=` that isn't one of the game's variables is remembered until the action or trigger that set it finishes, e.g. `pile = choose(discard_piles)`.
- **Changing values:** values only change in steps. In a `when` or a condition, `=` is an error, since `==` was probably meant.
- **When a step fails:** the rest of its list is skipped, and the log says why.

### if, for and while

Wherever a list of steps goes, you can also use these:

```json
{ "if": "count(hand) > 7", "then": ["discard_needed = true"], "else": ["discard_needed = false"] }
{ "for": "column", "in": "tableau", "do": ["draw(stock, column, column_number)"] }
{ "while": "sum(dealer, 'value') < 17", "do": ["draw(deck, dealer)"] }
```

- **`else`** is optional, and steps nest freely.
- **`for`** goes through a zone's cards, a group of zones, any list, or a number of times (`"in": "3"`). `<name>_number` is the position, counting from 1. It goes through what was there when it started, so moving those cards inside the loop is safe.
- **`"for": "player", "in": "players"`** runs the steps once for each player, as if it were their turn: `hand` and `score` follow along. The turn goes back to whoever had it afterwards.
- **`while`** checks before each repeat, and stops with an error after 1,000 repeats.

### Choosing in the middle of steps

`choose(...)` stops the steps until the current player picks something, and then gives what they picked:

```json
"crab": [
	"pile = choose(discard_piles, 'Look through which pile?')",
	"move(choose(pile, 'Take a card'), hand)"
]
```

- **What can be chosen:** cards (`hand`); zones (`discard_piles`), which the player clicks; or values (`['Red', 'Blue']`, `numbers(1, 6)`), which appear as buttons. The prompt shows above the table.
- **Who chooses:** the current player. Inside a loop over players, each of them chooses in turn.
- **While waiting,** nothing else can be done but the choice. Undo goes back to before the choice.
- **When there's nothing to choose,** `choose` gives `null` and the others give `[]`. With only one card or zone, `choose` gives it without asking, and with no more cards than `choose_exactly` asks for, it gives them all. A choice of values always asks, so `choose(['Done'])` is a pause.
- **In a trigger,** a choice holds up the steps that called the trigger.
- **Where it works:** only in steps, not in a `when`, a condition or `status`.
- **`choose` or an action's `choose`?** Choosing through an action's `choose` key can be cancelled, can check the whole pick with `allow`, and can have its own bot `prefer`.

## Building blocks

Wherever a building block takes `cards`, you can give a zone, a card, or a list of either.

**Moving cards and changing things**

| Building block | Does |
|---|---|
| `draw(from, to, amount)` | Moves the top card(s) of `from` to `to` (1 card unless you say). Fires `<from>_empty` if `from` runs out. |
| `deal(from, to, amount)` | Deals `amount` cards to each zone in `to`, one at a time, e.g. `deal(deck, all_hand, 13)` |
| `move(cards, to)` | Moves cards to a zone |
| `swap(card, other)` | The two cards trade places, zone and spot. Sorry!'s 11; and Scout turns a card round by swapping it for its other way up, kept in a box. |
| `try_move(card, to)` | Moves a card only if a drag action allows it, and does that action. `to` can be a group. |
| `shuffle(zone)` | Shuffles a zone |
| `flip(cards)` | Turns cards over, until they move to another zone |
| `reveal(cards)` | Shows cards to everyone, even in a private zone, until they move: hands at the end of a round |
| `sort(zone, ['column', ...])` | Keeps a zone sorted by these columns, until a player drags a card to a new place in it. `sort(zone, [])` stops sorting. |
| `mark(cards, 'key', value)`, `unmark(cards, 'key')` | Puts a note on cards, or takes it off, e.g. a clue `mark(cards, 'color', 'Red')`. Marks show on the card and last until it moves. A mark named after a column tells bots what the card is. |
| `end_turn()` | Passes the turn to the next player and fires `turn_start` |
| `trigger('name')` | Runs a trigger right after this step, with the same `card` |
| `run_effect(card, 'play')` | Runs a card's `on_play` effect again, e.g. Throne Room |
| `reset(['variable', ...])` | Puts variables back to their starting values, for every player if they're player variables |
| `knock_out(players)`, `bring_back(players)` | Takes players out of the game, or puts them back |
| `end_game(winners)` | Ends the game (see [turn](#turn)) |
| `log('text')` | Writes a line in the action log for everyone, e.g. `log('%s has %d points' % [name_of(player), score])` |

**Waiting for players**

| Building block | Does |
|---|---|
| `choose(options, 'prompt')` | Waits for one card, zone or value, and gives it |
| `choose_exactly(cards, amount, 'prompt')` | Waits for exactly that many cards, and gives them as a list |
| `choose_up_to(cards, most, 'prompt')` | Waits for up to `most` cards, until the player presses **Done** |
| `offer(...)`, `offer_until(...)` | Wait for the player to do an action (see [turn](#turn)) |
| `ask('Action', player)` | Starts that action's choice once the current steps finish, made by `player` if given (Militia's discard). It must be made, and several asks wait in line. |
| `ask_all('Action', 'trigger')` | Everyone makes that choice at once, e.g. secret bids, then the trigger runs |
| `ask_others('Action', 'trigger')` | Every other player, in turn order, gets a chance to respond, then the trigger runs. A response that calls `ask_others` again starts a new round. This is Kaboom's Nope. |

**Cards and zones**

| Building block | Gives |
|---|---|
| `top(cards)`, `all(zone)` | The top card (`null` if there's none), or all of a zone's cards |
| `position(card)` | Where a card lies in its zone, from 1 at the bottom (the left of a row). `all(hand)[position(card) - 2]` is the card to its left. |
| `count(cards)` | How many cards there are |
| `where(cards, 'column', value)` | The cards with that value in the column |
| `matching(cards, 'condition')` | The cards for which a condition holds, with each one as `card` |
| `without(cards, removed)` | The cards, minus the removed ones |
| `highest(cards, 'column')`, `lowest(cards, 'column')` | The card with the biggest or smallest number in the column |
| `number(card, 'column')`, `text(card, 'column')` | A column as a number or as text, giving `0` or `''` when `card` is `null` |
| `sum(cards, 'column')` | The total of a column, or `sum(list)` for a list of numbers |
| `last(cards, amount)`, `first(list, amount)` | The last cards (the top of a pile) or the first ones, or the last or first items of a list |
| `tops(zones)` | The top card of each zone, e.g. `tops(supply)` |
| `face_down(cards)` | The cards that are face down |
| `movable(cards, to)` | The cards a drag action allows into `to` right now. `count(movable(hand, pile)) == 0` means "can't play". |
| `covered(zone)` | Whether a card lies on the zone (see `covered_by`). It also takes a card, meaning the card's zone. |
| `mark_of(card, 'key')` | A card's mark, or `null` |
| `one_of(items)` | A random card or item, or `null` if there are none |
| `zone('kind', number)` | That copy or player's zone, e.g. `zone('basic_pile', 7)` |
| `follows_suit(card, hand, lead)` | Whether playing the card follows suit (uses the `suit` column) |
| `trick_winner(cards, lead, trump)` | The card that takes the trick (uses `suit` and `value`; `trump` is optional) |

**Boards**

See [Boards](#boards). A `'condition'` here is a condition's name in quotes, with one parameter: the space.

| Building block | Gives |
|---|---|
| `space_at(board, column, row)` | The space at that column and row, counting from 1, or `null` off the board |
| `neighbors(space, directions)` | The spaces next to it. On a square board, `directions` can be `'orthogonal'` or `'diagonal'`; left out, it's all eight. |
| `zones_where(zones, 'condition')` | The zones or spaces for which the condition holds, e.g. `zones_where(square, 'empty')` |
| `flanked(space, 'between', 'end')` | The spaces caught in straight lines from the space: in each direction, a run where `between` holds, closed by a space where `end` holds. Reversi's captures. |
| `connected(from, to, 'condition', directions)` | Whether a path of neighbouring spaces, each where the condition holds, joins a space of `from` to a space of `to`. Hex's win. |
| `in_a_row(space, 'condition')` | The most spaces in one straight line through the space where the condition holds, e.g. `in_a_row(clicked_zone, 'mine') >= 4` |

**Players**

| Building block | Gives |
|---|---|
| `owner(card)` | The player whose zone the card is in (`0` for a shared zone) |
| `next_player(seats)` | The player that many seats on: `1` is the next player, `-1` the previous one |
| `name_of(player)` | A player's name. Use it rather than writing names into text. |
| `viewer()` | The player looking at this screen. It only works in `status`. |
| `most('name')`, `least('name')` | The highest or lowest value of a player variable or condition, across the players |
| `players_with_most('name')`, `players_with_least('name')` | The players who have it (several when tied), e.g. `end_game(players_with_most('score'))` |
| `players_where('name')` | The players for whom a player variable or condition is true |
| `players_left()` | The players who aren't knocked out |
| `team(player)` | A player's team number |

**Numbers and lists**

| Building block | Gives |
|---|---|
| `numbers(low, high)` | The whole numbers from `low` to `high` |
| `random(low, high)` | A random whole number, e.g. `random(1, 6)` |
| `size(list)` | How many items are in a list. A group of cards counts as one item. |
| `lookup(list, position)` | The item at that position, or the first or last item when the position is past the end: a points table |
| `group_sizes(cards, 'column')` | How many cards share each value, biggest first, e.g. `[3, 2, 1]` |

**Combinations:** these find groups of cards. `size(...)` counts the groups, and `count(...)` counts the cards in them.

| Building block | Gives |
|---|---|
| `groups(cards, 'column')` | The cards grouped by value, biggest group first. `size(groups(hand, 'suit')) == 1` is a flush. |
| `pairs(cards, 'column')` | Every two cards that share a value. Three of a kind makes three pairs. |
| `runs(cards, 'column', length)` | Every longest run of consecutive numbers that's at least `length` long. A repeated value makes one run per copy, as in cribbage. |
| `sums(cards, 'column', total)` | Every combination of cards whose column adds up to `total`. Use it on small hands only. |
| `melds(cards)`, `deadwood(cards)`, `layoff(cards, melds)` | The best melds, the cards left out of them, and which cards to lay off on them (these need [`melds`](#melds)) |

## Tips

- **An empty zone's top card is `null`,** and `card.value` fails on it. Use `number(top(pile), 'value')` and `text(top(pile), 'suit')` instead. Alternatively, `and` and `or` stop as soon as the answer is known, so `count(to) > 0 and top(to).color == 'Red'` is safe.
- **To reuse steps,** put them in a trigger and call `trigger('deal')`. **To reuse a check,** name it in `conditions`.
- **"Draw only if you can't play"** is a drag action with `"when": "count(movable(hand, pile)) == 0"`.
- **Whole numbers stay whole,** so `dealer % 4` and `list[index]` work.

## Decks

A deck is a CSV file in `decks/`. The first row names the columns, and each row after that is one card. `decks/standard_deck.csv` is an ordinary 52-card deck with `name` (`A♠`), `rank`, `suit`, `color` and `value` (1 to 13). Most games can use it and add their own numbers with a zone's [`columns`](#zones).

- **`name`** is the card's title in the log.
- **`count`** is how many copies to make. Leave it blank for 1.
- **`on_<zone>`** is a card effect: the trigger to run when the card enters that zone, e.g. `draw_two`. Use the per-player zone's plain name: `on_hand`, not `on_hand_1`. A cell can also hold steps, separated by `;`.
- **Saving:** save the file as UTF-8 so that symbols like ♠ survive. A cell containing a comma needs quotes around it, which spreadsheet programs add for you.
- **Godot:** the empty `decks/.gdignore` stops Godot from reading the CSVs as translations. Because of it, decks don't appear in Godot's FileSystem panel. To export a standalone build, add `decks/*`, `games/*.jsonc` and `images/*` to the export filter.

## Images

Images are files in `images/`: PNG, JPG, WebP or SVG. Game files and decks name them by their path there: `moat.png`, `cards/A_Spades.png`. A full `res://` path works too.

- **On cards:** `card_face`'s `image` names a column of file names, e.g. a deck with an `art` column. A zone's `columns` can make that column from the others, as Solitaire does: `"art": "'cards/' + card.rank + '_' + card.suit + '.png'"`. `card_back` gives the back, and `layout` puts the picture over the whole card or above its text.
- **On pieces and boards:** a disc or square piece shows its `image` instead of its color, and a board's `image` is drawn under its spaces.
- **Missing images** are load errors, like missing decks.
- **Online,** the host sends the game's images with it, so the other players don't need them.
- **In the editor,** the deck table's **Image** button puts an image in the selected cell; one from outside `images/` is copied in first.
- **Godot:** the empty `images/.gdignore` keeps Godot from importing the images: the game reads the files itself, so unsaved and online games show them too. The placeholders in `images/cards/` and `images/card_back.svg` are free to use.
