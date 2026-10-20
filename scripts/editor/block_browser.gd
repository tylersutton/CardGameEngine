class_name BlockBrowser
extends VBoxContainer
## The sidebar's Building blocks page: every building block by kind (from BlockHelp), searchable, with what it
## does. Double-click or Insert puts it into the step being typed (insert_asked).

signal insert_asked(text: String)

var search := LineEdit.new()
var tree := Tree.new()
var description := Label.new()


func _ready() -> void:
	add_theme_constant_override("separation", 6)
	search.placeholder_text = "Search building blocks"
	search.clear_button_enabled = true
	search.right_icon = EditorIcons.icon("search", EditorTheme.TEXT_DIM)
	search.text_changed.connect(func(_text: String) -> void: _fill())
	add_child(EditorTheme.padded(search, 8, 0))
	tree.hide_root = true
	tree.size_flags_vertical = Control.SIZE_EXPAND_FILL
	tree.item_selected.connect(_show)
	tree.item_activated.connect(func() -> void: _insert())
	add_child(tree)
	description.theme_type_variation = &"DimLabel"
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	var insert := Button.new()
	insert.text = "Insert into step"
	insert.icon = EditorIcons.icon("plus", Color.WHITE, 0.85)
	insert.tooltip_text = "Put it where you were typing a step (double-click does the same)"
	insert.pressed.connect(_insert)
	var details := VBoxContainer.new()
	details.add_child(description)
	details.add_child(insert)
	add_child(EditorTheme.padded(details, 10, 8))
	_fill()


func _fill() -> void:
	tree.clear()
	var root := tree.create_item()
	var typed := search.text.to_lower()
	var categories := {}
	for entry: Dictionary in BlockHelp.entries:
		if typed and typed not in entry.title.to_lower() and typed not in entry.text.to_lower():
			continue
		if entry.category not in categories:
			var heading := tree.create_item(root)
			heading.set_text(0, entry.category)
			heading.set_selectable(0, false)
			heading.set_custom_color(0, EditorTheme.TEXT_DIM)
			categories[entry.category] = heading
		var item := tree.create_item(categories[entry.category])
		item.set_text(0, entry.title)
		item.set_icon(0, EditorIcons.icon("function", EditorTheme.FUNCTION, 0.85))
		item.set_custom_font(0, EditorTheme.code_font)
		item.set_tooltip_text(0, entry.text)
		item.set_metadata(0, entry)
	description.text = "Pick a building block to see what it does." if BlockHelp.entries else "The building blocks are listed in docs/reference.md."


func _show() -> void:
	var entry: Dictionary = tree.get_selected().get_metadata(0)
	description.text = entry.title + "\n" + entry.text


func _insert() -> void:
	var item := tree.get_selected()
	if item and item.get_metadata(0):
		insert_asked.emit(item.get_metadata(0).names[0] + "(")
