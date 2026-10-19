extends SceneTree
## Checks turns and actions: the "turn" steps, offer and offer_until, actions that are only possible while offered,
## actions done by a drag, a click or a button, end_game, undo and replays, and load errors for old game files.
## Run after changing turns or actions: godot --headless -s tests/turn_test.gd

const RULES := """{
	"players": 2,
	"zones": {
		"deck": { "layout": "pile", "face": "down", "cards": "res://decks/standard_deck.csv" },
		"discard": { "layout": "pile" },
		"table": { "per_player": true },
		"hand": { "per_player": true }
	},
	"variables": { "turns": 0, "taps": 0 },
	"actions": {
		"Draw": { "drag": { "from": "deck", "to": "hand" }, "click": true },
		"Discard": { "drag": { "from": "hand", "to": "discard" } },
		"Lay down": { "drag": { "from": "hand", "to": "table" }, "button": true, "when": "count(hand) >= 3" },
		"Pass": [],
		"Tap the discard": { "click": "discard", "steps": "taps += 1" },
		"Sort hand": "sort(hand, ['value'])"
	},
	"turn": [
		"turns += 1",
		"offer(['Draw', 'Pass'])",
		"offer_until('Lay down', 'Discard')",
		{ "if": "turns >= 4", "then": "end_game()" },
		"end_turn()"
	],
	"triggers": { "start": ["deal(deck, all_hand, 2)"] }
}"""

var problems := []


func _init() -> void:
	offers_and_actions()
	turns_undo_and_replays()
	game_over_and_endless_turns()
	load_errors()
	print("TURN: ", "all passed" if problems.is_empty() else "FAILED %s" % [problems])
	quit(0 if problems.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		problems.append(what)


func game_with(changes := {}, watch := true) -> Game:
	var rules: Dictionary = JSON.parse_string(RULES)
	rules.merge(changes, true)
	var game := Game.new("res://turn_test.jsonc", 5, {"res://turn_test.jsonc": JSON.stringify(rules)})
	if watch:
		game.failed.connect(func(text: String) -> void: problems.append("failed: " + text))
	game.start()
	return game


func offers_and_actions() -> void:
	var game := game_with()
	check(game.errors.is_empty(), "test game loads: %s" % [game.errors])
	check(game.offered == ["Draw", "Pass"] and game.current_player() == 1, "the turn offers Draw or Pass: %s" % [game.offered])
	var card: Card = game.zones.hand_1.cards[0]
	check(not game.apply({type = "drag", card = card.id, to = "discard"}), "Discard isn't possible before it's offered")
	check(not game.can_perform("Lay down"), "nor is Lay down's button")
	game.apply({type = "perform", action = "Sort hand"})
	check(game.zones.hand_1.sort_columns == ["value"] and game.offered == ["Draw", "Pass"], "an action never offered works any time, and the offer goes on")
	game.apply({type = "click_empty", zone = "discard"})
	check(game.variables.taps == 1, "an action done by clicking a zone")
	game.apply({type = "click", card = game.zones.deck.cards.back().id})
	check(game.zones.hand_1.cards.size() == 3 and game.offered == ["Lay down", "Discard"], "clicking the deck does Draw, then the turn goes on: %s" % [game.offered])
	check(game.can_perform("Lay down"), "Lay down's button works while offered and possible")
	game.apply({type = "perform", action = "Lay down"})
	check(game.choosing == "Lay down" and game.choices.size() == 3, "its button asks for a card to do it with")
	game.apply({type = "click", card = game.choices[0].id})
	check(game.zones.table_1.cards.size() == 1 and game.offered == ["Lay down", "Discard"], "the card is laid down, and Discard or Lay down is offered again")
	check(not game.can_perform("Lay down"), "with 2 cards left, Lay down isn't possible")
	game.apply({type = "drag", card = game.zones.hand_1.cards[0].id, to = "discard"})
	check(game.current_player() == 2 and game.offered == ["Draw", "Pass"], "Discard ends the offers, and the turn passes")


func turns_undo_and_replays() -> void:
	var game := game_with()
	game.apply({type = "perform", action = "Pass"})
	game.apply({type = "drag", card = game.zones.hand_1.cards[0].id, to = "discard"})
	check(game.current_player() == 2 and game.variables.turns == 2, "player 2's turn")
	check(not game.apply({type = "perform", action = "Pass", player = 1}), "player 1 can't act on player 2's turn")
	game.apply({type = "undo"})
	check(game.current_player() == 1 and game.offered == ["Lay down", "Discard"] and game.variables.turns == 1, "undo goes back into player 1's turn: %s" % [game.offered])
	game.apply({type = "drag", card = game.zones.hand_1.cards[1].id, to = "discard"})
	var replayed := Game.new(game.rules_path, game.match_seed, game.given_files)
	replayed.start()
	for input in game.inputs:
		replayed.apply(input)
	check(replayed.state_hash() == game.state_hash(), "a replay ends in the same state")


func game_over_and_endless_turns() -> void:
	var game := game_with()
	for turn in 4:
		game.apply({type = "perform", action = "Pass"})
		game.apply({type = "drag", card = game.zones["hand_%d" % game.current_player()].cards[0].id, to = "discard"})
	check(game.over and game.offered.is_empty() and game.variables.turns == 4, "end_game() stops the turns after 4 (%d)" % game.variables.turns)
	check(not game.apply({type = "perform", action = "Pass"}) or game.variables.turns == 4, "offered actions can't be done once the game is over")
	game = game_with({"turn": ["end_turn()"]}, false)
	check(game.runner.turns_stopped, "turns that never wait for anyone are stopped")


func load_errors() -> void:
	var cases := {
		"moves": [{"moves": [{"from": "deck", "to": "hand"}]}, "\"moves\" are now actions with \"drag\""],
		"zone on_click": [{"zones": {"deck": {"cards": "res://decks/standard_deck.csv", "on_click": "draw(deck, discard)"}, "discard": {}, "table": {"per_player": true}, "hand": {"per_player": true}}}, "make an action with \"click\""],
		"phase": [{"actions": {"Pass": {"phase": "draw", "steps": []}}}, "\"phase\" on actions is gone"],
		"an unknown offer": [{"turn": ["offer('Drew')"]}, "no action named \"Drew\""],
		"an offer of a variable": [{"turn": ["offer(turns)"]}, "needs action names in quotes"],
		"click true without drag": [{"actions": {"Pass": {"click": true, "steps": []}}}, "it needs \"drag\""],
		"choose on a drag": [{"actions": {"Draw": {"drag": {"from": "deck", "to": "hand"}, "choose": "hand"}}}, "\"choose\" is for buttons"],
	}
	for name: String in cases:
		var rules: Dictionary = JSON.parse_string(RULES)
		rules.merge(cases[name][0], true)
		var game := Game.new("res://turn_mistake.jsonc", 1, {"res://turn_mistake.jsonc": JSON.stringify(rules)})
		check(game.errors.any(func(error: String) -> bool: return error.contains(cases[name][1])), "%s should be a load error with \"%s\": %s" % [name, cases[name][1], game.errors])
