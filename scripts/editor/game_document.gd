class_name GameDocument
extends RefCounted
## A game file being edited. It keeps the file's own text and changes only the part being edited, so comments,
## key order and formatting everywhere else stay exactly as written. Paths name a value: ["zones", "deck", "anchor"],
## ["turn", 0].

const INDENT := "\t"

var path: String
var text := ""
## The file as data (null if it isn't valid JSONC; then parse_error says why).
var data: Variant
var parse_error := ""
## Where each value sits in the text: path (as JSON) -> {start, end, member_start, key_start, key_end}.
var spans: Dictionary = {}
var _at := 0


static func open(file_path: String) -> GameDocument:
	var document := GameDocument.new()
	document.path = file_path
	document.set_text(FileAccess.get_file_as_string(file_path))
	return document


func save() -> Error:
	var file := FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return FileAccess.get_open_error()
	file.store_string(text)
	return OK


func set_text(new_text: String) -> void:
	text = new_text
	var json := JSON.new()
	if json.parse(GameRules.plain_json(text)) == OK:
		data = json.data
		parse_error = ""
		_index()
	else:
		data = null
		parse_error = "line %d: %s" % [json.get_error_line() + 1, json.get_error_message()]
		spans.clear()


func has(value_path: Array) -> bool:
	return _key(value_path) in spans


func get_value(value_path: Array, default: Variant = null) -> Variant:
	var value: Variant = data
	for step: Variant in value_path:
		if value is Dictionary and step in value:
			value = value[step]
		elif value is Array and step is int and step < value.size():
			value = value[step]
		else:
			return default
	return value


## Sets a value, adding it (and any missing objects above it) if it isn't there yet.
func set_value(value_path: Array, value: Variant) -> void:
	# Text that isn't valid JSONC has no values to find, so there's nowhere to put this one.
	if data == null:
		return
	if has(value_path):
		if one_line(get_value(value_path)) != one_line(value):
			var span: Dictionary = spans[_key(value_path)]
			_replace(span.start, span.end, format(value, _indent_at(span.start)))
		return
	var parent := value_path.slice(0, -1)
	var key: Variant = value_path.back()
	if not has(parent):
		set_value(parent, {key: value} if key is String else [value])
		return
	if get_value(parent) is Array:
		insert(parent, get_value(parent).size(), value)
	else:
		_add_member(parent, "%s: %s" % [JSON.stringify(key), format(value, _indent_at(spans[_key(parent)].start) + INDENT)])


## Inserts into a list at a position (the end when index is the list's size).
func insert(list_path: Array, index: int, value: Variant) -> void:
	var list: Array = get_value(list_path, [])
	if not has(list_path):
		set_value(list_path, [value])
	elif index < list.size():
		var before: Dictionary = spans[_key(list_path + [index])]
		var line_start := _line_start(before.member_start)
		var on_own_line := text.substr(line_start, before.member_start - line_start).strip_edges().is_empty()
		var indent := _indent_at(before.member_start)
		var item := format(value, indent)
		if on_own_line:
			_replace(line_start, line_start, indent + item + ",\n")
		else:
			_replace(before.member_start, before.member_start, item + ", ")
	else:
		_add_member(list_path, format(value, _indent_at(spans[_key(list_path)].start) + INDENT))


func remove(value_path: Array) -> void:
	if not has(value_path) or value_path.is_empty():
		return
	var span: Dictionary = spans[_key(value_path)]
	var from: int = span.member_start
	var to: int = span.end
	var after := _skip_spaces(to)
	var comma_after := after < text.length() and text[after] == ","
	var before := _previous_meaningful(from)
	var comma_before := -1 if comma_after or before < 0 or text[before] != "," else before
	if comma_after:
		to = after + 1
	# A member alone on its lines takes its lines (and the note above it) with it; if it was the last one,
	# the comma that came before it goes too.
	var line_start := _line_start(span.member_start)
	var line_end := _line_end(to)
	var rest_of_line := text.substr(to, line_end - to).strip_edges()
	if text.substr(line_start, span.member_start - line_start).strip_edges().is_empty() and (rest_of_line.is_empty() or rest_of_line.begins_with("//")):
		_replace(_note_start(line_start), mini(line_end + 1, text.length()), "")
		if comma_before >= 0:
			_replace(comma_before, comma_before + 1, "")
	else:
		_replace(comma_before if comma_before >= 0 else from, to, "")
	_close_up_if_empty(value_path.slice(0, -1))


## An object or list left with nothing in it (not even a comment) is written {} or [].
func _close_up_if_empty(container_path: Array) -> void:
	var span: Dictionary = spans[_key(container_path)]
	var inside := text.substr(span.start + 1, span.end - span.start - 2)
	if inside and inside.strip_edges().is_empty():
		_replace(span.start + 1, span.end - 1, "")


func rename_key(value_path: Array, new_key: String) -> void:
	var span: Dictionary = spans.get(_key(value_path), {})
	if span.get("key_start", -1) >= 0 and not has(value_path.slice(0, -1) + [new_key]):
		_replace(span.key_start, span.key_end, JSON.stringify(new_key))


## The comment lines just above an entry, as plain text (one line each).
func note(value_path: Array) -> String:
	if not has(value_path):
		return ""
	var line_start := _line_start(spans[_key(value_path)].member_start)
	var lines := text.substr(_note_start(line_start), line_start - _note_start(line_start)).split("\n", false)
	return "\n".join(Array(lines).map(func(line: String) -> String: return line.strip_edges().trim_prefix("//").strip_edges()))


func set_note(value_path: Array, note_text: String) -> void:
	if not has(value_path):
		return
	var line_start := _line_start(spans[_key(value_path)].member_start)
	var indent := _indent_at(line_start)
	var lines := Array(note_text.strip_edges().split("\n", false)).map(func(line: String) -> String: return indent + "// " + line.strip_edges() + "\n")
	_replace(_note_start(line_start), line_start, "".join(lines))


## A value written the way the game files are (VS Code's Format Document): one entry per line.
static func format(value: Variant, indent: String = "") -> String:
	if not (value is Dictionary or value is Array) or value.is_empty():
		return one_line(value)
	var inner := indent + INDENT
	var items: Array = value.keys().map(func(key: Variant) -> String: return inner + JSON.stringify(key) + ": " + format(value[key], inner)) \
		if value is Dictionary else value.map(func(item: Variant) -> String: return inner + format(item, inner))
	var brackets := ["{", "}"] if value is Dictionary else ["[", "]"]
	return brackets[0] + "\n" + ",\n".join(items) + "\n" + indent + brackets[1]


## A value on one line, for a text box.
static func one_line(value: Variant) -> String:
	if value is Dictionary:
		return "{ %s }" % ", ".join(value.keys().map(func(key: Variant) -> String: return JSON.stringify(key) + ": " + one_line(value[key]))) if value else "{}"
	if value is Array:
		return "[%s]" % ", ".join(value.map(one_line))
	if value is float and value == floorf(value):
		return str(int(value))
	return JSON.stringify(value)


func _replace(from: int, to: int, new_text: String) -> void:
	set_text(text.substr(0, from) + new_text + text.substr(to))


## Adds a member or item at the end of an object or list, matching its style (one line, or one entry per line);
## an empty one gets one entry per line.
func _add_member(container_path: Array, member: String) -> void:
	var span: Dictionary = spans[_key(container_path)]
	var close: int = span.end - 1
	var container: Variant = get_value(container_path)
	var children: Array = (container.keys() if container is Dictionary else range(container.size())) \
		.map(func(child: Variant) -> String: return _key(container_path + [child]))
	if children.is_empty():
		var empty_inside := text.substr(span.start + 1, close - span.start - 1)
		var indent := _indent_at(span.start)
		_replace(span.start + 1, close, empty_inside.strip_edges(false, true) + "\n" + indent + INDENT + member.strip_edges() + "\n" + indent)
		return
	var last_end: int = children.map(func(key: String) -> int: return spans[key].end).max()
	var has_comma := _skip_spaces(last_end) < text.length() and text[_skip_spaces(last_end)] == ","
	var comma_end := _skip_spaces(last_end) + 1 if has_comma else last_end
	var first_member: int = children.map(func(key: String) -> int: return spans[key].member_start).min()
	# One member per line only when the first member starts its own line; a one-line object can hold a multi-line value.
	if text.substr(span.start, first_member - span.start).contains("\n"):
		var last_member: int = children.map(func(key: String) -> int: return spans[key].member_start).max()
		var line_end := _line_end(comma_end)
		_replace(last_end, line_end, "," + text.substr(comma_end, line_end - comma_end) + "\n" + _indent_at(last_member) + member.strip_edges())
	else:
		_replace(comma_end, comma_end, ("" if has_comma else ",") + " " + member.strip_edges())


func _index() -> void:
	spans.clear()
	_at = _skip_spaces(0)
	if _at < text.length():
		_scan([], -1, -1, -1)


func _scan(value_path: Array, member_start: int, key_start: int, key_end: int) -> void:
	var start := _at
	var opening := text[_at]
	if opening == "{" or opening == "[":
		_at += 1
		var index := 0
		while true:
			_at = _skip_spaces(_at)
			if _at >= text.length() or text[_at] in "}]":
				break
			var item_start := _at
			var key: Variant = index
			var item_key_end := -1
			if opening == "{":
				key = _scan_string()
				item_key_end = _at
				_at = _skip_spaces(_at) + 1
				_at = _skip_spaces(_at)
			_scan(value_path + [key], item_start, item_start if opening == "{" else -1, item_key_end)
			index += 1
			_at = _skip_spaces(_at)
			if _at < text.length() and text[_at] == ",":
				_at += 1
		_at += 1
	elif opening == "\"":
		_scan_string()
	else:
		while _at < text.length() and text[_at] not in ",]} \t\r\n/":
			_at += 1
	spans[_key(value_path)] = {start = start, end = _at, member_start = member_start if member_start >= 0 else start,
		key_start = key_start, key_end = key_end}


func _scan_string() -> String:
	var start := _at
	_at += 1
	while _at < text.length() and text[_at] != "\"":
		_at += 2 if text[_at] == "\\" else 1
	_at += 1
	return JSON.parse_string(text.substr(start, _at - start))


## Past spaces, line breaks and comments.
func _skip_spaces(from: int) -> int:
	var i := from
	while i < text.length():
		if text[i] in " \t\r\n":
			i += 1
		elif text.substr(i, 2) == "//":
			i = _line_end(i)
		elif text.substr(i, 2) == "/*":
			var close := text.find("*/", i + 2)
			i = text.length() if close == -1 else close + 2
		else:
			break
	return i


## The last character before here that isn't a space, a line break or part of a comment (-1 if none).
func _previous_meaningful(before: int) -> int:
	var i := before - 1
	while i >= 0:
		var line_start := _line_start(i)
		var comment := _comment_start(line_start)
		if comment != -1 and i >= comment:
			i = comment - 1
		elif text[i] in " \t\r\n":
			i -= 1
		else:
			return i
	return -1


## Where a "//" comment starts on the line (outside quoted text), or -1.
func _comment_start(line_start: int) -> int:
	var in_text := false
	var i := line_start
	while i < text.length() and text[i] != "\n":
		if in_text and text[i] == "\\":
			i += 2
			continue
		if text[i] == "\"":
			in_text = not in_text
		elif not in_text and text.substr(i, 2) == "//":
			return i
		i += 1
	return -1


## The start of the comment lines directly above a line (the line itself if there are none).
func _note_start(line_start: int) -> int:
	var start := line_start
	while start > 0:
		var previous := _line_start(start - 1)
		if not text.substr(previous, start - previous).strip_edges().begins_with("//"):
			break
		start = previous
	return start


func _line_start(at: int) -> int:
	return text.rfind("\n", at - 1) + 1 if at > 0 else 0


func _line_end(at: int) -> int:
	var end := text.find("\n", at)
	return text.length() if end == -1 else end


func _indent_at(at: int) -> String:
	var line_start := _line_start(at)
	var indent := ""
	while line_start + indent.length() < text.length() and text[line_start + indent.length()] in " \t":
		indent += text[line_start + indent.length()]
	return indent


static func _key(value_path: Array) -> String:
	return JSON.stringify(value_path)
