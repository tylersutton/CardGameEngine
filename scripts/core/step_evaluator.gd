class_name StepEvaluator
extends RefCounted
## Works out a step tree from StepParser against the game: names are looked up as they're met, building blocks
## are called on an Actions, and "and"/"or" stop as soon as the answer is known. Problems (a column the card
## doesn't have, text compared with a number) set failure instead of stopping the game. One per evaluation.

## How many numbers each math function takes; min and max take two or more.
const MATH_ARGUMENTS := {"abs": 1, "floor": 1, "ceil": 1, "round": 1, "int": 1, "min": 2, "max": 2}
const ZONE_CLASS := "Zone"

var game: Game
var runner: StepRunner
var card: Card
var actions: Actions
var failure := ""
## After a failure, or once a choose(...) is waiting for its answer: the rest of the step isn't worked out.
var stopped := false


func _init(game_to_read: Game, card_of_step: Card) -> void:
	game = game_to_read
	runner = game.runner
	card = card_of_step


func evaluate(node: Dictionary) -> Variant:
	if stopped:
		return null
	# The most common kinds first: match tries them in order.
	match node.kind:
		"name":
			return _name(node.name)
		"value":
			return node.value
		"call":
			return _call(node.name, node.args)
		"binary":
			return _binary(node)
		"list":
			return node.items.map(evaluate)
		"member":
			return _member(evaluate(node.target), node.name)
		"index":
			return _index(evaluate(node.target), evaluate(node.index))
		"unary":
			return _unary(node.op, evaluate(node.term))
		"choose":
			var condition: Variant = evaluate(node.condition)
			return evaluate(node.if_true if is_true(condition) else node.if_false)
		"assign":
			return _assign(node)
		"unreadable":
			return _fail(node.error)
	return null


static func is_true(value: Variant) -> bool:
	return true if value else false


func _name(name: String) -> Variant:
	if name == Game.CARD_VARIABLE:
		return card
	if name in game.rules.conditions:
		return evaluate(game.tree_of(game.rules.conditions[name]))
	var value: Variant = game.value_of(name)
	if value == null and name in game.local_names and not runner.has_local(name):
		return _fail("\"%s\" has no value here: it's remembered only in the list of steps that sets it (with = or a loop)" % name)
	return value


func _assign(node: Dictionary) -> Variant:
	var value: Variant = evaluate(node.value)
	if node.op != "=":
		value = operate(node.op.left(1), _name(node.name), value)
	if not stopped:
		game.assign(node.name, value)
	return null


func _binary(node: Dictionary) -> Variant:
	var left: Variant = evaluate(node.left)
	if node.op == "and" and not is_true(left) or node.op == "or" and is_true(left):
		return node.op == "or"
	if node.op in ["and", "or"]:
		return is_true(evaluate(node.right))
	return operate(node.op, left, evaluate(node.right))


func _unary(op: String, value: Variant) -> Variant:
	if stopped:
		return null
	if op == "not":
		return not is_true(value)
	if not _is_number(value):
		return _fail("can't make %s negative" % describe(value))
	return -value


## The operators, with their meanings checked first: numbers with numbers (whole numbers stay whole, so 7 / 2
## is 3), text joined with + and compared, lists joined with +, and 'text %d' % [values] filled in.
func operate(op: String, left: Variant, right: Variant) -> Variant:
	if stopped:
		return null
	if op in ["==", "!="]:
		if not _comparable(left, right):
			return _fail("can't compare %s with %s%s" % [describe(left), describe(right), _quote_hint(left, right)])
		return (left == right) == (op == "==")
	if op == "%" and left is String:
		return _format(left, right)
	var both_numbers := _is_number(left) and _is_number(right)
	var both_text := left is String and right is String
	match op:
		"+":
			if both_numbers or both_text or left is Array and right is Array:
				return left + right
		"-", "*":
			if both_numbers:
				return left - right if op == "-" else left * right
		"/", "%":
			if both_numbers and right == 0:
				return _fail("can't divide by zero")
			if both_numbers:
				return _divide(op, left, right)
		"<", ">", "<=", ">=":
			if both_numbers or both_text:
				return _compare(op, left, right)
	var hint := _quote_hint(left, right) if op in StepParser.COMPARISONS else ""
	return _fail("can't work out %s %s %s%s" % [describe(left), op, describe(right), hint])


static func _divide(op: String, left: Variant, right: Variant) -> Variant:
	if op == "/":
		return left / right
	return left % right if left is int and right is int else fmod(left, right)


static func _compare(op: String, left: Variant, right: Variant) -> bool:
	match op:
		"<":
			return left < right
		">":
			return left > right
		"<=":
			return left <= right
	return left >= right


## Things of different kinds can't be equal, so comparing them is a mistake worth reporting (card.rank == 10
## when rank is text), except with null ("nothing yet") and between numbers.
static func _comparable(left: Variant, right: Variant) -> bool:
	return left == null or right == null or typeof(left) == typeof(right) or _is_number(left) and _is_number(right)


static func _quote_hint(left: Variant, right: Variant) -> String:
	if left is String and _is_number(right) or right is String and _is_number(left):
		return " (to compare with text, put the number in quotes: '10')"
	return ""


## 'text' % values fills in %s and %d. Godot reports a mismatch (too few values) instead of stopping.
func _format(template: String, values: Variant) -> Variant:
	var expression := Expression.new()
	expression.parse("template % values", ["template", "values"])
	var result: Variant = expression.execute([template, values])
	if expression.has_execute_failed():
		return _fail("can't fill in '%s' with %s" % [template, describe(values)])
	return result


func _member(target: Variant, name: String) -> Variant:
	if stopped:
		return null
	if target == null:
		return _fail("can't read .%s of nothing (if the card might be missing, text() and number() give '' and 0)" % name)
	if target is Dictionary:
		if name in target:
			return target[name]
		return _fail("no column \"%s\" in this card's deck" % name)
	if target is Card and target.get(name) == null:
		return _fail("no column \"%s\" in this card's deck" % name)
	if target is Object and name in target:
		return target.get(name)
	return _fail("%s has no \"%s\"" % [describe(target), name])


func _index(target: Variant, index: Variant) -> Variant:
	if stopped:
		return null
	if target is Array and _is_number(index):
		var position := int(index)
		if position >= -target.size() and position < target.size():
			return target[position]
		return _fail("position %d is outside the list (it has %d items, from position 0)" % [position, target.size()])
	if target is Dictionary and index in target:
		return target[index]
	return _fail("can't read position %s of %s" % [Game.format_value(index), describe(target)])


func _call(name: String, arg_nodes: Array) -> Variant:
	var args := []
	for arg_node: Dictionary in arg_nodes:
		args.append(evaluate(arg_node))
	if stopped:
		return null
	if name in MATH_ARGUMENTS:
		return _math(name, args)
	if name not in StepCompiler.building_blocks:
		return _fail("unknown building block \"%s\"" % name)
	var count_problem := StepCompiler.argument_count_problem(name, args.size())
	if count_problem:
		return _fail(count_problem)
	var method: Dictionary = StepCompiler.building_blocks[name]
	for i in args.size():
		var parameter: Dictionary = method.args[i]
		if not _fits(args[i], parameter):
			return _fail("%s: \"%s\" should be %s, not %s (%s)" % [name, parameter.name, _parameter_kind(parameter), describe(args[i]), StepCompiler.signature(name)])
		if parameter.type == TYPE_INT:
			args[i] = int(args[i])
		elif parameter.type == TYPE_FLOAT:
			args[i] = float(args[i])
		elif parameter.type == TYPE_BOOL:
			args[i] = is_true(args[i])
	if actions == null:
		actions = Actions.new(game, card)
	var result: Variant = actions.callv(name, args)
	stopped = not runner.wait_request.is_empty()
	return result


## Whether the value is the kind of thing the building block's parameter takes (building blocks take zones and
## cards as objects). A card parameter may be null (no card), since building blocks like number(card, ...) allow
## for it; a zone parameter may not.
static func _fits(value: Variant, parameter: Dictionary) -> bool:
	match parameter.type:
		TYPE_INT, TYPE_FLOAT:
			return _is_number(value)
		TYPE_STRING:
			return value is String
		TYPE_ARRAY:
			return value is Array
		TYPE_OBJECT:
			if parameter.class_name == ZONE_CLASS:
				return value is Zone
			return value == null or value is Card
	return true


func _math(name: String, args: Array) -> Variant:
	if name == "int" and args.size() == 1 and args[0] is bool:
		return int(args[0])
	if not args.all(_is_number):
		return _fail("%s needs numbers, not %s" % [name, ", ".join(args.map(describe))])
	match name:
		"abs":
			return abs(args[0])
		"floor":
			return floor(args[0])
		"ceil":
			return ceil(args[0])
		"round":
			return round(args[0])
		"int":
			return int(args[0])
		"min":
			return args.min()
	return args.max()


static func _parameter_kind(parameter: Dictionary) -> String:
	match parameter.type:
		TYPE_INT, TYPE_FLOAT:
			return "a number"
		TYPE_STRING:
			return "text in quotes"
		TYPE_ARRAY:
			return "a list"
		TYPE_OBJECT:
			return "a zone" if parameter.class_name == ZONE_CLASS else "a card"
	return "something else"


## How a value is named in error messages: "the text 'A'", "the number 10", "the zone hand_1", ...
static func describe(value: Variant) -> String:
	if value == null:
		return "nothing"
	if value is bool:
		return "true" if value else "false"
	if value is String:
		return "the text '%s'" % value
	if _is_number(value):
		return "the number %s" % Game.format_value(value)
	if value is Card:
		return "the card %s" % value.title
	if value is Zone:
		return "the zone %s" % value.name
	if value is Array:
		return "a list of %d" % value.size()
	return str(value)


static func _is_number(value: Variant) -> bool:
	return value is int or value is float


func _fail(message: String) -> Variant:
	stopped = true
	if failure.is_empty():
		failure = message
	return null
