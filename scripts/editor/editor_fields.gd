class_name EditorFields
## What each part of a game file can hold, for the editor's forms: the key, the kind of control, and a line of help
## (the same as README's game file reference). Kinds: text, long_text, number, whole, yes_no, choice (with options),
## spot ([x, y] on the table), list (comma-separated; one item stays text), names and numbers (comma-separated lists),
## column (a deck column), columns, expression and steps (checked as typed), zones, json (anything else, as text,
## with an example).

const GAME := [
	{key = "title", kind = "text", help = "The name on the title screen."},
	{key = "description", kind = "long_text", help = "A line or two about the game, shown on the title screen."},
]

const ZONE := [
	{key = "label", kind = "text", help = "Shown faintly inside the empty zone. In per-player zones and copies, # becomes the number."},
	{key = "layout", kind = "choice", options = ["pile", "row", "fan", "column", "melds"], help = "How the cards spread out. Default: row."},
	{key = "face", kind = "choice", options = ["up", "down", "private", "others"], help = "Which way cards face: private = only their owner sees them, others = everyone but their owner. Default: up."},
	{key = "cards", kind = "deck", help = "A deck that starts here."},
	{key = "per_player", kind = "yes_no", help = "One of these zones for each player (hand_1, hand_2, ...)."},
	{key = "copies", kind = "whole", help = "Make this many of the zone (tableau_1, tableau_2, ...)."},
	{key = "anchor", kind = "spot", help = "Where it sits, from 0 to 1 across and down. For per-player zones, seat 1's spot; the rest go round the table. Drag zones on the table to move them."},
	{key = "width", kind = "number", help = "Widest a row or fan may spread, as a share of the table (e.g. 0.45)."},
	{key = "rotation", kind = "number", help = "Turn the zone, in degrees (90 for the left side of the table)."},
	{key = "outline", kind = "yes_no", help = "Draw the zone's outline. Default: yes."},
	{key = "spread", kind = "whole", help = "For a pile: fan out its top few cards (3 for a draw-three waste)."},
	{key = "drag", kind = "choice", options = ["stack"], help = "stack: dragging a card also carries every card on top of it."},
	{key = "group", kind = "text", help = "With copies: also put them in a group with this name, shared with other zones."},
	{key = "on_click", kind = "steps", help = "Steps run when a card here (or the empty zone) is clicked; card is the clicked card, clicked_zone the zone (one copy, not the group)."},
	{key = "on_double_click", kind = "steps", help = "Steps run when a card here is double-clicked."},
	{key = "columns", kind = "json", example = "{ \"points\": \"min(card.value, 10)\" }", help = "With cards: columns to add or change on every card, e.g. { \"points\": \"min(card.value, 10)\" }."},
	{key = "anchors", kind = "json", example = "[[0.5, 0.85], [0.5, 0.15]]", help = "With per_player or copies: one [x, y] spot each, to place them yourself."},
	{key = "rotations", kind = "json", example = "[0, 180]", help = "With per_player or copies: one rotation each."},
	{key = "labels", kind = "json", example = "[\"You\", \"Them\"]", help = "With per_player or copies: one label each."},
	{key = "grid", kind = "json", example = "{ \"columns\": 5, \"spacing\": [0.09, 0.19] }", help = "With copies: lay them out in rows, e.g. { \"columns\": 5, \"spacing\": [0.09, 0.19] }."},
	{key = "covered_by", kind = "json", example = "[[1, 2], [2, 3]]", help = "With copies: which copies lie on top of each one (Pyramid)."},
]

const MOVE := [
	{key = "from", kind = "zones", help = "Where the card is dragged from: a zone (hand means the current player's), a group, or several (comma-separated). Empty: anywhere."},
	{key = "to", kind = "zones", help = "Where it's dragged to, the same way."},
	{key = "phase", kind = "list", help = "Only allowed in this phase (or any of several, comma-separated): the phase variable must match."},
	{key = "when", kind = "expression", help = "Only allowed while this is true. card is the dragged card, from and to its zones."},
	{key = "then", kind = "steps", help = "Steps run after the move."},
]

const ACTION := [
	{key = "steps", kind = "steps", help = "What the button does (after the choice, if it has one: choice is what was picked)."},
	{key = "when", kind = "expression", help = "The button is greyed out while this is false."},
	{key = "phase", kind = "list", help = "Only works in this phase (or any of several, comma-separated)."},
	{key = "choose", kind = "expression", help = "Something to choose first: cards (e.g. hand) are clicked; values (e.g. ['Red', 'Blue'], numbers(0, 13)) get buttons."},
	{key = "pick", kind = "expression", help = "How many cards to choose (a number, or e.g. count(hand) - 3)."},
	{key = "up_to", kind = "expression", help = "Choose from none up to this many, then press Done."},
	{key = "allow", kind = "expression", help = "A condition the whole choice must meet, e.g. size(groups(choice, 'rank')) == 1."},
	{key = "button", kind = "yes_no", help = "Show it in the toolbar. No: it only happens through ask('name'). Default: yes."},
	{key = "human_only", kind = "yes_no", help = "Bots never press it (e.g. Next hand)."},
	{key = "prefer", kind = "expression", help = "For a bot making this choice: what to aim for instead of the bot's usual prefer."},
]

const CARD_FACE := [
	{key = "corner", kind = "column", help = "Column shown small in the top-left."},
	{key = "title", kind = "column", help = "Column shown large in the middle."},
	{key = "body", kind = "columns", help = "Columns shown below the title (comma-separated)."},
	{key = "text", kind = "column", help = "Column of rules text, drawn small under the title."},
	{key = "color", kind = "text", help = "Text color: a column holding each card's color, or one color for every card (#333333)."},
	{key = "background", kind = "text", help = "Face color: a column, or one color for every card (#f3e9d2). White if not given."},
	{key = "zoom", kind = "number", help = "e.g. 2.4: hovering a card shows an enlarged copy, so small text can be read."},
]

const PLAYERS := [
	{key = "players", kind = "whole", help = "How many players. Turns pass with end_turn(); player is whose turn it is."},
	{key = "player_names", kind = "names", help = "Names on the seat tags, in seat order (comma-separated). Otherwise Player 1, Player 2, ..."},
	{key = "seats", kind = "json", example = "[[0.22, 0.86]]", help = "Where the name tags go: [[x, y]] for seat 1 (the rest go round the table), or one [x, y] per player."},
	{key = "teams", kind = "json", example = "[[1, 3], [2, 4]]", help = "Partnerships, e.g. [[1, 3], [2, 4]]: every player on exactly one team."},
]

const BOT := [
	{key = "players", kind = "numbers", help = "Seats the computer plays (comma-separated, e.g. 2, 3, 4). Without prefer, bots play random legal moves."},
	{key = "prefer", kind = "expression", help = "How good a position is for the bot, e.g. score - opponent_score. It tries every legal input and takes the best."},
	{key = "samples", kind = "whole", help = "How many ways to shuffle the cards it can't see when judging a move. More is fairer but slower."},
	{key = "peek", kind = "yes_no", help = "Let the bot see every card: stronger and faster."},
]

const SCORING := [
	{key = "show_variables", kind = "names", help = "Variables listed in the side panel, in this order (comma-separated). Empty: all of them."},
]

const MELDS := [
	{key = "points", kind = "column", help = "The column counted as deadwood (cards left out of melds)."},
]

const MELD_TYPE := [
	{key = "same", kind = "column", help = "Cards in the meld share this column's value (rank for sets, suit for runs)."},
	{key = "sequence", kind = "column", help = "For runs: this column's numbers must also follow on one from another."},
	{key = "min", kind = "whole", help = "The fewest cards in the meld."},
]
