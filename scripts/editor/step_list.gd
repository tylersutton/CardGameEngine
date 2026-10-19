class_name StepList
extends VBoxContainer
## A list of steps in the editor: plain steps, and "if", "for each" and "repeat while" blocks holding their own
## steps (folded away with ▾). Steps can be added, moved and removed, or the whole list edited as text.
## Edits go straight to the editor, one step at a time, so comments elsewhere in the list survive.

const INDENT := 24
const ADD_CHOICES := ["Step", "If", "For each", "Repeat while"]
const NEW_STEPS := ["", {"if": "", "then": []}, {"for": "item", "in": "", "do": []}, {"while": "", "do": []}]

var editor: Node
var path: Array


func _init(game_editor: Node, list_path: Array) -> void:
	editor = game_editor
	path = list_path
	var value: Variant = editor.document.get_value(path)
	var steps: Array = value if value is Array else ([value] if value != null else [])
	for i in steps.size():
		add_child(_row(steps[i], _step_path(value, i), i, steps.size()))
	var footer := HBoxContainer.new()
	var add := MenuButton.new()
	add.text = "+ Add"
	add.flat = false
	for choice: String in ADD_CHOICES:
		add.get_popup().add_item(choice)
	add.get_popup().index_pressed.connect(func(index: int) -> void: _add(NEW_STEPS[index]))
	footer.add_child(add)
	var as_text := Button.new()
	as_text.text = "Edit as text"
	as_text.pressed.connect(_edit_as_text)
	footer.add_child(as_text)
	add_child(footer)


## A list written as a single step ("then": "end_turn()") has just that step, at the list's own path.
func _step_path(value: Variant, index: int) -> Array:
	return path if value is String else path + [index]


func _row(step: Variant, step_path: Array, index: int, count: int) -> Control:
	if step is String:
		return _line([], _field(step, step_path), step_path, index, count)
	var block := VBoxContainer.new()
	var inner := VBoxContainer.new()
	var parts: Array[Control] = []
	if step.has("for"):
		var item_name := LineEdit.new()
		item_name.text = step.for
		item_name.custom_minimum_size.x = 70
		item_name.text_submitted.connect(func(text: String) -> void: editor.change(step_path + ["for"], text))
		item_name.focus_exited.connect(func() -> void:
			if item_name.text != step.for:
				editor.change(step_path + ["for"], item_name.text))
		parts = [_word("for each"), item_name, _word("in")]
		block.add_child(_line(parts, _field(step.in, step_path + ["in"]), step_path, index, count))
		inner.add_child(_nested(step_path + ["do"]))
	elif step.has("while"):
		block.add_child(_line([_word("repeat while")], _field(step.while, step_path + ["while"]), step_path, index, count))
		inner.add_child(_nested(step_path + ["do"]))
	else:
		block.add_child(_line([_word("if")], _field(step.get("if", ""), step_path + ["if"]), step_path, index, count))
		inner.add_child(_nested(step_path + ["then"]))
		if step.has("else"):
			inner.add_child(_word("else"))
			inner.add_child(_nested(step_path + ["else"]))
		else:
			var add_else := Button.new()
			add_else.text = "+ else"
			add_else.flat = true
			add_else.pressed.connect(func() -> void: editor.edit(func() -> void: editor.document.set_value(step_path + ["else"], [])))
			inner.add_child(add_else)
	var fold := Button.new()
	fold.text = "▾"
	fold.flat = true
	fold.pressed.connect(func() -> void:
		inner.visible = not inner.visible
		fold.text = "▾" if inner.visible else "▸")
	block.get_child(0).add_child(fold)
	block.get_child(0).move_child(fold, 0)
	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", INDENT)
	margin.add_child(inner)
	block.add_child(margin)
	return block


func _nested(list_path: Array) -> StepList:
	return StepList.new(editor, list_path)


func _field(text: String, field_path: Array) -> SmartField:
	var field: SmartField = editor.smart_field(text)
	field.committed.connect(func(new_text: String) -> void: editor.change(field_path, new_text))
	field.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return field


## One line: its words, its text box, and a ⋮ menu to move or remove the step.
func _line(parts: Array, field: Control, step_path: Array, index: int, count: int) -> HBoxContainer:
	var line := HBoxContainer.new()
	for part: Control in parts:
		line.add_child(part)
	line.add_child(field)
	var menu := MenuButton.new()
	menu.text = "⋮"
	var popup := menu.get_popup()
	popup.add_item("Move up")
	popup.add_item("Move down")
	popup.add_item("Remove")
	popup.set_item_disabled(0, index == 0)
	popup.set_item_disabled(1, index == count - 1)
	popup.index_pressed.connect(func(choice: int) -> void:
		if choice == 2:
			editor.edit(func() -> void: editor.document.remove(step_path))
		else:
			_move(index, index - 1 if choice == 0 else index + 1))
	line.add_child(menu)
	return line


func _word(text: String) -> Label:
	var label := Label.new()
	label.text = text
	return label


## The list is read inside edit, after any text still being typed has been sent.
func _add(step: Variant) -> void:
	editor.edit(func() -> void:
		var value: Variant = editor.document.get_value(path)
		if value is String:
			editor.document.set_value(path, [value, step])
		else:
			editor.document.insert(path, value.size() if value is Array else 0, step))


func _move(from: int, to: int) -> void:
	editor.edit(func() -> void:
		var step: Variant = editor.document.get_value(path + [from])
		editor.document.remove(path + [from])
		editor.document.insert(path, to, step))


## The whole list as JSONC text, for when a form is more trouble than typing.
func _edit_as_text() -> void:
	var dialog := ConfirmationDialog.new()
	dialog.title = "Steps as text"
	var box := TextEdit.new()
	box.custom_minimum_size = Vector2(640, 360)
	box.text = GameDocument.format(editor.document.get_value(path, []), "")
	dialog.add_child(box)
	dialog.confirmed.connect(func() -> void:
		var steps: Variant = JSON.parse_string(GameRules.plain_json(box.text))
		if steps is Array or steps is String:
			editor.edit(func() -> void: editor.document.set_value(path, steps)))
	editor.add_child(dialog)
	dialog.popup_centered()
