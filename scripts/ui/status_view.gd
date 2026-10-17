class_name StatusView
extends Label

var game: Game


func show_game(game_to_show: Game) -> void:
	game = game_to_show
	game.variables_changed.connect(_refresh)
	game.state_changed.connect(_refresh)
	_refresh()


func _refresh() -> void:
	var values := {}
	for name in (game.rules.show_variables if game.rules.show_variables else game.variables.keys()):
		values[name] = Game.format_value(game.variables.get(name))
	values.merge(game.status_values())
	visible = not values.is_empty()
	text = "\n".join(values.keys().map(func(name: String) -> String: return "%s: %s" % [name, values[name]]))
