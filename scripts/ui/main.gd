extends Control

@export_file("*.json") var rules_path := "res://games/gin_rummy.json"

const BOT_DELAY := 0.6

var game: Game
var bot: Bot
var bot_waiting := false


func _ready() -> void:
	%ReloadButton.pressed.connect(_load_game)
	%UndoButton.pressed.connect(func() -> void: game.undo())
	%SandboxToggle.toggled.connect(_set_sandbox)
	_load_game()


func _load_game() -> void:
	var new_game := Game.new(rules_path)
	if new_game.errors:
		for error in new_game.errors:
			%LogView.add_error(error)
		return
	game = new_game
	game.sandbox = %SandboxToggle.button_pressed
	%LogView.clear()
	game.logged.connect(%LogView.add)
	game.failed.connect(%LogView.add_error)
	game.state_changed.connect(_refresh_action_buttons)
	game.state_changed.connect(_refresh_choice_bar)
	game.state_changed.connect(_schedule_bot)
	bot = Bot.new(game)
	%TableView.show_game(game)
	%StatusView.show_game(game)
	_add_action_buttons()
	game.start()


func _set_sandbox(enabled: bool) -> void:
	if game:
		game.sandbox = enabled
		_refresh_action_buttons()


func _add_action_buttons() -> void:
	for button in %ActionButtons.get_children():
		button.free()
	for action_name in game.rules.actions:
		if not game.rules.actions[action_name].get("button", true):
			continue
		var button := Button.new()
		button.text = action_name
		button.pressed.connect(game.perform.bind(action_name))
		%ActionButtons.add_child(button)


func _refresh_action_buttons() -> void:
	%UndoButton.disabled = game.history.is_empty()
	for button: Button in %ActionButtons.get_children():
		button.disabled = not game.can_perform(button.text)


func _refresh_choice_bar() -> void:
	for child in %ChoiceBar.get_children():
		%ChoiceBar.remove_child(child)
		child.queue_free()
	%ChoiceMargin.visible = game.choosing != "" and not game.choosing_cards()
	if not %ChoiceMargin.visible:
		return
	var prompt := Label.new()
	prompt.text = game.choosing + ":"
	%ChoiceBar.add_child(prompt)
	for option in game.choices:
		var button := Button.new()
		button.text = Game.format_value(option)
		button.pressed.connect(game.choose_option.bind(option))
		%ChoiceBar.add_child(button)


func _schedule_bot() -> void:
	if bot_waiting or not game.is_bot_turn():
		return
	bot_waiting = true
	await get_tree().create_timer(BOT_DELAY).timeout
	bot_waiting = false
	bot.take_input()
