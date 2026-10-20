extends SceneTree
## Plays every game in games/ to its end with random bots in every seat (pressing the buttons only people press,
## like Next hand), and checks it ends with end_game() and nothing fails on the way. Games with no end (solitaire,
## the sandbox, President's endless hands) just play a while at random. Run after changing games or turns:
## godot --headless -s tests/playthrough_test.gd

const MOST_INPUTS := 3000
const ENDLESS_INPUTS := 300
## Games that never call end_game(): one player games, the sandbox, and President (hands go on for ever).
const ENDLESS := ["pyramid.jsonc", "solitaire.jsonc", "standard.jsonc", "president.jsonc"]
## Games too long for random bots to finish (they rarely knock in Gin): finishing a hand will do.
const LONG := ["gin_rummy.jsonc"]
## Bots with an aim, for games random play wouldn't finish (Color Match's random bots draw as often as they play).
const PREFER := {"color_match.jsonc": "count(opponent_hand) - count(hand)"}

var problems := []


func _init() -> void:
	for path in GameRules.game_paths():
		_play(path)
	print("PLAYTHROUGH: ", "all passed" if problems.is_empty() else "FAILED\n  " + "\n  ".join(problems))
	quit(0 if problems.is_empty() else 1)


func _play(path: String) -> void:
	var data: Dictionary = GameRules.read_file(path)
	var file_name := path.get_file()
	# Bots without a prefer pick at random with Godot's own random numbers: seeded, the test plays the same each time.
	seed(11)
	data.bot = {"players": range(1, int(data.get("players", 1)) + 1)}
	if file_name in PREFER:
		data.bot.prefer = PREFER[file_name]
	var game := Game.new(path, 11, {path: JSON.stringify(data)})
	var failures := []
	game.failed.connect(func(text: String) -> void: failures.append(text))
	game.start()
	var bot := Bot.new(game)
	var chooser := RandomNumberGenerator.new()
	chooser.seed = 11
	var endless := file_name in ENDLESS
	var started_at := Time.get_ticks_msec()
	var inputs := 0
	var next_hands := [0]
	game.input_applied.connect(func(input: Dictionary) -> void: next_hands[0] += int(input.get("action") == "Next hand"))
	while inputs < (ENDLESS_INPUTS if endless else MOST_INPUTS) and not game.over and failures.is_empty():
		var moved := _random_move(game, chooser) if endless else bot.take_input()
		if not moved and not _press_a_button(game):
			break
		inputs += 1
	var ended := "ended: " + game.result_text() if game.over else "stopped after %d inputs, %d hands" % [inputs, next_hands[0]]
	print("  %s: %s, %d ms" % [file_name, ended, Time.get_ticks_msec() - started_at])
	if failures:
		problems.append("%s: %s" % [file_name, failures[0]])
	elif file_name in LONG and next_hands[0] == 0:
		problems.append("%s didn't finish a hand (%d inputs)" % [file_name, inputs])
	elif not game.over and not endless and file_name not in LONG:
		problems.append("%s didn't end (%d inputs; offered %s, choosing '%s')" % [file_name, inputs, game.plays.offered, game.choice.title])


func _random_move(game: Game, chooser: RandomNumberGenerator) -> bool:
	var moves := game.plays.legal_moves()
	if moves.is_empty():
		return false
	game.apply_move(moves[chooser.randi_range(0, moves.size() - 1)])
	return true


## A button only people press (Collect trick, Next hand), as the person at the table would.
func _press_a_button(game: Game) -> bool:
	for action_name: String in game.rules.actions:
		if game.rules.actions[action_name].get("human_only", false) and game.plays.can_perform(action_name):
			game.apply({type = "perform", action = action_name})
			return true
	return false
