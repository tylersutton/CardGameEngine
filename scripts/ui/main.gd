extends Control

@export_file("*.json") var rules_path := "res://games/gin_rummy.json"

const BOT_DELAY := 0.6
const TITLE_SCENE := "res://scenes/title.tscn"

var game: Game
var bot: Bot
var bot_waiting := false


func _ready() -> void:
	%GamesButton.pressed.connect(_leave)
	%ReloadButton.pressed.connect(_load_game)
	%UndoButton.pressed.connect(func() -> void: if game: _submit({type = "undo"}))
	%SandboxToggle.toggled.connect(_set_sandbox)
	%ZoneNamesToggle.toggled.connect(_show_zone_names)
	if Net.online:
		%ReloadButton.disabled = true
		%SandboxToggle.disabled = true
		Net.match_started.connect(_load_game)
		Net.undo_asked.connect(_ask_undo)
		Net.notice.connect(%LogView.add)
		Net.disconnected.connect(_leave)
	_load_game()


## Offline the table changes the game directly; online, inputs go through the session.
func _submit(input: Dictionary) -> bool:
	return Net.submit(input) if Net.online else game.apply(input)


func _leave() -> void:
	Net.leave()
	get_tree().change_scene_to_file(TITLE_SCENE)


func _ask_undo(asker: String) -> void:
	var dialog := ConfirmationDialog.new()
	dialog.dialog_text = "%s wants to undo the last move. Allow it?" % asker
	dialog.ok_button_text = "Allow"
	dialog.cancel_button_text = "Don't allow"
	dialog.confirmed.connect(Net.answer_undo.bind(true))
	dialog.canceled.connect(Net.answer_undo.bind(false))
	add_child(dialog)
	dialog.popup_centered()


func _load_game() -> void:
	var new_game := Net.game if Net.online else Game.new(rules_path)
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
	%TableView.submit = _submit
	%TableView.show_game(game)
	%StatusView.show_game(game)
	_add_action_buttons()
	game.start()
	if Net.online:
		Net.catch_up()


func _set_sandbox(enabled: bool) -> void:
	if game:
		game.sandbox = enabled
		_refresh_action_buttons()


func _show_zone_names(enabled: bool) -> void:
	%TableView.show_zone_names = enabled
	%TableView.queue_redraw()


func _add_action_buttons() -> void:
	for button in %ActionButtons.get_children():
		button.free()
	for action_name in game.rules.actions:
		if not game.rules.actions[action_name].get("button", true):
			continue
		var button := Button.new()
		button.text = action_name
		button.pressed.connect(func() -> void: _submit({type = "perform", action = action_name}))
		%ActionButtons.add_child(button)


func _refresh_action_buttons() -> void:
	%UndoButton.disabled = game.history.is_empty()
	for button: Button in %ActionButtons.get_children():
		button.disabled = not game.can_perform(button.text) or not _may_press(button.text)


func _may_press(action_name: String) -> bool:
	return not Net.online or game.may_act(game.local_seat, {type = "perform", action = action_name})


func _refresh_choice_bar() -> void:
	for child in %ChoiceBar.get_children():
		%ChoiceBar.remove_child(child)
		child.queue_free()
	var picking_cards := game.choosing_cards() and not game.pick_exact
	%ChoiceMargin.visible = game.choosing != "" and (picking_cards or not game.choosing_cards()) and game.is_local_turn()
	if not %ChoiceMargin.visible:
		return
	var prompt := Label.new()
	prompt.text = game.choosing + ":"
	if picking_cards:
		prompt.text += " choose up to %d (%d selected)" % [game.pick_count, game.selected.size()]
	%ChoiceBar.add_child(prompt)
	if picking_cards:
		_add_choice_button("Done", func() -> void: _submit({type = "finish"}))
	else:
		for option in game.choices:
			_add_choice_button(Game.format_value(option), func() -> void: _submit({type = "choose", option = option}))


func _add_choice_button(text: String, on_pressed: Callable) -> void:
	var button := Button.new()
	button.text = text
	button.pressed.connect(on_pressed)
	%ChoiceBar.add_child(button)


## Bots play on the host only; everyone else gets their moves like any other input.
func _schedule_bot() -> void:
	if bot_waiting or not game.is_bot_turn() or (Net.online and not Net.is_host()):
		return
	bot_waiting = true
	await get_tree().create_timer(BOT_DELAY).timeout
	bot_waiting = false
	bot.take_input()
