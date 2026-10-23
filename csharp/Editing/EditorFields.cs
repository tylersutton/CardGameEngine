namespace Editing;

/// One setting in a game file, for the editor's forms: its key, the kind of control, a line of help (the same as
/// docs/reference.md), and for some kinds the options or an example. Fields with a group are folded away under that
/// heading.
public record Field(string Key, string Kind, string Help, string Group = "", string[]? Options = null, string Example = "");

/// What each part of a game file can hold. Kinds: text, long_text, lines (a list of text, one item per line), number, whole, yes_no, choice (with options), spot
/// ([x, y] on the table), list (comma-separated; one item stays text), names and numbers (comma-separated lists),
/// column (a deck column), columns, deck, expression and steps (checked as typed), drag (its from and to zones),
/// click (zones clicked), board (shape, columns, rows, size and paint), json (anything else, as text, with an example).
public static class EditorFields
{
    public static readonly Field[] Game =
    [
        new("title", "text", "The name on the title screen."),
        new("description", "long_text", "A line or two about the game, shown on the title screen."),
        new("how_to_play", "lines", "The rules for players, shown by the table's How to play button: one paragraph per line, \"# \" starts a heading and \"- \" a bullet."),
    ];

    public static readonly Field[] Zone =
    [
        new("label", "text", "Shown faintly inside the empty zone. In per-player zones and copies, # becomes the number."),
        new("layout", "choice", "How the cards spread out. Default: row.", Options: ["pile", "row", "fan", "column", "melds"]),
        new("face", "choice", "Which way cards face: private = only their owner sees them, others = everyone but their owner. Default: up.", Options: ["up", "down", "private", "others"]),
        new("cards", "deck", "A deck that starts here."),
        new("per_player", "yes_no", "One of these zones for each player (hand_1, hand_2, ...)."),
        new("copies", "whole", "Make this many of the zone (tableau_1, tableau_2, ...)."),
        new("anchor", "spot", "Where it sits, from 0 to 1 across and down. For per-player zones, seat 1's spot; the rest go round the table. Drag zones on the table to move them."),
        new("width", "number", "Widest a row or fan may spread, as a share of the table (e.g. 0.45).", "Layout"),
        new("rotation", "number", "Turn the zone, in degrees (90 for the left side of the table).", "Layout"),
        new("outline", "yes_no", "Draw the zone's outline. Default: yes.", "Layout"),
        new("spread", "whole", "For a pile: fan out its top few cards (3 for a draw-three waste).", "Layout"),
        new("show_count", "yes_no", "Show how many cards it holds, under it (seeds in a pit, cards in a deck).", "Layout"),
        new("drag", "choice", "stack: dragging a card also carries every card on top of it.", "Advanced", ["stack"]),
        new("group", "text", "With copies: also put them in a group with this name, shared with other zones.", "Advanced"),
        new("columns", "json", "With cards: columns to add or change on every card, e.g. { \"points\": \"min(card.value, 10)\" }.", "Advanced", Example: "{ \"points\": \"min(card.value, 10)\" }"),
        new("anchors", "json", "With per_player or copies: one [x, y] spot each, to place them yourself.", "Advanced", Example: "[[0.5, 0.85], [0.5, 0.15]]"),
        new("rotations", "json", "With per_player or copies: one rotation each.", "Advanced", Example: "[0, 180]"),
        new("labels", "json", "With per_player or copies: one label each.", "Advanced", Example: "[\"You\", \"Them\"]"),
        new("grid", "json", "With copies: lay them out in rows, e.g. { \"columns\": 5, \"spacing\": [0.09, 0.19] }.", "Advanced", Example: "{ \"columns\": 5, \"spacing\": [0.09, 0.19] }"),
        new("covered_by", "json", "With copies: which copies lie on top of each one (Pyramid).", "Advanced", Example: "[[1, 2], [2, 3]]"),
        new("board", "board", "Make it a board: one space per column and row (square_1, square_2, ...), square or hex, its spaces size pixels apart. paint colors spaces by conditions about the space, e.g. { \"top_edge\": \"#c8443a\" }.", "Board"),
    ];

    public static readonly Field[] Action =
    [
        new("steps", "steps", "What the action does. card is the card dragged, clicked or chosen; from and to are a drag's zones, clicked_zone a click's; choice is what was chosen."),
        new("drag", "drag", "Done by dragging a card from one zone (or group of copies) to another: from hand, to pile. Several, comma-separated; leave one empty for any zone. Tick \"also by clicking\" to let a click on the card do the drag. An action is done by a drag or by clicking a zone, not both."),
        new("click", "click", "Done by clicking a card in this zone (or the empty zone), e.g. deck. Only for actions without a drag: a drag action is clicked with \"also by clicking\", under drag."),
        new("double_click", "click", "The same for double-clicking. On a drag action, use \"also by double-clicking\" under drag, e.g. solitaire's moves to the foundations.", "Advanced"),
        new("when", "expression", "Only allowed while this is true (the button is greyed out). card, from and to are the drag's."),
        new("choose", "expression", "For a button: something to choose first. Cards (e.g. hand) are clicked; values (e.g. ['Red', 'Blue'], numbers(0, 13)) get buttons.", "Choosing"),
        new("pick", "expression", "How many cards to choose (a number, or e.g. count(hand) - 3).", "Choosing"),
        new("up_to", "expression", "Choose from none up to this many, then press Done.", "Choosing"),
        new("allow", "expression", "A condition the whole choice must meet, e.g. size(groups(choice, 'rank')) == 1.", "Choosing"),
        new("button", "yes_no", "Show it as a button under the table. Default: yes, unless it's done by a drag or click (then yes asks for a card to do it with). No: it only happens through ask('name').", "Advanced"),
        new("human_only", "yes_no", "Bots never do it (e.g. Next hand).", "Advanced"),
        new("any_time", "yes_no", "Anyone may do it, even on someone else's turn or while a choice is being made, and it's done as them: hand is their hand (e.g. Sort hand). Its steps can't wait for a choice.", "Advanced"),
        new("prefer", "expression", "For a bot making this choice: what to aim for instead of the bot's usual prefer.", "Advanced"),
    ];

    public static readonly Field[] CardFace =
    [
        new("corner", "column", "Column shown small in the top-left."),
        new("title", "column", "Column shown large in the middle."),
        new("body", "columns", "Columns shown below the title (comma-separated)."),
        new("text", "column", "Column of rules text, drawn small under the title."),
        new("color", "text", "Text color: a column holding each card's color, or one color for every card (#333333)."),
        new("background", "text", "Face color: a column, or one color for every card (#f3e9d2). White if not given."),
        new("image", "text", "A picture: a column of image files in images/ (moat.png), or one for every card.", "Images"),
        new("layout", "choice", "full: the image covers the card (the default). art: the image sits above the title and text.", "Images", ["full", "art"]),
        new("card_back", "text", "What face-down cards show: an image in images/ or a color, for every card or from a column.", "Images"),
        new("zoom", "number", "e.g. 2.4: hovering a card shows an enlarged copy, so small text can be read."),
        new("shape", "text", "disc or square draws cards as pieces in their background color: a column, or one shape for every card.", "Pieces"),
        new("size", "text", "A piece's size in pixels: a column, or one number for every card. Default: 50.", "Pieces"),
        new("back", "text", "A two-sided piece's other side (Reversi's discs): a column or one color. flip turns it over; both sides are public.", "Pieces"),
    ];

    public static readonly Field[] Players =
    [
        new("players", "counts", "How many players, or the numbers it can be played with (comma-separated, e.g. 2, 3, 4; the first is the usual one). Turns pass with end_turn(); player is whose turn it is."),
        new("player_names", "names", "Names on the seat tags, in seat order (comma-separated). Otherwise Player 1, Player 2, ..."),
        new("seats", "json", "Where the name tags go: [[x, y]] for seat 1 (the rest go round the table), or one [x, y] per player.", "Advanced", Example: "[[0.22, 0.86]]"),
        new("teams", "json", "Partnerships, e.g. [[1, 3], [2, 4]]: every player on exactly one team.", "Advanced", Example: "[[1, 3], [2, 4]]"),
    ];

    public static readonly Field[] Bot =
    [
        new("players", "numbers", "Seats the computer plays (comma-separated, e.g. 2, 3, 4). Without prefer, bots play random legal moves."),
        new("prefer", "expression", "How good a position is for the bot, e.g. score - opponent_score. It tries every legal input and takes the best."),
        new("samples", "whole", "How many ways to shuffle the cards it can't see when judging a move. More is fairer but slower.", "Advanced"),
        new("peek", "yes_no", "Let the bot see every card: stronger and faster.", "Advanced"),
    ];

    public static readonly Field[] Scoring =
    [
        new("show_variables", "names", "Variables listed in the side panel, in this order (comma-separated). Empty: all of them."),
    ];

    public static readonly Field[] Melds =
    [
        new("points", "column", "The column counted as deadwood (cards left out of melds)."),
    ];

    public static readonly Field[] MeldType =
    [
        new("same", "column", "Cards in the meld share this column's value (rank for sets, suit for runs)."),
        new("sequence", "column", "For runs: this column's numbers must also follow on one from another."),
        new("min", "whole", "The fewest cards in the meld."),
    ];
}
