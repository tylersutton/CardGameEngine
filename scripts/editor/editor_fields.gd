class_name EditorFields
## What each part of a game file can hold, for the editor's forms: the key, the kind of control, and a line of help
## (the same as README's game file reference). Kinds: text, long_text, number, whole, yes_no, choice (with options),
## spot ([x, y] on the table), list (comma-separated), column (a deck column), columns, json (anything else, as text).

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
	{key = "on_click", kind = "json", help = "Steps run when a card here (or the empty zone) is clicked, e.g. [\"draw(deck, hand)\"]."},
	{key = "on_double_click", kind = "json", help = "Steps run when a card here is double-clicked."},
	{key = "columns", kind = "json", help = "With cards: columns to add or change on every card, e.g. { \"points\": \"min(card.value, 10)\" }."},
	{key = "anchors", kind = "json", help = "With per_player or copies: one [x, y] spot each, to place them yourself."},
	{key = "rotations", kind = "json", help = "With per_player or copies: one rotation each."},
	{key = "labels", kind = "json", help = "With per_player or copies: one label each."},
	{key = "grid", kind = "json", help = "With copies: lay them out in rows, e.g. { \"columns\": 5, \"spacing\": [0.09, 0.19] }."},
	{key = "covered_by", kind = "json", help = "With copies: which copies lie on top of each one (Pyramid)."},
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
