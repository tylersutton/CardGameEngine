extends Control
## The game editor: sections on the left (each with a list of entries, like the zones), the table in the middle
## (drag zones in Edit mode, play the game in Play mode), the selected entry's details on the right, and the
## game's problems along the bottom. Every change edits the game file's text in place (GameDocument), so
## comments and layout survive; nothing is written to disk until Save.

const MAIN_SCENE := preload("res://scenes/main.tscn")
const TITLE_SCENE := "res://scenes/title.tscn"
const SECTIONS := ["Game", "Table", "Deck"]
## Changes to the same setting within this long count as one step for Undo (e.g. dragging a number).
const MERGE_TIME := 1500
const RELOAD_DELAY := 0.3
const NEW_ZONE := {"layout": "pile", "anchor": [0.5, 0.5]}

@export_file("*.jsonc", "*.json") var file_path := "res://games/color_match.jsonc"

var document: GameDocument
## Decks edited here, by path, until saved.
var deck_texts: Dictionary = {}
var history: Array[Dictionary] = []
var future: Array[Dictionary] = []
var saved_state: Dictionary
var last_change_path: Array = []
var last_change_time := 0
var section := ""
var entry := ""
var preview: Control
var dragger: ZoneDragger
var deck_table := DeckTable.new()
var reload_timer := Timer.new()


func _ready() -> void:
	document = GameDocument.open(file_path)
	saved_state = _state()
	%BackButton.pressed.connect(_leave)
	%SaveButton.pressed.connect(save)
	%UndoButton.pressed.connect(undo)
	%RedoButton.pressed.connect(redo)
	%PlayToggle.toggled.connect(_set_playing)
	%SectionList.item_selected.connect(func(index: int) -> void: select_section(SECTIONS[index]))
	%EntryList.item_selected.connect(func(index: int) -> void: select_entry(%EntryList.get_item_text(index)))
	%Problems.item_activated.connect(func(index: int) -> void: _go_to_problem(%Problems.get_item_text(index)))
	for name in SECTIONS:
		%SectionList.add_item(name)
	reload_timer.one_shot = true
	reload_timer.timeout.connect(_reload)
	add_child(reload_timer)
	preview = MAIN_SCENE.instantiate()
	preview.rules_path = file_path
	preview.embedded = true
	preview.given_files = _files()
	preview.set_anchors_preset(Control.PRESET_FULL_RECT)
	%Center.add_child(preview)
	preview.show_side_panel(false)
	var table: TableView = preview.get_node("%TableView")
	dragger = ZoneDragger.new(table)
	dragger.zone_picked.connect(func(zone: Zone) -> void:
		if section != "Table":
			select_section("Table")
		select_entry(zone.kind))
	dragger.zone_moved.connect(_move_zone)
	table.add_child(dragger)
	deck_table.set_anchors_preset(Control.PRESET_FULL_RECT)
	deck_table.edited.connect(_deck_edited)
	%Center.add_child(deck_table)
	select_section("Table")
	_reload()


func _unhandled_key_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_undo"):
		undo()
	elif event.is_action_pressed("ui_redo"):
		redo()


# --- Changing the game ------------------------------------------------------------------------------------

## Every edit from the forms comes through here, so it can be undone. A null value takes the key out.
func change(path: Array, value: Variant) -> void:
	_remember(path)
	if value == null:
		document.remove(path)
	else:
		document.set_value(path, value)
	_changed(false)


## Bigger edits (adding, renaming, removing), as a function that changes the document.
func edit(how: Callable) -> void:
	_remember([])
	how.call()
	_changed(true)


func undo() -> void:
	if history:
		future.append(_state())
		_restore(history.pop_back())


func redo() -> void:
	if future:
		history.append(_state())
		_restore(future.pop_back())


func save() -> void:
	if document.save() != OK:
		_show_problems(["Couldn't save " + file_path])
		return
	for path: String in deck_texts:
		var file := FileAccess.open(path, FileAccess.WRITE)
		file.store_string(deck_texts[path])
		file.close()
	saved_state = _state()
	_update_title()


func _remember(path: Array) -> void:
	var now := Time.get_ticks_msec()
	if path.is_empty() or path != last_change_path or now - last_change_time > MERGE_TIME:
		history.append(_state())
		future.clear()
	last_change_path = path
	last_change_time = now


func _state() -> Dictionary:
	return {rules = document.text, decks = deck_texts.duplicate()}


func _restore(state: Dictionary) -> void:
	document.set_text(state.rules)
	deck_texts = state.decks.duplicate()
	last_change_path = []
	_changed(true)


## After any change: the lists and title catch up, and the table and problems are checked again shortly.
func _changed(rebuild_details: bool) -> void:
	_update_title()
	_fill_entries()
	if rebuild_details:
		_show_details()
	reload_timer.start(RELOAD_DELAY)


func _update_title() -> void:
	var unsaved := _state() != saved_state
	%FileLabel.text = file_path.get_file() + (" (unsaved)" if unsaved else "")


func _files() -> Dictionary:
	var files := {file_path: document.text}
	files.merge(deck_texts, true)
	return files


func _reload() -> void:
	preview.given_files = _files()
	preview.load_game()
	dragger.queue_redraw()
	if document.parse_error:
		_show_problems(["The game file can't be read: " + document.parse_error])
	else:
		_show_problems(Game.new(file_path, 1, _files()).errors)


func _show_problems(problems: Array) -> void:
	%Problems.clear()
	for problem: String in problems:
		%Problems.add_item(problem)
	if problems.is_empty():
		%Problems.add_item("No problems found.")


func _leave() -> void:
	if _state() == saved_state:
		get_tree().change_scene_to_file(TITLE_SCENE)
		return
	var dialog := ConfirmationDialog.new()
	dialog.dialog_text = "Leave without saving your changes?"
	dialog.ok_button_text = "Leave"
	dialog.confirmed.connect(func() -> void: get_tree().change_scene_to_file(TITLE_SCENE))
	add_child(dialog)
	dialog.popup_centered()


func _set_playing(playing: bool) -> void:
	dragger.visible = not playing
	preview.show_side_panel(playing)
	_reload()


# --- Sections and entries ---------------------------------------------------------------------------------

func select_section(name: String) -> void:
	section = name
	%SectionList.select(SECTIONS.find(name))
	entry = ""
	_fill_entries()
	var entries := _entries()
	if entries:
		select_entry(entries[0])
	else:
		_show_details()


func select_entry(name: String) -> void:
	entry = name
	for index in %EntryList.item_count:
		if %EntryList.get_item_text(index) == name:
			%EntryList.select(index)
	dragger.selected_kind = name if section == "Table" else ""
	dragger.queue_redraw()
	_show_details()


func _entries() -> Array:
	match section:
		"Table":
			return document.get_value(["zones"], {}).keys()
		"Deck":
			return _decks_used()
	return []


func _decks_used() -> Array:
	var decks := []
	for zone: Variant in document.get_value(["zones"], {}).values():
		if zone is Dictionary and zone.get("cards") is String and zone.cards not in decks:
			decks.append(zone.cards)
	return decks


func _fill_entries() -> void:
	%EntryList.clear()
	for name: String in _entries():
		%EntryList.add_item(name)
		if name == entry:
			%EntryList.select(%EntryList.item_count - 1)
	for child in %EntryButtons.get_children():
		child.queue_free()
	if section == "Table":
		_add_entry_button("Add zone", func() -> void: _ask("New zone", "", _add_zone))
		_add_entry_button("Rename", func() -> void: if entry: _ask("Rename zone (moves and steps using the old name aren't changed)", entry, _rename_zone))
		_add_entry_button("Remove", func() -> void: if entry: edit(func() -> void: document.remove(["zones", entry])))


func _add_entry_button(text: String, pressed: Callable) -> void:
	var button := Button.new()
	button.text = text
	button.pressed.connect(pressed)
	%EntryButtons.add_child(button)


func _add_zone(name: String) -> void:
	if name.is_valid_identifier() and not document.has(["zones", name]):
		edit(func() -> void: document.set_value(["zones", name], NEW_ZONE))
		select_entry(name)


func _rename_zone(name: String) -> void:
	if name.is_valid_identifier() and not document.has(["zones", name]):
		var old := entry
		edit(func() -> void: document.rename_key(["zones", old], name))
		select_entry(name)


# --- The details panel and the middle ---------------------------------------------------------------------

func _show_details() -> void:
	for child in %Details.get_children():
		child.queue_free()
	preview.visible = section != "Deck"
	deck_table.visible = section == "Deck"
	match section:
		"Game":
			_add_form([], EditorFields.GAME)
		"Table":
			if entry:
				_add_heading("Zone: " + entry)
				_add_note(["zones", entry])
				_add_form(["zones", entry], EditorFields.ZONE)
			else:
				_add_heading("No zones yet: press Add zone.")
		"Deck":
			if entry:
				var text: String = deck_texts.get(entry, FileAccess.get_file_as_string(entry))
				deck_table.show_deck(text, document.get_value(["card_face"], GameRules.DEFAULT_CARD_FACE))
				_add_heading("Card face")
				_add_form(["card_face"], EditorFields.CARD_FACE, Array(deck_table.rows[0]) if deck_table.rows else [])
			else:
				_add_heading("No deck yet: in Table, pick a zone and choose its cards.")


func _add_form(path: Array, fields: Array, columns: Array = []) -> void:
	var form := EditorForm.new()
	form.show_fields(document, path, fields, columns)
	form.changed.connect(func(changed_path: Array, value: Variant) -> void:
		change(changed_path, value)
		if value == null or changed_path.back() in ["per_player", "copies", "cards"]:
			_show_details.call_deferred())
	%Details.add_child(form)


func _add_heading(text: String) -> void:
	var label := Label.new()
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", 18)
	%Details.add_child(label)


## The comment lines above an entry in the file, editable as a note.
func _add_note(path: Array) -> void:
	var note := TextEdit.new()
	note.placeholder_text = "Note (kept as a // comment in the file)"
	note.text = document.note(path)
	note.custom_minimum_size.y = 50
	note.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	note.focus_exited.connect(func() -> void:
		if note.text != document.note(path):
			edit(func() -> void: document.set_note(path, note.text)))
	%Details.add_child(note)


func _deck_edited(text: String) -> void:
	_remember(["deck", entry])
	deck_texts[entry] = text
	_changed(false)


## Dragging a zone on the table: per-player zones placed round the table move seat 1's spot, grids move as a
## whole, and zones placed one by one ("anchors") move just that one.
func _move_zone(zone: Zone, spot: Vector2) -> void:
	var path: Array = ["zones", zone.kind]
	var definition: Dictionary = document.get_value(path, {})
	var number := int(zone.name.trim_prefix(zone.kind + "_")) if zone.name != zone.kind else 0
	var players: int = preview.game.rules.players if preview.game else 1
	if number and definition.has("anchors"):
		path += ["anchors", number - 1]
	else:
		path.append("anchor")
		if number > 1 and definition.get("per_player", false):
			spot = Vector2(0.5, 0.5) + (spot - Vector2(0.5, 0.5)).rotated(-TAU * (number - 1) / players)
		elif number and definition.has("grid"):
			var start: Array = definition.get("anchor", [0.5, 0.5])
			spot = Vector2(start[0], start[1]) + spot - zone.anchor
	change(path, [snappedf(spot.x, 0.005), snappedf(spot.y, 0.005)])
	_show_details()


## Problems name what they're about: a zone, or a step's text, which is looked up in the file.
func _go_to_problem(problem: String) -> void:
	var zone := RegEx.create_from_string("^Zone \"(\\w+)\"").search(problem)
	if zone:
		select_section("Table")
		select_entry(zone.get_string(1))
		return
	var quoted := RegEx.create_from_string("\"([^\"]+)\"").search(problem)
	var path := _path_holding(document.data, quoted.get_string(1), []) if quoted else []
	if path.size() >= 2 and path[0] == "zones":
		select_section("Table")
		select_entry(path[1])
	elif path and path[0] == "card_face":
		select_section("Deck")


## The path of the first value or key in the file equal to the text.
func _path_holding(value: Variant, text: String, path: Array) -> Array:
	if value is String and value == text:
		return path
	if value is Dictionary:
		for key: String in value:
			if key == text:
				return path + [key]
			var found := _path_holding(value[key], text, path + [key])
			if found:
				return found
	if value is Array:
		for i in value.size():
			var found := _path_holding(value[i], text, path + [i])
			if found:
				return found
	return []


func _ask(title: String, start: String, then: Callable) -> void:
	var dialog := ConfirmationDialog.new()
	dialog.title = title
	var line := LineEdit.new()
	line.text = start
	dialog.add_child(line)
	dialog.register_text_enter(line)
	dialog.confirmed.connect(func() -> void: then.call(line.text.strip_edges()))
	add_child(dialog)
	dialog.popup_centered(Vector2i(420, 80))
	line.grab_focus()
