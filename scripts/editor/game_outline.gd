class_name GameOutline
extends Tree
## The editor's outline: every part of the game, with its named entries (zones, actions...) under their section.
## Picking a row sends picked(section, entry). A section's + button, the right-click menu, F2 and Delete ask the
## editor to add, rename, duplicate or remove entries; the editor makes the change and shows the outline again.

signal picked(section: String, entry: String)
signal add_asked(section: String)
signal rename_asked(section: String, entry: String, new_name: String)
signal duplicate_asked(section: String, entry: String)
signal remove_asked(section: String, entry: String)

const ICONS := {
	"Game": "game", "Table": "table", "Deck": "deck", "Players": "players", "Variables": "variables",
	"Actions": "actions", "Turn": "turn", "Triggers": "triggers", "Conditions": "conditions", "Scoring": "scoring",
	"Melds": "melds",
}
const ADD := 0
const MENU_RENAME := 0
const MENU_DUPLICATE := 1
const MENU_REMOVE := 2
const MENU_ADD := 3

var menu := PopupMenu.new()
var filter := ""
## Sections the user folded away, kept when the outline is shown again.
var folded: Dictionary = {}
## While the editor is filling or selecting rows, so that doesn't count as the user picking one.
var _quiet := false


func _init() -> void:
	hide_root = true
	columns = 2
	set_column_expand(1, false)
	set_column_custom_minimum_width(1, 64)
	select_mode = SELECT_ROW
	allow_rmb_select = true
	scroll_horizontal_enabled = false
	item_selected.connect(_on_selected)
	item_collapsed.connect(func(item: TreeItem) -> void:
		if not _quiet and item.get_parent() == get_root():
			folded[item.get_metadata(0)[0]] = item.collapsed)
	button_clicked.connect(func(item: TreeItem, _column: int, id: int, _mouse: int) -> void:
		if id == ADD:
			add_asked.emit(item.get_metadata(0)[0]))
	item_mouse_selected.connect(func(at: Vector2, button: int) -> void:
		if button == MOUSE_BUTTON_RIGHT:
			_open_menu(get_global_transform() * at))
	item_edited.connect(_on_renamed)
	menu.id_pressed.connect(_on_menu)
	add_child(menu)


## sections: one dictionary per section: {name, entries: [[key, label, detail], ...], noun (for named entries),
## count (shown beside it)}. problems: "section/entry" (or "section") -> what's wrong there.
func show_sections(sections: Array, problems: Dictionary, section: String, entry: String) -> void:
	_quiet = true
	clear()
	var root := create_item()
	var lowered := filter.to_lower()
	for part: Dictionary in sections:
		var item := create_item(root)
		item.set_text(0, part.name)
		item.set_icon(0, EditorIcons.icon(ICONS[part.name]))
		item.set_icon_max_width(0, 16)
		item.set_icon_modulate(0, EditorTheme.TEXT_DIM)
		item.set_metadata(0, [part.name, ""])
		if part.get("count", 0):
			item.set_text(1, str(part.count))
			item.set_custom_color(1, EditorTheme.TEXT_DIM)
			item.set_text_alignment(1, HORIZONTAL_ALIGNMENT_RIGHT)
		if part.get("noun", ""):
			item.add_button(1, EditorIcons.icon("plus", Color.WHITE, 0.85), ADD, false, "Add a " + part.noun)
		var matches_section: bool = lowered.is_empty() or lowered in part.name.to_lower()
		var any_entry_matches := false
		for row: Array in part.get("entries", []):
			var child := create_item(item)
			child.set_text(0, row[1])
			child.set_metadata(0, [part.name, row[0]])
			if row.size() > 2:
				child.set_text(1, row[2])
				child.set_custom_color(1, EditorTheme.TEXT_DIM)
				child.set_text_alignment(1, HORIZONTAL_ALIGNMENT_RIGHT)
			var problem: String = problems.get(part.name + "/" + row[0], "")
			if problem:
				_mark(child, problem)
			child.visible = matches_section or lowered in str(row[1]).to_lower()
			any_entry_matches = any_entry_matches or child.visible
			if part.name == section and row[0] == entry:
				child.select(0)
		item.visible = matches_section or any_entry_matches
		item.collapsed = folded.get(part.name, false) and lowered.is_empty()
		var problem: String = problems.get(part.name, "")
		if problem:
			_mark(item, problem)
		if part.name == section and entry.is_empty():
			item.select(0)
	_quiet = false


## Selects the row without sending picked.
func show_selected(section: String, entry: String) -> void:
	_quiet = true
	deselect_all()
	for item in get_root().get_children() if get_root() else []:
		if item.get_metadata(0)[0] != section:
			continue
		var target := item
		for child in item.get_children():
			if child.get_metadata(0)[1] == entry:
				target = child
		if target != item:
			item.collapsed = false
		target.select(0)
		scroll_to_item(target)
	_quiet = false


## Starts renaming the selected entry in place.
func start_rename() -> void:
	var item := get_selected()
	if item and item.get_metadata(0)[1] and _can_edit(item):
		item.set_editable(0, true)
		edit_selected(true)


func _mark(item: TreeItem, problem: String) -> void:
	item.set_custom_color(0, EditorTheme.ERROR)
	item.set_tooltip_text(0, problem)


func _on_selected() -> void:
	if not _quiet:
		var where: Array = get_selected().get_metadata(0)
		picked.emit(where[0], where[1])


func _on_renamed() -> void:
	var item := get_edited()
	item.set_editable(0, false)
	var where: Array = item.get_metadata(0)
	var new_name := item.get_text(0).strip_edges()
	if new_name != where[1]:
		rename_asked.emit(where[0], where[1], new_name)


func _can_edit(item: TreeItem) -> bool:
	var section_item := item if item.get_parent() == get_root() else item.get_parent()
	return section_item.get_button_count(1) > 0


func _gui_input(event: InputEvent) -> void:
	var item := get_selected()
	if not (event is InputEventKey and event.pressed and item):
		return
	var where: Array = item.get_metadata(0)
	if event.keycode == KEY_F2:
		start_rename()
		accept_event()
	elif event.keycode == KEY_DELETE and where[1] and _can_edit(item):
		remove_asked.emit(where[0], where[1])
		accept_event()


func _open_menu(at: Vector2) -> void:
	var item := get_selected()
	if not item or not _can_edit(item):
		return
	var where: Array = item.get_metadata(0)
	menu.clear()
	if where[1]:
		menu.add_icon_item(EditorIcons.icon("rename"), "Rename", MENU_RENAME, KEY_F2)
		menu.add_icon_item(EditorIcons.icon("duplicate"), "Duplicate", MENU_DUPLICATE)
		menu.add_separator()
		menu.add_icon_item(EditorIcons.icon("trash"), "Delete", MENU_REMOVE, KEY_DELETE)
	else:
		menu.add_icon_item(EditorIcons.icon("plus"), "Add", MENU_ADD)
	menu.reset_size()
	menu.popup(Rect2i(Vector2i(at), Vector2i.ZERO))


func _on_menu(id: int) -> void:
	var where: Array = get_selected().get_metadata(0)
	match id:
		MENU_RENAME:
			start_rename()
		MENU_DUPLICATE:
			duplicate_asked.emit(where[0], where[1])
		MENU_REMOVE:
			remove_asked.emit(where[0], where[1])
		MENU_ADD:
			add_asked.emit(where[0])
