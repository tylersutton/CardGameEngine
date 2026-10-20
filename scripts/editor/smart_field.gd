class_name SmartField
extends VBoxContainer
## A box for one step or condition: suggests names as you type (zones, variables, conditions, building blocks,
## card columns after "card.", and inside quotes the names a building block takes there, like the actions in
## offer('...')), shows a building block's (or a condition's) arguments inside its brackets, and says what's
## wrong with the text. Building blocks, names, numbers and quoted text are colored. Sends committed(text) when
## you leave the box or press Enter.

signal committed(text: String)

const NAME_COLOR := Color("9cdcfe")

var code := CodeEdit.new()
var hint := Label.new()
var names: Array = []
var columns: Array = []
## Per building block, the names its quoted arguments take: {"offer": ["Draw", "Pass"], "trigger": [...]}.
var quoted_names: Dictionary = {}
## Conditions with parameters, as they're called: {"points": "points(cards)"}.
var signatures: Dictionary = {}
## Checks the text: returns its problems (none when it's fine).
var check: Callable
var last_sent := ""


func _init(text: String, known_names: Array, deck_columns: Array, checker: Callable, names_in_quotes := {}, condition_signatures := {}) -> void:
	names = known_names
	columns = deck_columns
	check = checker
	quoted_names = names_in_quotes
	signatures = condition_signatures
	last_sent = text
	code.text = text
	code.scroll_fit_content_height = true
	code.wrap_mode = TextEdit.LINE_WRAPPING_BOUNDARY
	code.code_completion_enabled = true
	code.auto_brace_completion_enabled = true
	code.custom_minimum_size.x = 120
	code.syntax_highlighter = _highlighter()
	code.text_changed.connect(_on_text_changed)
	code.caret_changed.connect(_show_hint)
	code.focus_exited.connect(_send)
	code.gui_input.connect(func(event: InputEvent) -> void:
		if event is InputEventKey and event.pressed and event.keycode == KEY_ENTER and not code.get_code_completion_options():
			code.accept_event()
			_send())
	code.code_completion_requested.connect(_suggest)
	hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	hint.theme_type_variation = &"DimLabel"
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
	if before and (before[-1] == "." or before[-1].is_valid_identifier() or _quoted_block(before)):
		code.request_code_completion()
	_show_hint()


func _text_before_caret() -> String:
	return code.get_line(code.get_caret_line()).substr(0, code.get_caret_column())


## Suggestions for the word being typed: card columns after "card.", otherwise names and building blocks.
func _suggest() -> void:
	var before := _text_before_caret()
	var quoted_block := _quoted_block(before)
	if quoted_block:
		var quoted := before.substr(before.rfind("'") + 1).to_lower()
		for name: String in quoted_names[quoted_block]:
			if name.to_lower().begins_with(quoted):
				code.add_code_completion_option(CodeEdit.KIND_PLAIN_TEXT, name, name)
		code.update_code_completion_options(true)
		return
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
	var block := _call_around(_text_before_caret())
	if block in StepCompiler.building_blocks:
		var help: String = BlockHelp.by_name.get(block, "")
		lines.append(StepCompiler.signature(block) + (": " + help if help else ""))
	elif block in signatures:
		lines.append(signatures[block])
	var problems: Array = check.call(code.text) if code.text.strip_edges() and check.is_valid() else []
	lines.append_array(problems)
	hint.text = "\n".join(lines)
	hint.visible = not lines.is_empty()
	hint.add_theme_color_override("font_color", EditorTheme.ERROR if problems else EditorTheme.TEXT_DIM)


## The name whose brackets the text ends inside, e.g. "draw" in "draw(deck, |", or "".
func _call_around(before: String) -> String:
	var depth := 0
	for i in range(before.length() - 1, -1, -1):
		if before[i] == ")":
			depth += 1
		elif before[i] == "(":
			if depth == 0:
				var name := RegEx.create_from_string("([A-Za-z_]\\w*)\\s*$").search(before.substr(0, i))
				return name.get_string(1) if name else ""
			depth -= 1
	return ""


## When the text ends inside quotes given to a building block that takes names there (offer('Dr|), that block.
func _quoted_block(before: String) -> String:
	if before.count("'") % 2 == 0:
		return ""
	var block := _call_around(before.substr(0, before.rfind("'")))
	return block if block in quoted_names else ""


func _highlighter() -> CodeHighlighter:
	var colors := CodeHighlighter.new()
	colors.number_color = EditorTheme.NUMBER
	colors.symbol_color = EditorTheme.TEXT
	colors.function_color = EditorTheme.FUNCTION
	colors.member_variable_color = NAME_COLOR
	colors.add_color_region("'", "'", EditorTheme.STRING)
	colors.add_color_region("\"", "\"", EditorTheme.STRING)
	for name: String in names:
		colors.add_keyword_color(name, NAME_COLOR)
	for keyword: String in StepParser.KEYWORDS:
		colors.add_keyword_color(keyword, EditorTheme.KEYWORD)
	return colors
