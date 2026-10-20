class_name CommandPalette
extends PopupPanel
## Ctrl+P: a search box over every command and every part of the game. Type a few letters (in order, gaps
## allowed), then Enter or click to do it. Each entry is {text, detail, icon, run}.

const WIDTH := 560
const LIST_HEIGHT := 320

var search := LineEdit.new()
var list := ItemList.new()
var entries: Array = []
var shown: Array = []


func _ready() -> void:
	var column := VBoxContainer.new()
	search.placeholder_text = "Search commands, zones, actions..."
	search.right_icon = EditorIcons.icon("search", EditorTheme.TEXT_DIM)
	search.text_changed.connect(func(_text: String) -> void: _filter())
	search.gui_input.connect(_on_search_key)
	search.text_submitted.connect(func(_text: String) -> void: _run(_chosen()))
	list.custom_minimum_size = Vector2(WIDTH, LIST_HEIGHT)
	list.focus_mode = Control.FOCUS_NONE
	# item_clicked also fires for the mouse wheel, which would run whatever was scrolled past.
	list.item_clicked.connect(func(index: int, _at: Vector2, button: int) -> void:
		if button == MOUSE_BUTTON_LEFT:
			_run(index))
	column.add_child(search)
	column.add_child(list)
	add_child(column)


func open(all_entries: Array) -> void:
	entries = all_entries
	search.text = ""
	_filter()
	var area := get_parent().get_viewport().get_visible_rect().size
	popup(Rect2i(Vector2i(int((area.x - WIDTH) / 2), 56), Vector2i(WIDTH, 0)))
	search.grab_focus()


func _filter() -> void:
	var typed := search.text.to_lower().replace(" ", "")
	var scored := []
	for i in entries.size():
		var score := _score(typed, (entries[i].text + entries[i].detail).to_lower())
		if score >= 0:
			scored.append([score, i])
	# Equal scores keep the order they were given in.
	scored.sort_custom(func(a: Array, b: Array) -> bool: return a[0] < b[0] or a[0] == b[0] and a[1] < b[1])
	shown = scored.map(func(pair: Array) -> Dictionary: return entries[pair[1]])
	list.clear()
	for entry: Dictionary in shown:
		var index := list.add_item("%s    %s" % [entry.text, entry.detail] if entry.detail else entry.text, EditorIcons.icon(entry.icon, EditorTheme.TEXT_DIM))
		list.set_item_tooltip(index, entry.text)
	if shown:
		list.select(0)


## How well the typed letters match: lower is better, -1 is no match. They must all appear in order; words that
## start with them, or contain them together, come first.
static func _score(typed: String, text: String) -> int:
	if typed.is_empty():
		return 0
	var together := text.replace(" ", "").find(typed)
	if together >= 0:
		return together
	var at := 0
	var gaps := 0
	for letter in typed:
		var found := text.find(letter, at)
		if found < 0:
			return -1
		gaps += found - at
		at = found + 1
	return 100 + gaps


func _on_search_key(event: InputEvent) -> void:
	if not (event is InputEventKey and event.pressed) or shown.is_empty():
		return
	var step := 0
	match event.keycode:
		KEY_DOWN:
			step = 1
		KEY_UP:
			step = -1
		KEY_ESCAPE:
			hide()
			search.accept_event()
			return
	if step:
		var index := posmod(_chosen() + step, shown.size())
		list.select(index)
		list.ensure_current_is_visible()
		search.accept_event()


func _chosen() -> int:
	var selected := list.get_selected_items()
	return selected[0] if selected else 0


func _run(index: int) -> void:
	if index < shown.size():
		hide()
		shown[index].run.call()
