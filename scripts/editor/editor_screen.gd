extends Control
## The game editor: sections on the left (each with a list of entries, like the zones), the table in the middle
## (drag zones in Edit mode, play the game in Play mode), the selected entry's details on the right, and the
## game's problems along the bottom. Every change edits the game file's text in place (GameDocument), so
## comments and layout survive; nothing is written to disk until Save.

const MAIN_SCENE := preload("res://scenes/main.tscn")
const TITLE_SCENE := "res://scenes/title.tscn"
const SECTIONS := ["Game", "Table", "Deck", "Variables", "Moves", "Buttons", "Triggers", "Conditions"]
## Changes to the same setting within this long count as one step for Undo (e.g. dragging a number).
const MERGE_TIME := 1500
const RELOAD_DELAY := 0.3
const NEW_ZONE := {"layout": "pile", "anchor": [0.5, 0.5]}
const NEW_MOVE := {"from": "hand", "to": "discard"}
## Sections listing named entries: where they live in the file, what one is called, and what a new one holds.
const NAMED := {
	"Table": {key = "zones", noun = "zone", new = NEW_ZONE},
	"Buttons": {key = "actions", noun = "button", new = {"steps": []}},
	"Triggers": {key = "triggers", noun = "trigger", new = []},
	"Conditions": {key = "conditions", noun = "condition", new = "true"},
}
const VARIABLE_TABLES := [
	["variables", "Variables", "Shared values, with their starting values, e.g. phase \"draw\" or passes 0."],
	["player_variables", "Player variables", "One of each per player (score_1, score_2); in steps, score means the current player's."],
	["team_variables", "Team variables", "One of each per team (needs teams, under Players)."],
]
## Which section each part of the file is edited in.
const SECTION_OF := {
	"title": "Game", "description": "Game", "zones": "Table", "card_face": "Deck", "variables": "Variables",
	"player_variables": "Variables", "team_variables": "Variables", "moves": "Moves", "actions": "Buttons",
	"triggers": "Triggers", "conditions": "Conditions",
}
const TRIGGER_HELP := {
	"start": "Runs when the game begins (and on Reload): deal the cards, set things up.",
	"turn_start": "Runs after every end_turn().",
}

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
## The game as last checked, for suggesting names and checking steps as they're typed.
var checked_game: Game


func _ready() -> void:
	document = GameDocument.open(file_path)
	saved_state = _state()
	%BackButton.pressed.connect(_leave)
	%SaveButton.pressed.connect(save)
	%UndoButton.pressed.connect(undo)
	%RedoButton.pressed.connect(redo)
	%PlayToggle.toggled.connect(_set_playing)
	%SectionList.item_selected.connect(func(index: int) -> void: select_section(SECTIONS[index]))
	%EntryList.item_selected.connect(func(index: int) -> void: select_entry(%EntryList.get_item_metadata(index)))
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
	checked_game = Game.new(file_path, 1, _files())
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
		checked_game = Game.new(file_path, 1, _files())
		_show_problems(checked_game.errors)


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
		select_entry(entries[0][0])
	else:
		_show_details()


func select_entry(key: String) -> void:
	entry = key
	for index in %EntryList.item_count:
		if %EntryList.get_item_metadata(index) == key:
			%EntryList.select(index)
	dragger.selected_kind = key if section == "Table" else ""
	dragger.queue_redraw()
	_show_details()


## The section's entries as [key, label] pairs.
func _entries() -> Array:
	if section in NAMED:
		return document.get_value([NAMED[section].key], {}).keys().map(func(name: String) -> Array: return [name, name])
	match section:
		"Deck":
			return _decks_used().map(func(deck: String) -> Array: return [deck, deck.get_file()])
		"Moves":
			var moves: Array = document.get_value(["moves"], [])
			return range(moves.size()).map(func(i: int) -> Array: return [str(i), "%d. %s" % [i + 1, _move_label(moves[i])]])
	return []


static func _move_label(rule: Variant) -> String:
	if rule is not Dictionary:
		return "?"
	var ends: Array = ["from", "to"].map(func(end: String) -> String:
		var zones: Variant = rule.get(end, "anywhere")
		return ", ".join(zones) if zones is Array else str(zones))
	return "%s → %s" % ends


func _decks_used() -> Array:
	var decks := []
	for zone: Variant in document.get_value(["zones"], {}).values():
		if zone is Dictionary and zone.get("cards") is String and zone.cards not in decks:
			decks.append(zone.cards)
	return decks


func _fill_entries() -> void:
	%EntryList.clear()
	for pair: Array in _entries():
		%EntryList.add_item(pair[1])
		%EntryList.set_item_metadata(%EntryList.item_count - 1, pair[0])
		if pair[0] == entry:
			%EntryList.select(%EntryList.item_count - 1)
	for child in %EntryButtons.get_children():
		child.queue_free()
	if section in NAMED:
		var kind: String = NAMED[section].noun
		_add_entry_button("Add", func() -> void: _ask("New " + kind, "", _add_named))
		_add_entry_button("Rename", func() -> void: if entry: _ask("Rename %s (steps using the old name aren't changed)" % kind, entry, _rename_named))
		_add_entry_button("Remove", func() -> void: if entry: edit(func() -> void: document.remove([NAMED[section].key, entry])))
	elif section == "Moves":
		_add_entry_button("Add", func() -> void:
			var count: int = document.get_value(["moves"], []).size()
			edit(func() -> void: document.insert(["moves"], count, NEW_MOVE))
			select_entry(str(count)))
		_add_entry_button("Up", _move_rule.bind(-1))
		_add_entry_button("Down", _move_rule.bind(1))
		_add_entry_button("Remove", func() -> void: if entry: edit(func() -> void: document.remove(["moves", int(entry)])))


func _add_entry_button(text: String, pressed: Callable) -> void:
	var button := Button.new()
	button.text = text
	button.pressed.connect(pressed)
	%EntryButtons.add_child(button)


func _add_named(name: String) -> void:
	var key: String = NAMED[section].key
	if _good_name(name) and not document.has([key, name]):
		edit(func() -> void: document.set_value([key, name], NAMED[section].new))
		select_entry(name)


func _rename_named(name: String) -> void:
	var key: String = NAMED[section].key
	if _good_name(name) and not document.has([key, name]):
		var old := entry
		edit(func() -> void: document.rename_key([key, old], name))
		select_entry(name)


## Zone, trigger and condition names are used in steps, so they must be single words; buttons can say anything.
func _good_name(name: String) -> bool:
	return name.is_valid_identifier() if section != "Buttons" else not name.is_empty()


## Move rules are tried in order, so their order matters.
func _move_rule(step: int) -> void:
	var moves: Array = document.get_value(["moves"], [])
	var from := int(entry)
	var to := from + step
	if entry.is_empty() or to < 0 or to >= moves.size():
		return
	var rule: Variant = moves[from]
	edit(func() -> void:
		document.remove(["moves", from])
		document.insert(["moves"], to, rule))
	select_entry(str(to))


# --- The details panel and the middle ---------------------------------------------------------------------

func _show_details() -> void:
	for child in %Details.get_children():
		child.queue_free()
	preview.visible = section != "Deck"
	deck_table.visible = section == "Deck"
	if section in ["Table", "Deck", "Moves", "Buttons", "Triggers", "Conditions"] and entry.is_empty():
		_add_heading("Nothing here yet: press Add." if section != "Deck" else "No deck yet: in Table, pick a zone and choose its cards.")
		return
	match section:
		"Game":
			_add_form([], EditorFields.GAME)
		"Table":
			_add_heading("Zone: " + entry)
			_add_note(["zones", entry])
			_add_form(["zones", entry], EditorFields.ZONE)
		"Deck":
			var text: String = deck_texts.get(entry, FileAccess.get_file_as_string(entry))
			deck_table.show_deck(text, document.get_value(["card_face"], GameRules.DEFAULT_CARD_FACE))
			_add_heading("Card face")
			_add_form(["card_face"], EditorFields.CARD_FACE, _deck_columns())
		"Moves":
			_add_heading("Move " + _move_label(document.get_value(["moves", int(entry)])))
			_add_help("A drag is allowed when a rule matches its zones (and phase and when); the first matching rule's then steps run.")
			_add_note(["moves", int(entry)])
			_add_form(["moves", int(entry)], EditorFields.MOVE)
		"Buttons":
			_show_button()
		"Triggers":
			_add_heading("Trigger: " + entry)
			_add_help(TRIGGER_HELP.get(entry, "Runs when a step calls trigger('%s'), or when a card names it in an on_<zone> column (then card is that card)." % entry))
			_add_note(["triggers", entry])
			%Details.add_child(StepList.new(self, ["triggers", entry]))
		"Conditions":
			_add_heading("Condition: " + entry)
			_add_help("A named check: write its name in any step or condition, or use matching(cards, '%s') to keep the cards it's true for (card is each card)." % entry)
			_add_note(["conditions", entry])
			var field := smart_field(str(document.get_value(["conditions", entry], "")))
			field.committed.connect(func(text: String) -> void: change(["conditions", entry], text))
			%Details.add_child(field)
		"Variables":
			for table: Array in VARIABLE_TABLES:
				_add_heading(table[1])
				_add_help(table[2])
				_add_variable_table(table[0])


## A button written as just a list of steps gets the full form once it needs more than steps.
func _show_button() -> void:
	var path: Array = ["actions", entry]
	_add_heading("Button: " + entry)
	_add_note(path)
	if document.get_value(path) is Dictionary:
		_add_form(path, EditorFields.ACTION)
		return
	_add_help("What the button does:")
	%Details.add_child(StepList.new(self, path))
	var more := Button.new()
	more.text = "More settings (when, choose, ...)"
	more.pressed.connect(func() -> void:
		var steps: Variant = document.get_value(path)
		edit(func() -> void: document.set_value(path, {"steps": steps if steps is Array else [steps]})))
	%Details.add_child(more)


## Name and starting value of each variable; values are written as in the file (5, "draw", true, []).
func _add_variable_table(key: String) -> void:
	var grid := GridContainer.new()
	grid.columns = 3
	for name: String in document.get_value([key], {}):
		var name_box := LineEdit.new()
		name_box.text = name
		name_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_box.text_submitted.connect(func(new_name: String) -> void:
			if new_name.is_valid_identifier() and not document.has([key, new_name]):
				edit(func() -> void: document.rename_key([key, name], new_name)))
		var value_box := LineEdit.new()
		value_box.text = GameDocument.format(document.get_value([key, name]))
		value_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var send := func(_text: Variant = null) -> void: change([key, name], _value_from_text(value_box.text))
		value_box.text_submitted.connect(send)
		value_box.focus_exited.connect(send)
		var remove := Button.new()
		remove.text = "×"
		remove.flat = true
		remove.pressed.connect(func() -> void: edit(func() -> void: document.remove([key, name])))
		for control: Control in [name_box, value_box, remove]:
			grid.add_child(control)
	%Details.add_child(grid)
	var add := Button.new()
	add.text = "+ Add"
	add.pressed.connect(func() -> void: _ask("New variable", "", func(name: String) -> void:
		if name.is_valid_identifier() and not document.has([key, name]):
			edit(func() -> void: document.set_value([key, name], 0))))
	%Details.add_child(add)


## 5, true, [] and "text" as written in the file; anything that isn't valid there is taken as text.
static func _value_from_text(text: String) -> Variant:
	var value: Variant = JSON.parse_string(text)
	return text if value == null and text.strip_edges() != "null" else value


func smart_field(text: String) -> SmartField:
	var names: Array = Array(checked_game.input_names()) + checked_game.rules.conditions.keys() if checked_game else []
	return SmartField.new(text, names, _deck_columns(), _check_step)


func _check_step(text: String) -> Array:
	return StepCompiler.new(checked_game).problems_with(text) if checked_game else []


func _deck_columns() -> Array:
	var columns := []
	for deck: String in _decks_used():
		var rows := DeckLoader.rows(deck_texts.get(deck, FileAccess.get_file_as_string(deck)))
		for column in rows[0] if rows else PackedStringArray():
			if column not in columns:
				columns.append(column)
	return columns


func _add_form(path: Array, fields: Array, columns: Array = []) -> void:
	var form := EditorForm.new()
	form.show_fields(self, path, fields, columns)
	form.changed.connect(func(changed_path: Array, value: Variant) -> void:
		change(changed_path, value)
		if value == null or changed_path.back() in ["per_player", "copies", "cards", "from", "to"]:
			_show_details.call_deferred())
	%Details.add_child(form)


func _add_heading(text: String) -> void:
	var label := Label.new()
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_font_size_override("font_size", 18)
	%Details.add_child(label)


func _add_help(text: String) -> void:
	var label := Label.new()
	label.text = text
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.add_theme_color_override("font_color", EditorForm.HELP_COLOR)
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
	if path.is_empty() or path[0] not in SECTION_OF:
		return
	select_section(SECTION_OF[path[0]])
	if path.size() >= 2 and section not in ["Variables", "Deck", "Game"]:
		select_entry(str(path[1]))


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
