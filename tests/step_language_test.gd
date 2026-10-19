extends SceneTree
## Checks the step language (StepParser, StepEvaluator, StepCompiler): what steps work out to, changing variables,
## and the messages for mistakes, both when the game loads and while it runs.
## Run after changing the language: godot --headless -s tests/step_language_test.gd

const RULES := """{
	"players": 2,
	"zones": {
		"deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
		"pile": { "layout": "pile" },
		"hand": { "per_player": true }
	},
	"variables": { "phase": "draw", "total": 0, "names": [] },
	"player_variables": { "score": 0 },
	"conditions": { "is_ace": "card.rank == 'A'", "deck_full": "count(deck) == 52" }
}"""

var problems := []


func _init() -> void:
	values()
	changing_variables()
	running_mistakes()
	load_mistakes()
	print("STEP LANGUAGE: ", "all passed" if problems.is_empty() else "FAILED %s" % problems)
	quit(0 if problems.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		problems.append(what)


func game_with(changes := {}) -> Game:
	var rules: Dictionary = JSON.parse_string(RULES)
	rules.merge(changes, true)
	var game := Game.new("res://step_language_test.jsonc", 1, {"res://step_language_test.jsonc": JSON.stringify(rules)})
	game.start()
	return game


func expect(game: Game, step: String, wanted: Variant) -> void:
	var result: Variant = game.evaluate(step)
	check(not game.evaluation_failed and typeof(result) == typeof(wanted) and result == wanted, "%s gave %s, wanted %s" % [step, result, wanted])


func values() -> void:
	var game := game_with()
	check(game.errors.is_empty(), "test game loads: %s" % [game.errors])
	expect(game, "1 + 2 * 3", 7)
	expect(game, "(1 + 2) * 3", 9)
	expect(game, "7 / 2", 3)
	expect(game, "7.0 / 2", 3.5)
	expect(game, "-2 - -3", 1)
	expect(game, "10 % 4", 2)
	expect(game, "not 1 == 2", true)
	expect(game, "not deck_full or 1 == 1", true)
	expect(game, "'big' if count(deck) > 10 else 'small'", "big")
	expect(game, "'a' if false else 'b' if false else 'c'", "c")
	expect(game, "count(pile) > 0 and top(pile).rank == 'A'", false)
	expect(game, "count(pile) == 0 or top(pile).rank == 'A'", true)
	expect(game, "'%s has %d' % ['deck', count(deck)]", "deck has 52")
	expect(game, "[1, 2] + [3]", [1, 2, 3])
	expect(game, "['a', 'b', 'c'][1]", "b")
	expect(game, "'King\\'s Court'", "King's Court")
	expect(game, "min(3, 1, 2) + max(4, 5)", 6)
	expect(game, "int(true) + abs(-2)", 3)
	expect(game, "phase != null", true)
	expect(game, "phase == 'draw' and total == 0", true)
	expect(game, "score == opponent_score", true)
	var ace: Card = game.cards.filter(func(card: Card) -> bool: return card.title == "A♠")[0]
	check(game.evaluate("is_ace", ace) == true, "conditions see the step's card")
	check(game.evaluate("card.value + 1", ace) == 2, "number columns are numbers")


func changing_variables() -> void:
	var game := game_with()
	game.run(["total = 5", "total += 2", "total -= 1", "names += ['x']", "score += 3", "player = 2", "score += 10"])
	check(game.variables.total == 6, "= += -= on total: %s" % game.variables.total)
	check(game.variables.names == ["x"], "+= joins lists: %s" % [game.variables.names])
	check(game.variables.score_1 == 3 and game.variables.score_2 == 10, "score means the current player's: %s, %s" % [game.variables.score_1, game.variables.score_2])


func running_mistakes() -> void:
	var game := game_with()
	var failures := []
	game.failed.connect(func(text: String) -> void: failures.append(text))
	var ace: Card = game.cards.filter(func(card: Card) -> bool: return card.title == "A♠")[0]
	var cases := {
		"card.rank == 1": "put the number in quotes",
		"card.colour": "no column \"colour\"",
		"top(pile).rank": "can't read .rank of nothing",
		"draw('deck', pile)": "\"from\" should be a zone, not the text 'deck'",
		"total / 0": "can't divide by zero",
		"phase - 1": "can't work out the text 'draw' - the number 1",
		"[1, 2][5]": "outside the list",
	}
	for step: String in cases:
		failures.clear()
		game.evaluate(step, ace)
		check(game.evaluation_failed and failures.size() == 1 and failures[0].contains(cases[step]), "%s should fail with \"%s\": %s" % [step, cases[step], failures])


func load_mistakes() -> void:
	var cases := {
		"\"total = 1\" as a condition": [{"moves": [{"from": "deck", "to": "pile", "when": "total = 1"}]}, "only works as a step of its own"],
		"assigning a zone": [{"triggers": {"start": ["pile = 1"]}}, "is a zone"],
		"assigning an unknown variable": [{"triggers": {"start": ["totl = 1"]}}, "unknown variable \"totl\""],
		"double quotes": [{"triggers": {"start": ["move(top(deck), \"pile\")"]}}, "single quotes"],
		"!": [{"triggers": {"start": ["!deck_full"]}}, "use not instead of !"],
		"a missing bracket": [{"triggers": {"start": ["draw(deck, pile"]}}, "expected \")\""],
		"an unknown building block": [{"triggers": {"start": ["shufle(deck)"]}}, "unknown building block \"shufle\""],
		"too many arguments": [{"triggers": {"start": ["shuffle(deck, pile)"]}}, "shuffle takes 1 argument, got 2"],
		"a condition naming itself": [{"conditions": {"a": "b", "b": "not a"}}, "refers to itself"],
		"viewer() in a rule": [{"triggers": {"start": ["total = viewer()"]}}, "viewer() only works in \"status\""],
	}
	for name: String in cases:
		var game := game_with(cases[name][0])
		check(game.errors.any(func(error: String) -> bool: return error.contains(cases[name][1])), "%s should be a load error with \"%s\": %s" % [name, cases[name][1], game.errors])
