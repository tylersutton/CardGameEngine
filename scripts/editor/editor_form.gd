class_name EditorForm
extends VBoxContainer
## A form for one part of a game file (a zone, the card face...), built from EditorFields: one row per setting,
## its name on the left and its control on the right, with grouped settings folded away under their heading.
## Each change is sent as changed(path, value); a value of null means "take the key out" (back to its default).
## Hovering or focusing a row sends described(key, help), for the inspector's description.

signal changed(path: Array, value: Variant)
signal described(title: String, help: String)

const DECKS_FOLDER := "res://decks/"
const NOT_SET := "(default)"
## Expression settings that may also be a plain number.
const NUMBER_OR_TEXT := ["pick", "up_to"]
## Kinds whose control takes the whole width, under the setting's name.
const WIDE_KINDS := ["long_text", "json", "expression", "steps", "drag"]

## Groups opened or closed by the user, by form and heading, kept while the editor is open.
static var opened_groups: Dictionary = {}

var editor: Node
var document: GameDocument
var base_path: Array
## The deck's column names, for "column" fields.
var columns: Array = []


func show_fields(game_editor: Node, path: Array, fields: Array, deck_columns: Array = []) -> void:
	editor = game_editor
	document = editor.document
	base_path = path
	columns = deck_columns
	for child in get_children():
		child.queue_free()
	var groups := {}
	for field: Dictionary in fields:
		if field.has("group"):
			if field.group not in groups:
				groups[field.group] = []
			groups[field.group].append(field)
		else:
			add_child(_row(field))
	for group: String in groups:
		_add_group(group, groups[group])


## A heading that folds its settings away; it starts open if any of them is set.
func _add_group(group: String, fields: Array) -> void:
	var any_set := fields.any(func(field: Dictionary) -> bool: return document.has(base_path + [field.key]))
	var heading := Button.new()
	heading.theme_type_variation = &"GroupButton"
	heading.text = group.to_upper()
	heading.alignment = HORIZONTAL_ALIGNMENT_LEFT
	heading.toggle_mode = true
	var remembered := str(base_path.slice(0, 1)) + group
	heading.button_pressed = opened_groups.get(remembered, any_set)
	var rows := VBoxContainer.new()
	for field: Dictionary in fields:
		rows.add_child(_row(field))
	var show_rows := func(open: bool) -> void:
		rows.visible = open
		heading.icon = EditorIcons.icon("chevron_down" if open else "chevron_right", Color.WHITE, 0.8)
	show_rows.call(heading.button_pressed)
	heading.toggled.connect(func(open: bool) -> void:
		opened_groups[remembered] = open
		show_rows.call(open))
	add_child(heading)
	add_child(rows)


## One setting: its name (bright when it's set in the file) and its control, and a button to take it out.
func _row(field: Dictionary) -> Control:
	var path := base_path + [field.key]
	var is_set := document.has(path)
	var name_label := Label.new()
	name_label.text = field.key
	name_label.clip_text = true
	name_label.tooltip_text = field.help
	name_label.mouse_filter = Control.MOUSE_FILTER_PASS
	name_label.add_theme_color_override("font_color", EditorTheme.TEXT if is_set else EditorTheme.TEXT_DIM)
	var reset := Button.new()
	reset.theme_type_variation = &"FlatButton"
	reset.icon = EditorIcons.icon("restart", Color.WHITE, 0.75)
	reset.tooltip_text = "Take this out (back to the default)"
	reset.modulate.a = 1.0 if is_set else 0.0
	reset.disabled = not is_set
	reset.focus_mode = Control.FOCUS_NONE
	reset.pressed.connect(func() -> void: changed.emit(path, null))
	var control := _control_for(field, path, document.get_value(path))
	var row: Container
	if field.kind in WIDE_KINDS:
		row = VBoxContainer.new()
		var heading := HBoxContainer.new()
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		heading.add_child(name_label)
		heading.add_child(reset)
		row.add_child(heading)
		row.add_child(control)
	else:
		row = HBoxContainer.new()
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_label.size_flags_stretch_ratio = 0.7
		control.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(name_label)
		row.add_child(control)
		row.add_child(reset)
	row.mouse_entered.connect(func() -> void: described.emit(field.key, field.help))
	_on_focus(control, func() -> void: described.emit(field.key, field.help))
	return row


## Calls back when the control, or any box inside it, takes the keyboard focus.
static func _on_focus(control: Control, then: Callable) -> void:
	if control.focus_mode != Control.FOCUS_NONE:
		control.focus_entered.connect(then)
	for child in control.get_children():
		if child is Control:
			_on_focus(child, then)


func _control_for(field: Dictionary, path: Array, value: Variant) -> Control:
	match field.kind:
		"long_text":
			var box := TextEdit.new()
			box.text = str(value) if value != null else ""
			box.custom_minimum_size.y = 54
			box.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
			box.focus_exited.connect(func() -> void: changed.emit(path, box.text if box.text else null))
			return box
		"number", "whole":
			var spin := SpinBox.new()
			spin.allow_greater = true
			spin.allow_lesser = true
			spin.step = 1.0 if field.kind == "whole" else 0.01
			spin.value = value if value is float else 0.0
			spin.value_changed.connect(func(number: float) -> void: changed.emit(path, int(number) if field.kind == "whole" else snappedf(number, 0.001)))
			return spin
		"yes_no":
			var check := CheckBox.new()
			check.text = "yes" if value else "no"
			check.button_pressed = value == true
			check.toggled.connect(func(on: bool) -> void:
				check.text = "yes" if on else "no"
				changed.emit(path, on))
			return check
		"choice", "column", "deck":
			return _choice(path, value, _options(field))
		"spot":
			return _spot(path, value)
		"columns", "list", "names", "numbers":
			var line := LineEdit.new()
			line.text = ", ".join(value.map(_as_text)) if value is Array else _as_text(value)
			line.placeholder_text = "comma-separated" + (": " + ", ".join(columns) if field.kind == "columns" and columns else "")
			var commit := func(_text: Variant = null) -> void:
				var items := Array(line.text.split(",", false)).map(func(item: String) -> Variant:
					return item.strip_edges().to_int() if field.kind == "numbers" else item.strip_edges())
				var one_allowed: bool = field.kind == "list" and items.size() == 1
				changed.emit(path, null if items.is_empty() else (items[0] if one_allowed else items))
			line.text_submitted.connect(commit)
			line.focus_exited.connect(commit)
			return line
		"json":
			return _json_box(path, value, field.example)
		"expression":
			var smart: SmartField = editor.smart_field(_as_text(value))
			smart.committed.connect(func(text: String) -> void:
				changed.emit(path, _number_or_text(text) if field.key in NUMBER_OR_TEXT else (text if text else null)))
			return smart
		"steps":
			return StepList.new(editor, path)
		"drag":
			return _drag(path, value)
		"zones":
			var zones := LineEdit.new()
			zones.text = ", ".join(value) if value is Array else _as_text(value)
			zones.placeholder_text = "e.g. hand, or pile, discard"
			var send := func(_text: Variant = null) -> void:
				var names := Array(zones.text.split(",", false)).map(func(name: String) -> String: return name.strip_edges())
				changed.emit(path, null if names.is_empty() else (names[0] if names.size() == 1 else names))
			zones.text_submitted.connect(send)
			zones.focus_exited.connect(send)
			return zones
	var text_line := LineEdit.new()
	text_line.text = str(value) if value != null else ""
	var submit := func(_text: Variant = null) -> void: changed.emit(path, text_line.text if text_line.text else null)
	text_line.text_submitted.connect(submit)
	text_line.focus_exited.connect(submit)
	return text_line


static func _as_text(value: Variant) -> String:
	if value == null:
		return ""
	return GameDocument.one_line(value) if value is float or value is Array else str(value)


## "3" is the number 3 (for pick and up_to); anything else stays text. Empty takes the key out.
static func _number_or_text(text: String) -> Variant:
	if text.is_empty():
		return null
	return text.to_float() if text.is_valid_float() else text


func _options(field: Dictionary) -> Array:
	match field.kind:
		"column":
			return columns
		"deck":
			return Array(DirAccess.get_files_at(DECKS_FOLDER)).filter(func(file: String) -> bool: return file.get_extension() == "csv") \
				.map(func(file: String) -> String: return DECKS_FOLDER + file)
	return field.options


func _choice(path: Array, value: Variant, options: Array) -> OptionButton:
	var picker := OptionButton.new()
	picker.fit_to_longest_item = false
	picker.clip_text = true
	picker.add_item(NOT_SET)
	for option: String in options + ([str(value)] if value != null and value not in options else []):
		# Decks are listed by file name.
		picker.add_item(option.get_file() if option.begins_with("res://") else option)
		picker.set_item_metadata(picker.item_count - 1, option)
		picker.set_item_tooltip(picker.item_count - 1, option)
		if option == str(value):
			picker.select(picker.item_count - 1)
	picker.item_selected.connect(func(index: int) -> void:
		changed.emit(path, null if index == 0 else picker.get_item_metadata(index)))
	return picker


## Two numbers from 0 to 1: across and down the table.
func _spot(path: Array, value: Variant) -> HBoxContainer:
	var row := HBoxContainer.new()
	var spins: Array[SpinBox] = []
	for i in 2:
		var spin := SpinBox.new()
		spin.min_value = -1.0
		spin.max_value = 2.0
		spin.step = 0.005
		spin.prefix = ["x", "y"][i]
		spin.tooltip_text = ["across, from 0 (left) to 1 (right)", "down, from 0 (top) to 1 (bottom)"][i]
		spin.value = value[i] if value is Array and value.size() == 2 else 0.5
		spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		spins.append(spin)
		row.add_child(spin)
	for spin in spins:
		spin.value_changed.connect(func(_number: float) -> void:
			changed.emit(path, [snappedf(spins[0].value, 0.001), snappedf(spins[1].value, 0.001)]))
	return row


## A drag's "from" and "to": zone names (comma-separated for several), each left empty for any zone. Both empty
## takes the drag out.
func _drag(path: Array, value: Variant) -> GridContainer:
	var grid := GridContainer.new()
	grid.columns = 2
	var ends := {}
	for end in ["from", "to"]:
		var label := Label.new()
		label.text = end
		grid.add_child(label)
		var zones := LineEdit.new()
		var current: Variant = value.get(end) if value is Dictionary else null
		zones.text = ", ".join(current) if current is Array else _as_text(current)
		zones.placeholder_text = "any zone" if end == "from" else "e.g. pile, or tableau, foundation"
		zones.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		grid.add_child(zones)
		ends[end] = zones
	var send := func(_text: Variant = null) -> void:
		var drag := {}
		for end: String in ends:
			var names := Array(ends[end].text.split(",", false)).map(func(name: String) -> String: return name.strip_edges())
			if names:
				drag[end] = names[0] if names.size() == 1 else names
		if drag != (value if value is Dictionary else {}):
			changed.emit(path, drag if drag else null)
	for zones: LineEdit in ends.values():
		zones.text_submitted.connect(send)
		zones.focus_exited.connect(send)
	return grid


## Anything else, as JSONC text; only sent when it reads correctly.
func _json_box(path: Array, value: Variant, example: String) -> TextEdit:
	var box := TextEdit.new()
	box.text = GameDocument.one_line(value) if value != null else ""
	box.scroll_fit_content_height = true
	box.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	box.add_theme_font_override("font", EditorTheme.code_font)
	box.placeholder_text = "e.g. " + example
	box.focus_exited.connect(func() -> void:
		if box.text.strip_edges().is_empty():
			changed.emit(path, null)
			return
		var parsed: Variant = JSON.parse_string(GameRules.plain_json(box.text))
		box.add_theme_color_override("font_color", EditorTheme.ERROR if parsed == null else EditorTheme.TEXT)
		if parsed != null:
			changed.emit(path, parsed))
	return box
