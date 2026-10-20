extends Control

@export_file("*.jsonc", "*.json") var rules_path := "res://games/gin_rummy.jsonc"

const BOT_DELAY := 0.6
const TITLE_SCENE := "res://scenes/title.tscn"
const WATCH_INTERVAL := 1.0

var game: Game
var bot: Bot
var bot_waiting := false
## Modified times of the game file and its decks, to reload when one is saved.
var watched_files: Dictionary = {}
## Game and deck text to play instead of the files on disk (the editor's unsaved edits).
var given_files: Dictionary = {}
## Inside the editor: no Games or Reload buttons, and no reloading when files change (the editor reloads it).
var embedded := false


func _ready() -> void:
	%GamesButton.pressed.connect(_leave)
	%ReloadButton.pressed.connect(load_game)
	%UndoButton.pressed.connect(func() -> void: if game: _submit({type = "undo"}))
	%SandboxToggle.toggled.connect(_set_sandbox)
	%ZoneNamesToggle.toggled.connect(_show_zone_names)
	%GamesButton.visible = not embedded
	%ReloadButton.visible = not embedded
	if not Net.online and not embedded:
		var watcher := Timer.new()
		watcher.timeout.connect(_reload_if_changed)
		add_child(watcher)
		watcher.start(WATCH_INTERVAL)
	if Net.online:
		%ReloadButton.disabled = true
		%SandboxToggle.disabled = true
		Net.match_started.connect(load_game)
		Net.undo_asked.connect(_ask_undo)
		Net.notice.connect(%LogView.add)
		Net.disconnected.connect(_leave)
	load_game()


## The variables and log beside the table (the editor hides them while you arrange the table).
## The editor hides the toolbar and log while it is laying out the table, not playing.
func show_play_controls(shown: bool) -> void:
	$Layout/TableColumn/ToolbarMargin.visible = shown
	$Layout/LogPanel.visible = shown


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


## Saving the game file or a deck reloads the game, so changes show up without pressing Reload.
func _reload_if_changed() -> void:
	for path in watched_files:
		if FileAccess.get_modified_time(path) != watched_files[path]:
			load_game()
			%LogView.add("[reloaded: %s changed]" % path.get_file())
			return


func load_game() -> void:
	var new_game := Net.game if Net.online else Game.new(rules_path, -1, given_files)
	watched_files.clear()
	for path in new_game.rules.files_used(rules_path):
		watched_files[path] = FileAccess.get_modified_time(path)
	if new_game.errors:
		for error in new_game.errors:
			%LogView.add_error(error)
		return
	game = new_game
	game.sandbox = %SandboxToggle.button_pressed
	%LogView.clear()
	game.game_log.logged.connect(%LogView.add)
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
		if not game.plays.shows_button(action_name):
			continue
		var button := Button.new()
		button.text = action_name
		button.pressed.connect(func() -> void: _submit({type = "perform", action = action_name}))
		%ActionButtons.add_child(button)


func _refresh_action_buttons() -> void:
	%UndoButton.disabled = game.history.is_empty()
	for button: Button in %ActionButtons.get_children():
		button.disabled = not game.plays.can_perform(button.text) or not _may_press(button.text)


func _may_press(action_name: String) -> bool:
	return not Net.online or game.may_act(game.local_seat, {type = "perform", action = action_name})


func _refresh_choice_bar() -> void:
	for child in %ChoiceBar.get_children():
		%ChoiceBar.remove_child(child)
		child.queue_free()
	var picking_cards := game.choice.cards_offered() and not game.choice.exactly
	%ChoiceMargin.visible = game.over or (game.choice.is_open() or not game.plays.offered.is_empty()) and game.is_local_turn()
	if not %ChoiceMargin.visible:
		return
	if game.over:
		var result := Label.new()
		result.text = game.result_text()
		%ChoiceBar.add_child(result)
		return
	if not game.choice.is_open():
		var offer := Label.new()
		offer.text = "Your turn: " + " or ".join(game.plays.offered)
		%ChoiceBar.add_child(offer)
		return
	var prompt := Label.new()
	prompt.text = game.choice.title + ":"
	if picking_cards:
		prompt.text += " choose up to %d (%d selected)" % [game.choice.pick, game.choice.selected.size()]
	elif game.choice.cards_offered():
		prompt.text += " click %s" % ("a highlighted card" if game.choice.pick == 1 else "%d highlighted cards (%d selected)" % [game.choice.pick, game.choice.selected.size()])
	%ChoiceBar.add_child(prompt)
	if picking_cards:
		_add_choice_button("Done", func() -> void: _submit({type = "finish"}))
	elif not game.choice.cards_offered():
		for option in game.choice.options:
			var sent: Variant = option.name if option is Zone else option
			_add_choice_button(game.choice.option_text(option), func() -> void: _submit({type = "choose", option = sent}))


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
