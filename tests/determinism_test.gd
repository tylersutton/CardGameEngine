extends SceneTree
## Plays every game in games/ with random legal moves (plus some bot moves and undos), feeding each accepted
## input to a second copy made from the same seed, and checks both copies stay identical after every input.
## Any step that fails along the way is reported too. Then checks that replaying the match's record, sent through JSON like a network message, ends in the same state, and that out-of-turn inputs are refused.
## Run: godot --headless -s tests/determinism_test.gd

const SEED := 1234
const STEPS := 150
const BOT_MOVES := 15
const UNDO_EVERY := 25

var failures: Array[String] = []


func _init() -> void:
	for path in GameRules.game_paths():
		_check_game(path)
	_check_turns()
	print("DETERMINISM: ", "all passed" if failures.is_empty() else "FAILED\n  " + "\n  ".join(failures))
	quit(0 if failures.is_empty() else 1)


func _check_game(path: String) -> void:
	var started_at := Time.get_ticks_msec()
	var game := Game.new(path, SEED)
	var copy := Game.new(path, SEED)
	game.failed.connect(func(text: String) -> void: failures.append("%s: %s" % [path.get_file(), text]))
	game.start()
	copy.start()
	var bot := Bot.new(game)
	var chooser := RandomNumberGenerator.new()
	chooser.seed = SEED
	var bot_moves := 0
	for step in STEPS:
		var applied_before := game.inputs.size()
		if step % UNDO_EVERY == UNDO_EVERY - 1:
			game.apply({type = "undo"})
		elif game.is_bot_turn() and bot_moves < BOT_MOVES and bot.take_input():
			bot_moves += 1
		elif not _random_move(game, chooser):
			break
		for input in game.inputs.slice(applied_before):
			copy.apply(input)
		if copy.state_hash() != game.state_hash():
			failures.append("%s: copies differ after input %d" % [path.get_file(), game.inputs.size()])
			return
	var sent_record: Dictionary = JSON.parse_string(JSON.stringify(game.record()))
	if Game.replay(sent_record).state_hash() != game.state_hash():
		failures.append("%s: the replay (sent as JSON) ends differently" % path.get_file())
	print("  %s: %d inputs, %d by bots, %d ms" % [path.get_file(), game.inputs.size(), bot_moves, Time.get_ticks_msec() - started_at])


## A random legal move, or when there are none, a random toolbar button (like Hearts' "Collect trick").
func _random_move(game: Game, chooser: RandomNumberGenerator) -> bool:
	var moves := game.legal_moves()
	if moves:
		game.apply_move(moves[chooser.randi_range(0, moves.size() - 1)])
		return true
	var buttons := game.rules.actions.keys().filter(func(action_name: String) -> bool:
		return game.shows_button(action_name) and game.can_perform(action_name))
	if buttons.is_empty():
		return false
	game.apply({type = "perform", action = buttons[chooser.randi_range(0, buttons.size() - 1)]})
	return true


func _check_turns() -> void:
	var game := Game.new("res://games/gin_rummy.jsonc", SEED)
	game.start()
	var move: Array = game.legal_moves()[0]
	var not_their_turn: Dictionary = move[0].duplicate()
	not_their_turn.player = 2
	if game.apply(not_their_turn):
		failures.append("gin_rummy.jsonc: player 2 acted on player 1's turn")
	var their_turn: Dictionary = move[0].duplicate()
	their_turn.player = 1
	if not game.apply(their_turn):
		failures.append("gin_rummy.jsonc: player 1 couldn't act on their own turn")
