class_name StepParser
extends RefCounted
## Turns a step's text into a tree of plain dictionaries for StepEvaluator, e.g. "score += 2" becomes
## {kind = "assign", name = "score", op = "+=", value = {kind = "value", value = 2}}.
##
## Node kinds: value (a number, text, true, false or null), list, name, call, member (card.suit), index (list[0]),
## unary (- and not), binary (+, ==, and, ...), choose (x if condition else y) and assign (=, += and -=, only as
## a whole step).

const KEYWORDS := ["and", "or", "not", "if", "else", "true", "false", "null"]
const LITERALS := {"true": true, "false": false, "null": null}
const COMPARISONS := ["==", "!=", "<", ">", "<=", ">="]
const ASSIGNMENTS := ["=", "+=", "-="]
## Longest first, so "==" isn't read as "=" twice.
const SYMBOLS := ["==", "!=", "<=", ">=", "+=", "-=", "<", ">", "=", "+", "-", "*", "/", "%", "(", ")", "[", "]", ",", "."]
const END := ""

var tokens: Array[Dictionary] = []
var position := 0
var error := ""


## {tree = ...} or {error = "..."}.
static func parse(text: String) -> Dictionary:
	var parser := StepParser.new()
	return parser._parse(text)


func _parse(text: String) -> Dictionary:
	_read_tokens(text)
	if error:
		return {error = error}
	var tree := _assignment_or_expression()
	if not error and _peek() != END:
		_fail("unexpected %s" % _describe(_peek()))
	return {error = error} if error else {tree = tree}


func _read_tokens(text: String) -> void:
	var i := 0
	while i < text.length():
		var character := text[i]
		if character in " \t\r\n":
			i += 1
		elif _is_digit(character):
			var start := i
			while i < text.length() and (_is_digit(text[i]) or text[i] == "." and i + 1 < text.length() and _is_digit(text[i + 1])):
				i += 1
			var digits := text.substr(start, i - start)
			tokens.append({kind = "value", value = digits.to_float() if "." in digits else digits.to_int()})
		elif _is_name_start(character):
			var start := i
			while i < text.length() and (_is_name_start(text[i]) or _is_digit(text[i])):
				i += 1
			var word := text.substr(start, i - start)
			if word in LITERALS:
				tokens.append({kind = "value", value = LITERALS[word]})
			else:
				tokens.append({kind = "keyword" if word in KEYWORDS else "name", text = word})
		elif character == "'":
			i = _read_text(text, i)
			if error:
				return
		elif character == "\"":
			error = "text goes in single quotes, e.g. 'discard'"
			return
		elif character == "!" and text.substr(i, 2) != "!=":
			error = "use not instead of !, e.g. not covered(card)"
			return
		else:
			var symbol := _symbol_at(text, i)
			if symbol.is_empty():
				error = "unexpected \"%s\"" % character
				return
			tokens.append({kind = "symbol", text = symbol})
			i += symbol.length()


## Reads 'text' (a \ before a quote or backslash keeps it, as in 'King\'s Court') and returns where it ends.
func _read_text(text: String, start: int) -> int:
	var value := ""
	var i := start + 1
	while i < text.length() and text[i] != "'":
		if text[i] == "\\" and i + 1 < text.length():
			i += 1
		value += text[i]
		i += 1
	if i >= text.length():
		error = "the text starting %s has no closing '" % text.substr(start, 12)
		return i
	tokens.append({kind = "value", value = value})
	return i + 1


static func _symbol_at(text: String, i: int) -> String:
	for symbol: String in SYMBOLS:
		if text.substr(i, symbol.length()) == symbol:
			return symbol
	return ""


## A whole step may change a variable: score = 0, score += 2, passes -= 1.
func _assignment_or_expression() -> Dictionary:
	var target := _expression()
	if error or _peek() not in ASSIGNMENTS:
		return target
	var op: String = _take()
	if target.kind != "name":
		_fail("only a variable can be changed with %s" % op)
		return {}
	return {kind = "assign", name = target.name, op = op, value = _expression()}


## Lowest precedence first: x if condition else y, or, and, not, comparisons, + -, * / %, unary -, then calls,
## .members, [indexes] and single values.
func _expression() -> Dictionary:
	var result := _or()
	if error or _peek() != "if":
		return result
	_take()
	var condition := _or()
	if not _expect("else", "after the condition of \"x if condition else y\""):
		return {}
	return {kind = "choose", condition = condition, if_true = result, if_false = _expression()}


func _or() -> Dictionary:
	var result := _and()
	while not error and _peek() == "or":
		_take()
		result = {kind = "binary", op = "or", left = result, right = _and()}
	return result


func _and() -> Dictionary:
	var result := _not()
	while not error and _peek() == "and":
		_take()
		result = {kind = "binary", op = "and", left = result, right = _not()}
	return result


func _not() -> Dictionary:
	if _peek() == "not":
		_take()
		return {kind = "unary", op = "not", term = _not()}
	return _comparison()


func _comparison() -> Dictionary:
	var result := _sum()
	while not error and _peek() in COMPARISONS:
		var op: String = _take()
		result = {kind = "binary", op = op, left = result, right = _sum()}
	return result


func _sum() -> Dictionary:
	var result := _product()
	while not error and _peek() in ["+", "-"]:
		var op: String = _take()
		result = {kind = "binary", op = op, left = result, right = _product()}
	return result


func _product() -> Dictionary:
	var result := _negation()
	while not error and _peek() in ["*", "/", "%"]:
		var op: String = _take()
		result = {kind = "binary", op = op, left = result, right = _negation()}
	return result


func _negation() -> Dictionary:
	if _peek() == "-":
		_take()
		return {kind = "unary", op = "-", term = _negation()}
	return _postfix()


func _postfix() -> Dictionary:
	var result := _single()
	while not error:
		if _peek() == "(":
			if result.kind != "name":
				_fail("only a building block can be called with ( )")
				return {}
			_take()
			result = {kind = "call", name = result.name, args = _items(")")}
		elif _peek() == ".":
			_take()
			var member: Dictionary = tokens[position] if position < tokens.size() else {}
			if member.get("kind") != "name" and member.get("kind") != "keyword":
				_fail("expected a column name after \".\"")
				return {}
			position += 1
			result = {kind = "member", target = result, name = member.text}
		elif _peek() == "[":
			_take()
			var index := _expression()
			_expect("]", "after the position in [ ]")
			result = {kind = "index", target = result, index = index}
		else:
			return result
	return {}


func _single() -> Dictionary:
	if position >= tokens.size():
		_fail("the step ends too soon")
		return {}
	var token: Dictionary = tokens[position]
	position += 1
	match token.kind:
		"value":
			return {kind = "value", value = token.value}
		"name":
			return {kind = "name", name = token.text}
		"symbol" when token.text == "(":
			var inner := _expression()
			_expect(")", "to close the (")
			return inner
		"symbol" when token.text == "[":
			return {kind = "list", items = _items("]")}
	position -= 1
	_fail("unexpected %s" % _describe(_peek()))
	return {}


## Comma-separated expressions up to the closing bracket, which is taken too.
func _items(closing: String) -> Array:
	var items := []
	if _peek() == closing:
		_take()
		return items
	while not error:
		items.append(_expression())
		if _peek() == ",":
			_take()
		elif _expect(closing, "or a comma between items"):
			break
	return items


## The next token's text ("+", "and", ...), the kind "value"/"name" for those, or END.
func _peek() -> String:
	if position >= tokens.size():
		return END
	var token: Dictionary = tokens[position]
	return token.text if token.has("text") and token.kind != "name" else token.kind


func _take() -> String:
	var taken := _peek()
	position += 1
	return taken


func _expect(wanted: String, context: String) -> bool:
	if error:
		return false
	if _peek() == wanted:
		_take()
		return true
	_fail("expected \"%s\" %s, but found %s" % [wanted, context, _describe(_peek())])
	return false


func _describe(token: String) -> String:
	if token == END:
		return "the end of the step"
	if token in ["value", "name"]:
		var found: Dictionary = tokens[position]
		return "\"%s\"" % (found.text if token == "name" else Game.format_value(found.value))
	return "\"%s\"" % token


func _fail(message: String) -> void:
	if error.is_empty():
		error = message


static func _is_digit(character: String) -> bool:
	return character >= "0" and character <= "9"


static func _is_name_start(character: String) -> bool:
	return character == "_" or (character >= "a" and character <= "z") or (character >= "A" and character <= "Z")
