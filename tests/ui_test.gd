extends Node
## Opens the real screens: the title screen, the lobby, and every game on the table screen with bots playing
## every seat for a few moves. Reports load errors and failed steps; script errors appear in the output above.
## Run: godot --headless res://tests/ui_test.tscn

const MAIN_SCENE := preload("res://scenes/main.tscn")
const EDITOR_SCENE := preload("res://scenes/editor.tscn")
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
		await _edit(path)
	print("UI TEST: ", "all passed" if problems.is_empty() else "FAILED\n  " + "\n  ".join(problems))
	get_tree().quit(0 if problems.is_empty() else 1)


## The editor: open the game, drag a zone, change a deck cell, then undo it all back to the file as it was.
func _edit(path: String) -> void:
	var original := FileAccess.get_file_as_string(path)
	var editor: Node = EDITOR_SCENE.instantiate()
	editor.file_path = path
	add_child(editor)
	await get_tree().process_frame
	var name := path.get_file()
	if editor.document.text != original:
		problems.append("%s: the editor changed the file just by opening it" % name)
	var zone: Zone = editor.preview.game.zones.values()[0]
	editor._move_zone(zone, Vector2(0.3, 0.3))
	if editor.document.text == original:
		problems.append("%s: dragging %s didn't change the file" % [name, zone.name])
	editor.select_section("Deck")
	if editor.entry:
		var deck_text := FileAccess.get_file_as_string(editor.entry)
		editor._deck_edited(deck_text + deck_text.split("\n")[1] + "\n")
	editor.select_section("Table")
	await get_tree().create_timer(0.5).timeout
	if editor.preview.game.errors:
		problems.append("%s: after editing in the editor: %s" % [name, editor.preview.game.errors])
	while editor.history:
		editor.undo()
	if editor.document.text != original or editor.deck_texts:
		problems.append("%s: undoing every edit didn't give back the file" % name)
	_check_rule_editing(editor, name)
	editor.get_node("%PlayToggle").button_pressed = true
	await get_tree().process_frame
	editor.queue_free()
	await get_tree().process_frame


## Milestone 2: steps are checked and suggested as they're typed, and step lists can be added to and changed.
func _check_rule_editing(editor: Node, name: String) -> void:
	if editor._check_step("draw(deck, no_such_zone)").is_empty():
		problems.append("%s: a step naming a missing zone wasn't flagged" % name)
	var field: SmartField = editor.smart_field("tri")
	field.code.set_caret_column(3)
	field._suggest()
	var options := field.code.get_code_completion_options().map(func(option: Dictionary) -> String: return option.display_text)
	if not options.any(func(text: String) -> bool: return text.begins_with("trigger(")):
		problems.append("%s: no suggestion for 'tri': %s" % [name, options])
	field.free()
	editor.select_section("Triggers")
	if not editor.entry:
		return
	var before: String = editor.document.text
	var steps := StepList.new(editor, ["triggers", editor.entry])
	steps._add({"if": "true", "then": ["shuffle(deck)"]})
	var list: Array = editor.document.get_value(["triggers", editor.entry])
	if not (list.back() is Dictionary and list.back().get("if") == "true"):
		problems.append("%s: adding an if block to a trigger didn't work" % name)
	steps._move(list.size() - 1, 0)
	if not (editor.document.get_value(["triggers", editor.entry, 0]) is Dictionary):
		problems.append("%s: moving a step up didn't work" % name)
	steps.free()
	editor.undo()
	editor.undo()
	if editor.document.text != before:
		problems.append("%s: undoing step edits didn't give back the trigger" % name)


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
