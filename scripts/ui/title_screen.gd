extends Control

const MAIN_SCENE := preload("res://scenes/main.tscn")
const LOBBY_SCENE := "res://scenes/lobby.tscn"
const EDITOR_SCENE := preload("res://scenes/editor.tscn")
const DECKS_FOLDER := "res://decks/"
const TEMPLATES := {1: "res://templates/new_solo_game.jsonc", 2: "res://templates/new_game.jsonc"}
const MAX_PLAYERS := 6
const SEAT_NAMES := ["You", "Player 2", "Player 3", "Player 4", "Player 5", "Player 6"]
const SELECTED_COLOR := Color(1, 0.85, 0.4)

static var selected_style := _selected_box()

var selected_path := ""


func _ready() -> void:
	%Felt.color = TableView.FELT_COLOR
	_fit_window_to_screen()
	%Play.pressed.connect(func() -> void: _play(selected_path))
	%OnlineButton.pressed.connect(func() -> void: get_tree().change_scene_to_file(LOBBY_SCENE))
	%NewGameButton.pressed.connect(_ask_for_new_game)
	%EditButton.pressed.connect(func() -> void: OS.shell_open(ProjectSettings.globalize_path(selected_path)))
	%OpenEditorButton.pressed.connect(func() -> void: _open_editor(selected_path))
	_list_games()


func _list_games(select := "") -> void:
	for child in %GameList.get_children():
		child.free()
	var group := ButtonGroup.new()
	var games := GameRules.game_paths().map(func(path: String) -> Array: return [path, GameRules.load_json(path)])
	games.sort_custom(func(a: Array, b: Array) -> bool: return _title(a[0], a[1]).naturalnocasecmp_to(_title(b[0], b[1])) < 0)
	for game: Array in games:
		_add_game(game[0], game[1], group)
	var chosen: Button = null
	for button: Button in %GameList.get_children():
		if not chosen or button.get_meta("path") == select:
			chosen = button
	if chosen:
		chosen.button_pressed = true
		chosen.grab_focus()


## "New game...": a name, a player count and a deck make a playable game file from a template, ready to edit.
func _ask_for_new_game() -> void:
	var dialog := ConfirmationDialog.new()
	dialog.title = "New game"
	dialog.ok_button_text = "Create"
	var form := GridContainer.new()
	form.columns = 2
	var game_name := LineEdit.new()
	game_name.text = "My Game"
	game_name.custom_minimum_size.x = 240
	var players := SpinBox.new()
	players.min_value = 1
	players.max_value = MAX_PLAYERS
	players.value = 2
	var deck := OptionButton.new()
	for file in DirAccess.get_files_at(DECKS_FOLDER):
		if file.get_extension() == "csv":
			deck.add_item(file)
			if file == "standard_deck.csv":
				deck.select(deck.item_count - 1)
	for row: Array in [["Name", game_name], ["Players", players], ["Deck", deck]]:
		var label := Label.new()
		label.text = row[0]
		form.add_child(label)
		form.add_child(row[1])
	dialog.add_child(form)
	dialog.confirmed.connect(func() -> void:
		_create_game(game_name.text.strip_edges(), int(players.value), deck.get_item_text(deck.selected)))
	add_child(dialog)
	dialog.popup_centered()


func _create_game(title: String, players: int, deck: String) -> void:
	var base_name := title.to_snake_case().validate_filename() if title else "my_game"
	var path := GameRules.GAMES_FOLDER + base_name + ".jsonc"
	var number := 2
	while FileAccess.file_exists(path):
		path = GameRules.GAMES_FOLDER + "%s_%d.jsonc" % [base_name, number]
		number += 1
	var template := FileAccess.get_file_as_string(TEMPLATES[mini(players, 2)])
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string(template.format({
		title = title if title else "My Game", players = players, deck = deck,
		player_names = JSON.stringify(SEAT_NAMES.slice(0, players)), bot_players = JSON.stringify(range(2, players + 1)),
	}))
	file.close()
	_open_editor(path)


func _fit_window_to_screen() -> void:
	var screen := DisplayServer.screen_get_usable_rect().size
	var window := DisplayServer.window_get_size()
	if window.x > screen.x or window.y > screen.y:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_MAXIMIZED)


## Selecting a game (click, or arrow keys) shows its description; double-click or Play starts it.
func _add_game(path: String, rules: GameRules, group: ButtonGroup) -> void:
	var button := Button.new()
	button.text = _title(path, rules)
	button.set_meta("path", path)
	button.toggle_mode = true
	button.add_theme_stylebox_override("pressed", selected_style)
	button.add_theme_color_override("font_pressed_color", Color.BLACK)
	button.button_group = group
	button.alignment = HORIZONTAL_ALIGNMENT_LEFT
	button.toggled.connect(func(pressed: bool) -> void:
		if pressed:
			_show(path, rules))
	button.focus_entered.connect(func() -> void: button.button_pressed = true)
	button.gui_input.connect(func(event: InputEvent) -> void:
		if event is InputEventMouseButton and event.double_click and not rules.error:
			_play(path))
	%GameList.add_child(button)


func _show(path: String, rules: GameRules) -> void:
	selected_path = path
	%GameTitle.text = _title(path, rules)
	%Description.text = rules.error if rules.error else rules.description
	%Play.disabled = rules.error != ""


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_accept") and selected_path and not %Play.disabled:
		_play(selected_path)


static func _title(path: String, rules: GameRules) -> String:
	return rules.title if not rules.error else path.get_file()


static func _selected_box() -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = SELECTED_COLOR
	box.set_corner_radius_all(4)
	box.content_margin_left = 4
	return box


func _open_editor(path: String) -> void:
	var editor := EDITOR_SCENE.instantiate()
	editor.file_path = path
	get_tree().root.add_child(editor)
	get_tree().current_scene = editor
	queue_free()


func _play(path: String) -> void:
	var main := MAIN_SCENE.instantiate()
	main.rules_path = path
	get_tree().root.add_child(main)
	get_tree().current_scene = main
	queue_free()
