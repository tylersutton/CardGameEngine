extends Control

const GAMES_FOLDER := "res://games/"
const MAIN_SCENE := preload("res://scenes/main.tscn")
const SELECTED_COLOR := Color(1, 0.85, 0.4)

static var selected_style := _selected_box()

var selected_path := ""


func _ready() -> void:
	%Felt.color = TableView.FELT_COLOR
	_fit_window_to_screen()
	%Play.pressed.connect(func() -> void: _play(selected_path))
	var group := ButtonGroup.new()
	var games := _game_paths().map(func(path: String) -> Array: return [path, GameRules.load_json(path)])
	games.sort_custom(func(a: Array, b: Array) -> bool: return _title(a[0], a[1]).naturalnocasecmp_to(_title(b[0], b[1])) < 0)
	for game: Array in games:
		_add_game(game[0], game[1], group)
	var first: Button = %GameList.get_child(0) if %GameList.get_child_count() else null
	if first:
		first.button_pressed = true
		first.grab_focus()


func _fit_window_to_screen() -> void:
	var screen := DisplayServer.screen_get_usable_rect().size
	var window := DisplayServer.window_get_size()
	if window.x > screen.x or window.y > screen.y:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_MAXIMIZED)


func _game_paths() -> Array[String]:
	var paths: Array[String] = []
	for file in DirAccess.get_files_at(GAMES_FOLDER):
		if file.get_extension() == "json":
			paths.append(GAMES_FOLDER + file)
	return paths


## Selecting a game (click, or arrow keys) shows its description; double-click or Play starts it.
func _add_game(path: String, rules: GameRules, group: ButtonGroup) -> void:
	var button := Button.new()
	button.text = _title(path, rules)
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


func _play(path: String) -> void:
	var main := MAIN_SCENE.instantiate()
	main.rules_path = path
	get_tree().root.add_child(main)
	get_tree().current_scene = main
	queue_free()
