extends Control

const GAMES_FOLDER := "res://games/"
const MAIN_SCENE := preload("res://scenes/main.tscn")
const DESCRIPTION_WIDTH := 460.0
const DESCRIPTION_COLOR := Color(1, 1, 1, 0.75)


func _ready() -> void:
	%Felt.color = TableView.FELT_COLOR
	_fit_window_to_screen()
	for path in _game_paths():
		_add_game(path, GameRules.load_json(path))


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


func _add_game(path: String, rules: GameRules) -> void:
	var button := Button.new()
	button.text = rules.title if not rules.error else path.get_file()
	button.disabled = rules.error != ""
	button.pressed.connect(_play.bind(path))
	%GameList.add_child(button)
	var description := Label.new()
	description.text = rules.error if rules.error else rules.description
	description.custom_minimum_size.x = DESCRIPTION_WIDTH
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.add_theme_color_override("font_color", DESCRIPTION_COLOR)
	%GameList.add_child(description)


func _play(path: String) -> void:
	var main := MAIN_SCENE.instantiate()
	main.rules_path = path
	get_tree().root.add_child(main)
	get_tree().current_scene = main
	queue_free()
