class_name GameFileView
extends CodeEdit
## The Game file tab: the game file's own text, colored, with line numbers and folding. Typing here sends
## edited(text) after a short pause; the editor then updates the forms and the table from it, and shows changes
## made in the forms back here.

signal edited(text: String)

const EDIT_DELAY := 0.4
const KEY_COLOR := Color("9cdcfe")

var pause := Timer.new()
var error_line := -1


func _ready() -> void:
	gutters_draw_line_numbers = true
	gutters_draw_fold_gutter = true
	line_folding = true
	highlight_current_line = true
	highlight_all_occurrences = true
	auto_brace_completion_enabled = true
	indent_automatic = true
	minimap_draw = true
	minimap_width = 70
	draw_tabs = false
	caret_blink = true
	clear_comment_delimiters()
	clear_string_delimiters()
	add_comment_delimiter("//", "", true)
	add_comment_delimiter("/*", "*/")
	add_string_delimiter("\"", "\"")
	syntax_highlighter = _highlighter()
	add_theme_stylebox_override("normal", EditorTheme.box(EditorTheme.BACKGROUND, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(4, 4)))
	add_theme_stylebox_override("focus", EditorTheme.box(EditorTheme.BACKGROUND, Color.TRANSPARENT, [0, 0, 0, 0], 0, Vector2(4, 4)))
	pause.one_shot = true
	pause.timeout.connect(func() -> void: edited.emit(text))
	add_child(pause)
	text_changed.connect(pause.start.bind(EDIT_DELAY))


## Shows the text, keeping the caret and scroll where they were (unless it's the same text already).
func show_text(new_text: String) -> void:
	if text == new_text:
		return
	var line := get_caret_line()
	var column := get_caret_column()
	var scrolled := scroll_vertical
	text = new_text
	clear_undo_history()
	set_caret_line(mini(line, get_line_count() - 1))
	set_caret_column(column)
	scroll_vertical = scrolled
	pause.stop()


## Puts the caret at a place in the text (a character offset) and scrolls it into view.
func go_to(offset: int) -> void:
	var line := text.substr(0, offset).count("\n")
	set_caret_line(line)
	set_caret_column(get_line(line).length() - get_line(line).strip_edges(true, false).length())
	center_viewport_to_caret()


## Marks the line the file can't be read at (0-based), or none with -1.
func show_error_line(line: int) -> void:
	if error_line >= 0 and error_line < get_line_count():
		set_line_background_color(error_line, Color.TRANSPARENT)
	error_line = line
	if line >= 0 and line < get_line_count():
		set_line_background_color(line, Color(EditorTheme.ERROR, 0.22))


func _highlighter() -> CodeHighlighter:
	var colors := CodeHighlighter.new()
	colors.number_color = EditorTheme.NUMBER
	colors.symbol_color = EditorTheme.TEXT
	colors.function_color = EditorTheme.TEXT
	colors.member_variable_color = EditorTheme.TEXT
	colors.add_color_region("\"", "\"", EditorTheme.STRING)
	colors.add_color_region("//", "", EditorTheme.COMMENT, true)
	colors.add_color_region("/*", "*/", EditorTheme.COMMENT)
	for keyword in ["true", "false", "null"]:
		colors.add_keyword_color(keyword, EditorTheme.KEYWORD)
	return colors
