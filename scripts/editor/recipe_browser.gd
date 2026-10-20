class_name RecipeBrowser
extends VBoxContainer
## The sidebar's Recipes page: ready-made pieces of a game (Recipes), what each one does, and a button to add the
## chosen one to the game.

signal add_asked(recipe: String)

var list := ItemList.new()
var description := Label.new()
var add := Button.new()
var paths := Recipes.paths()


func _ready() -> void:
	add_theme_constant_override("separation", 6)
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	for path: String in paths:
		list.add_item(Recipes.title(path), EditorIcons.icon("recipes", EditorTheme.TEXT_DIM))
	list.item_selected.connect(_show)
	list.item_activated.connect(func(index: int) -> void: add_asked.emit(paths[index]))
	description.theme_type_variation = &"DimLabel"
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add.theme_type_variation = &"AccentButton"
	add.text = "Add to game"
	add.icon = EditorIcons.icon("plus", Color.WHITE, 0.85)
	add.pressed.connect(func() -> void:
		if list.get_selected_items():
			add_asked.emit(paths[list.get_selected_items()[0]]))
	var details := VBoxContainer.new()
	details.add_child(description)
	details.add_child(add)
	add_child(list)
	add_child(EditorTheme.padded(details, 10, 8))
	if paths:
		list.select(0)
		_show(0)


func _show(index: int) -> void:
	description.text = Recipes.description(paths[index])
