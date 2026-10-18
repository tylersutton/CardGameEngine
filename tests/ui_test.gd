extends Node
## Opens the real screens: the title screen, the lobby, and every game on the table screen with bots playing
## every seat for a few moves. Reports load errors and failed steps; script errors appear in the output above.
## Run: godot --headless res://tests/ui_test.tscn

const MAIN_SCENE := preload("res://scenes/main.tscn")
const BOT_MOVES := 12

var problems: Array[String] = []


func _ready() -> void:
	for path in ["res://scenes/title.tscn", "res://scenes/lobby.tscn"]:
		var screen: Node = load(path).instantiate()
		add_child(screen)
		await get_tree().process_frame
		screen.queue_free()
	for path in GameRules.game_paths():
		await _play(path)
	print("UI TEST: ", "all passed" if problems.is_empty() else "FAILED\n  " + "\n  ".join(problems))
	get_tree().quit(0 if problems.is_empty() else 1)


func _play(path: String) -> void:
	var main: Node = MAIN_SCENE.instantiate()
	main.rules_path = path
	add_child(main)
	await get_tree().process_frame
	var game: Game = main.game
	if not game:
		problems.append("%s: didn't load" % path.get_file())
		main.queue_free()
		return
	game.failed.connect(func(text: String) -> void: problems.append("%s: %s" % [path.get_file(), text]))
	game.rules.bot.players = range(1, game.rules.players + 1)
	var bot := Bot.new(game)
	for move in BOT_MOVES:
		var buttons := game.rules.actions.keys().filter(func(action: String) -> bool:
			return game.rules.actions[action].get("human_only", false) and game.can_perform(action))
		if buttons:
			game.apply({type = "perform", action = buttons[0]})
		elif not bot.take_input():
			break
		await get_tree().process_frame
	main.queue_free()
	await get_tree().process_frame
