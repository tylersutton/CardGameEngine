extends SceneTree
## Checks steps that wait for a choice (choose, choose_exactly, choose_up_to): picking cards, zones and values,
## carrying on afterwards, undo while waiting, choices inside loops and triggers, bots and replays.
## Run after changing StepRunner or choices: godot --headless -s tests/choose_test.gd

const RULES := """{
	"players": 2,
	"zones": {
		"deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
		"piles": { "layout": "pile", "copies": 2, "label": "Pile #" },
		"hand": { "per_player": true }
	},
	"variables": { "total": 0, "color": "" },
	"actions": {
		"Play one": ["picked = choose(hand, 'Play a card')", "move(picked, piles_1)", "total += 1"],
		"Deal to a pile": ["target = choose(piles, 'Which pile?')", "move(top(deck), target)"],
		"Pick a color": ["color = choose(['Red', 'Black'])"],
		"Pass two": ["move(choose_exactly(hand, 2, 'Pass two'), piles_2)"],
		"Discard some": ["gone = choose_up_to(hand, 2)", "move(gone, piles_2)", "total = count(gone)"],
		"Everyone plays": [{ "for": "player", "in": "players", "do": ["move(choose(hand), piles_1)"] }, "total = count(piles_1)"],
		"Ask in a trigger": ["trigger('choose_one')", "total = 100 + count(piles_1)"]
	},
	"triggers": {
		"start": ["deal(deck, all_hand, 3)"],
		"choose_one": ["move(choose(hand), piles_1)"]
	}
}"""

var problems := []


func _init() -> void:
	waits_and_carries_on()
	zones_and_values()
	several_cards()
	loops_and_triggers()
	undo_bots_and_replays()
	load_mistakes()
	print("CHOOSE: ", "all passed" if problems.is_empty() else "FAILED %s" % [problems])
	quit(0 if problems.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		problems.append(what)


func game_with(changes := {}) -> Game:
	var rules: Dictionary = JSON.parse_string(RULES)
	rules.merge(changes, true)
	var game := Game.new("res://choose_test.jsonc", 3, {"res://choose_test.jsonc": JSON.stringify(rules)})
	game.failed.connect(func(text: String) -> void: problems.append("failed: " + text))
	game.start()
	check(game.errors.is_empty(), "test game loads: %s" % [game.errors])
	return game


func click(game: Game, card: Card) -> void:
	game.apply({type = "click", card = card.id})


func waits_and_carries_on() -> void:
	var game := game_with()
	game.apply({type = "perform", action = "Play one"})
	check(game.choice.title == "Play a card" and game.choice.action == "" and game.runner.is_waiting(), "Play one waits: '%s'" % game.choice.title)
	check(game.choice.options.size() == 3, "the hand's 3 cards are offered: %d" % game.choice.options.size())
	check(not game.apply({type = "drag", card = game.zones.deck.cards.back().id, to = "piles_2"}), "nothing else happens while waiting")
	game.apply({type = "click", card = game.cards.find(game.zones.deck.cards.back())})
	check(game.runner.is_waiting(), "clicking a card that isn't offered doesn't cancel a choose")
	var card: Card = game.zones.hand_1.cards[1]
	click(game, card)
	check(card in game.zones.piles_1.cards and game.variables.total == 1, "the step carries on with the picked card")
	check(not game.runner.is_waiting() and not game.choice.is_open(), "nothing is waiting afterwards")


func zones_and_values() -> void:
	var game := game_with()
	game.apply({type = "perform", action = "Deal to a pile"})
	check(game.choice.options == [game.zones.piles_1, game.zones.piles_2], "the piles are offered: %s" % [game.choice.options])
	check(game.choice.option_text(game.zones.piles_2) == "Pile 2", "a pile's button shows its label: %s" % game.choice.option_text(game.zones.piles_2))
	game.apply({type = "choose", option = "piles_2"})
	check(game.zones.piles_2.cards.size() == 1, "choosing a zone by name")
	game.apply({type = "perform", action = "Deal to a pile"})
	game.apply({type = "click_empty", zone = "piles_1"})
	check(game.zones.piles_1.cards.size() == 1, "clicking an empty zone chooses it")
	game.apply({type = "perform", action = "Pick a color"})
	game.apply({type = "choose", option = "Black"})
	check(game.variables.color == "Black", "choosing a value: %s" % game.variables.color)


func several_cards() -> void:
	var game := game_with()
	game.apply({type = "perform", action = "Pass two"})
	var hand: Array = game.zones.hand_1.cards.duplicate()
	click(game, hand[0])
	check(game.runner.is_waiting(), "one of two picked: still waiting")
	click(game, hand[2])
	check(game.zones.piles_2.cards == [hand[0], hand[2]], "both picked cards move")
	game.apply({type = "perform", action = "Pass two"})
	check(game.zones.piles_2.cards.size() == 3 and not game.runner.is_waiting(), "with only one card left, it's passed without asking")
	game = game_with()
	game.apply({type = "perform", action = "Discard some"})
	game.apply({type = "finish"})
	check(game.variables.total == 0 and game.zones.hand_1.cards.size() == 3, "up to: Done with nothing picked")


func loops_and_triggers() -> void:
	var game := game_with()
	game.apply({type = "perform", action = "Everyone plays"})
	check(game.current_player() == 1 and game.runner.is_waiting(), "player 1 chooses first")
	check(not game.apply({type = "click", card = game.zones.hand_2.cards[0].id, player = 2}), "player 2 waits their turn")
	click(game, game.zones.hand_1.cards[0])
	check(game.current_player() == 2 and game.choice.options == game.zones.hand_2.cards, "then player 2 chooses from their own hand")
	game.apply({type = "click", card = game.zones.hand_2.cards[0].id, player = 2})
	check(game.variables.total == 2 and game.current_player() == 1, "both played, and the turn is back with player 1 (total %d)" % game.variables.total)
	game = game_with()
	game.apply({type = "perform", action = "Ask in a trigger"})
	check(game.variables.total == 0 and game.runner.is_waiting(), "the step after trigger() waits for the trigger's choice")
	click(game, game.zones.hand_1.cards[0])
	check(game.variables.total == 101, "then carries on: %d" % game.variables.total)


func undo_bots_and_replays() -> void:
	var game := game_with()
	game.apply({type = "perform", action = "Play one"})
	click(game, game.zones.hand_1.cards[0])
	game.apply({type = "undo"})
	check(game.runner.is_waiting() and game.zones.piles_1.cards.is_empty() and game.variables.total == 0, "undo goes back to the waiting choice")
	click(game, game.zones.hand_1.cards[2])
	check(game.variables.total == 1, "and the choice can be made again")
	var replayed := Game.new(game.rules_path, game.match_seed, game.given_files)
	replayed.start()
	for input in game.inputs:
		replayed.apply(input)
	check(replayed.state_hash() == game.state_hash(), "a replay ends in the same state")
	game = game_with({"bot": {"players": [1]}})
	game.apply({type = "perform", action = "Pass two"})
	check(Bot.new(game).take_input() and game.zones.piles_2.cards.size() == 2, "a bot answers a waiting choice")


func load_mistakes() -> void:
	var game := Game.new("res://choose_mistake.jsonc", 1, {"res://choose_mistake.jsonc": JSON.stringify({
		"zones": {"hand": {}},
		"actions": {"Go": {"when": "choose(hand) == null", "steps": []}}})})
	check(game.errors.any(func(error: String) -> bool: return error.contains("only works in steps")), "choose in a when is a load error: %s" % [game.errors])
