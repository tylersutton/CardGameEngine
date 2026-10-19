class_name EditorForm
extends VBoxContainer
## A form for one part of a game file (a zone, the card face...), built from EditorFields. Each change is sent as
## changed(path, value); a value of null means "take the key out" (back to its default).

signal changed(path: Array, value: Variant)

const DECKS_FOLDER := "res://decks/"
const HELP_COLOR := Color(1, 1, 1, 0.55)
const BAD_COLOR := Color(1, 0.45, 0.4)
const NOT_SET := "(not set)"
## Expression settings that may also be a plain number.
const NUMBER_OR_TEXT := ["pick", "up_to"]

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
	for field: Dictionary in fields:
		_add_row(field)


func _add_row(field: Dictionary) -> void:
	var path := base_path + [field.key]
	var heading := HBoxContainer.new()
	var name_label := Label.new()
	name_label.text = field.key
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_child(name_label)
	if document.has(path):
		var clear := Button.new()
		clear.text = "×"
		clear.flat = true
		clear.tooltip_text = "Take this out (back to the default)"
		clear.pressed.connect(func() -> void: changed.emit(path, null))
		heading.add_child(clear)
	add_child(heading)
	add_child(_control_for(field, path, document.get_value(path)))
	var help := Label.new()
	help.text = field.help
	help.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	help.add_theme_color_override("font_color", HELP_COLOR)
	help.add_theme_font_size_override("font_size", 12)
	add_child(help)


func _control_for(field: Dictionary, path: Array, value: Variant) -> Control:
	match field.kind:
		"long_text":
			var box := TextEdit.new()
			box.text = str(value) if value != null else ""
			box.custom_minimum_size.y = 70
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
			check.toggled.connect(func(on: bool) -> void: changed.emit(path, on))
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
	picker.add_item(NOT_SET)
	for option: String in options:
		picker.add_item(option)
		if option == value:
			picker.select(picker.item_count - 1)
	if value != null and value not in options:
		picker.add_item(str(value))
		picker.select(picker.item_count - 1)
	picker.item_selected.connect(func(index: int) -> void:
		changed.emit(path, null if index == 0 else picker.get_item_text(index)))
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
		spin.prefix = ["across", "down"][i]
		spin.value = value[i] if value is Array and value.size() == 2 else 0.5
		spin.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		spins.append(spin)
		row.add_child(spin)
	for spin in spins:
		spin.value_changed.connect(func(_number: float) -> void:
			changed.emit(path, [snappedf(spins[0].value, 0.001), snappedf(spins[1].value, 0.001)]))
	return row


## Anything else, as JSONC text; only sent when it reads correctly.
func _json_box(path: Array, value: Variant, example: String) -> TextEdit:
	var box := TextEdit.new()
	box.text = GameDocument.format(value) if value != null else ""
	box.custom_minimum_size.y = 60
	box.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	box.placeholder_text = "e.g. " + example
	box.focus_exited.connect(func() -> void:
		if box.text.strip_edges().is_empty():
			changed.emit(path, null)
			return
		var parsed: Variant = JSON.parse_string(GameRules.plain_json(box.text))
		box.add_theme_color_override("font_color", BAD_COLOR if parsed == null else Color.WHITE)
		if parsed != null:
			changed.emit(path, parsed))
	return box
