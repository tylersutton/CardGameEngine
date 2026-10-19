extends SceneTree
## Checks players as values: a player's own variables and zones (score[2], hand[p]), comparing players (most,
## players_with_most, players_where), knocking players out, reset and end_game's winners.
## Run after changing players or turns: godot --headless -s tests/players_test.gd

const RULES := """{
	"players": 3,
	"player_names": ["Ann", "Bo", "Cy"],
	"zones": {
		"deck": { "layout": "pile", "cards": "res://decks/standard_deck.csv" },
		"hand": { "per_player": true }
	},
	"variables": { "passes": 3, "taken": null },
	"player_variables": { "score": 0, "lives": 2 },
	"conditions": { "big_hand": "count(hand) >= 2" },
	"actions": { "Pass": [] },
	"triggers": { "start": ["deal(deck, [hand_1, hand_2, hand_2], 1)"] }
}"""

var problems := []


func _init() -> void:
	any_players_values()
	comparing_players()
	knocking_out()
	resetting_and_winners()
	load_mistakes()
	print("PLAYERS: ", "all passed" if problems.is_empty() else "FAILED %s" % [problems])
	quit(0 if problems.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		problems.append(what)


func game_with(changes := {}) -> Game:
	var rules: Dictionary = JSON.parse_string(RULES)
	rules.merge(changes, true)
	var game := Game.new("res://players_test.jsonc", 2, {"res://players_test.jsonc": JSON.stringify(rules)})
	game.start()
	return game


func run(game: Game, steps: Array) -> void:
	game.failed.connect(func(text: String) -> void: problems.append("failed: " + text))
	game.run(steps)


func any_players_values() -> void:
	var game := game_with()
	check(game.errors.is_empty(), "test game loads: %s" % [game.errors])
	run(game, ["score[2] = 5", "score[3] += 7", "score += 1"])
	check(game.variables.score_1 == 1 and game.variables.score_2 == 5 and game.variables.score_3 == 7, "score[n] reads and changes player n's score")
	check(game.evaluate("count(hand[2])") == 2 and game.evaluate("score[next_player()]") == 5, "hand[2] is player 2's hand")
	var unwatched := game_with()
	unwatched.evaluate("score[4]")
	check(unwatched.evaluation_failed, "score[4] with 3 players is a mistake")


func comparing_players() -> void:
	var game := game_with()
	run(game, ["score[1] = 4", "score[2] = 9", "score[3] = 9"])
	check(game.evaluate("most('score')") == 9 and game.evaluate("least('score')") == 4, "most and least")
	check(game.evaluate("players_with_most('score')") == [2, 3] and game.evaluate("players_with_least('score')") == [1], "who has the most, ties included")
	check(game.evaluate("players_where('big_hand')") == [2], "players_where works out a condition as each player")
	check(game.current_player() == 1, "and the turn is back where it was")


func knocking_out() -> void:
	var game := game_with()
	run(game, ["knock_out(2)"])
	check(game.evaluate("players_left()") == [1, 3] and game.evaluate("next_player()") == 3, "a player knocked out is skipped")
	check(game.evaluate("count(opponent_hand)") == 0, "opponent_ skips them too (player 3's empty hand)")
	game.end_turn()
	check(game.current_player() == 3, "end_turn skips them")
	game.end_turn()
	check(game.current_player() == 1, "and wraps round")
	run(game, ["bring_back(players)"])
	check(game.evaluate("players_left()") == [1, 2, 3], "bring_back puts everyone back")


func resetting_and_winners() -> void:
	var game := game_with()
	run(game, ["passes = 0", "taken = top(deck)", "lives[2] = 0", "reset(['passes', 'taken', 'lives'])"])
	check(game.variables.passes == 3 and game.variables.taken == null and game.variables.lives_2 == 2, "reset puts variables back to their starting values")
	run(game, ["end_game([2, 3])"])
	check(game.over and game.result_text() == "Winners: Bo and Cy", "end_game announces the winners: %s" % game.result_text())
	game = game_with()
	run(game, ["end_game(1)"])
	check(game.result_text() == "Winner: Ann", "one winner: %s" % game.result_text())
	game = game_with()
	run(game, ["end_game()"])
	check(game.result_text() == "Game over", "no winner: %s" % game.result_text())


func load_mistakes() -> void:
	var cases := {
		"setting a player's zone": [{"triggers": {"start": ["hand[2] = 1"]}}, "only works for player (or team) variables"],
		"setting a shared variable by number": [{"triggers": {"start": ["passes[2] = 1"]}}, "only works for player (or team) variables"],
	}
	for name: String in cases:
		var game := game_with(cases[name][0])
		check(game.errors.any(func(error: String) -> bool: return error.contains(cases[name][1])), "%s should be a load error with \"%s\": %s" % [name, cases[name][1], game.errors])
