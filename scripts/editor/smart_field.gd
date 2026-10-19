class_name SmartField
extends VBoxContainer
## A box for one step or condition: suggests names as you type (zones, variables, conditions, building blocks,
## and card columns after "card."), shows a building block's arguments inside its brackets, and says what's
## wrong with the text. Sends committed(text) when you leave the box or press Enter.

signal committed(text: String)

const HINT_COLOR := Color(1, 1, 1, 0.6)
const PROBLEM_COLOR := Color(1, 0.5, 0.45)

var code := CodeEdit.new()
var hint := Label.new()
var names: Array = []
var columns: Array = []
## Checks the text: returns its problems (none when it's fine).
var check: Callable
var last_sent := ""


func _init(text: String, known_names: Array, deck_columns: Array, checker: Callable) -> void:
	names = known_names
	columns = deck_columns
	check = checker
	last_sent = text
	code.text = text
	code.scroll_fit_content_height = true
	code.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	code.code_completion_enabled = true
	code.auto_brace_completion_enabled = true
	code.custom_minimum_size.x = 160
	code.text_changed.connect(_on_text_changed)
	code.caret_changed.connect(_show_hint)
	code.focus_exited.connect(_send)
	code.gui_input.connect(func(event: InputEvent) -> void:
		if event is InputEventKey and event.pressed and event.keycode == KEY_ENTER and not code.get_code_completion_options():
			code.accept_event()
			_send())
	code.code_completion_requested.connect(_suggest)
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	hint.add_theme_font_size_override("font_size", 12)
	hint.visible = false
	add_child(code)
	add_child(hint)
	_show_hint()


func _send() -> void:
	var text := code.text.strip_edges()
	if text != last_sent:
		last_sent = text
		committed.emit(text)


func _on_text_changed() -> void:
	var before := _text_before_caret()
	if before and (before[-1] == "." or before[-1].is_valid_identifier()):
		code.request_code_completion()
	_show_hint()


func _text_before_caret() -> String:
	return code.get_line(code.get_caret_line()).substr(0, code.get_caret_column())


## Suggestions for the word being typed: card columns after "card.", otherwise names and building blocks.
func _suggest() -> void:
	var before := _text_before_caret()
	var word := RegEx.create_from_string("[A-Za-z_]\\w*$").search(before)
	var typed := word.get_string() if word else ""
	var after_card := before.trim_suffix(typed).ends_with("card.")
	if after_card:
		for column: String in columns:
			if column.to_lower().begins_with(typed.to_lower()):
				code.add_code_completion_option(CodeEdit.KIND_MEMBER, column, column)
	elif not before.trim_suffix(typed).ends_with("."):
		for name: String in names + StepParser.KEYWORDS:
			if name.begins_with(typed):
				code.add_code_completion_option(CodeEdit.KIND_VARIABLE, name, name)
		for block: String in StepCompiler.building_blocks:
			if block.begins_with(typed):
				code.add_code_completion_option(CodeEdit.KIND_FUNCTION, StepCompiler.signature(block), block + "(")
	code.update_code_completion_options(true)


## Under the box: the arguments of the building block the caret is inside, then anything wrong with the text.
func _show_hint() -> void:
	var lines: Array[String] = []
	var block := _block_around_caret()
	if block:
		lines.append(StepCompiler.signature(block))
	var problems: Array = check.call(code.text) if code.text.strip_edges() and check.is_valid() else []
	lines.append_array(problems)
	hint.text = "\n".join(lines)
	hint.visible = not lines.is_empty()
	hint.add_theme_color_override("font_color", PROBLEM_COLOR if problems else HINT_COLOR)


## The building block whose brackets the caret is in, e.g. "draw" in "draw(deck, |".
func _block_around_caret() -> String:
	var before := _text_before_caret()
	var depth := 0
	for i in range(before.length() - 1, -1, -1):
		if before[i] == ")":
			depth += 1
		elif before[i] == "(":
			if depth == 0:
				var name := RegEx.create_from_string("([A-Za-z_]\\w*)\\s*$").search(before.substr(0, i))
				return name.get_string(1) if name and name.get_string(1) in StepCompiler.building_blocks else ""
			depth -= 1
	return ""
