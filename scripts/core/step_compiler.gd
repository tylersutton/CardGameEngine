class_name StepCompiler
extends RefCounted
## Checks a game file when it loads and compiles every step and condition into an Expression, so mistakes
## (unknown names, wrong argument counts, misspelled keys) show up before anything runs. Used once per load.

const CONDITIONAL_KEYS := ["if", "then", "else"]
const LOOP_KEYS := ["for", "in", "do"]
const WHILE_KEYS := ["while", "do"]
const KEYWORDS := ["true", "false", "null", "and", "or", "not"]
## Godot's Expression reads "x if c else y" as just x, so it's refused with a pointer to either().
const TERNARY_WORDS := ["if", "else"]
const MATH_FUNCTIONS := ["abs", "min", "max", "floor", "ceil", "round", "int"]
const VIEWER_BLOCK := "viewer("
const ZONE_KEYS := ["label", "labels", "outline", "layout", "face", "anchor", "anchors", "width", "rotation", "rotations", "cards",
	"on_click", "on_double_click", "spread", "drag", "per_player", "copies", "grid", "group", "covered_by", "columns"]
const CARD_FACE_KEYS := ["corner", "title", "body", "text", "color", "background", "zoom"]
const TYPE_DESCRIPTIONS := {TYPE_STRING: "text", TYPE_ARRAY: "a list", TYPE_FLOAT: "a number", TYPE_BOOL: "true or false"}

static var building_blocks := _building_blocks()
static var failed_call := RegEx.create_from_string(r"On call to '(\w+)'")
static var unknown_name := RegEx.create_from_string(r"Invalid named index '(\w+)'")
static var string_literal := RegEx.create_from_string("'[^']*'|\"[^\"]*\"")
static var name_in_code := RegEx.create_from_string(r"(?<![.\w])([A-Za-z_]\w*)\s*(\()?")

var game: Game
var rules: GameRules
var errors: Array[String]


func _init(game_to_check: Game) -> void:
	game = game_to_check
	rules = game.rules
	errors = game.errors


## Before any zone is made: misspelled keys, and settings that would stop the zones being built at all.
func check_zone_definitions() -> void:
	for key in rules.unknown_keys:
		errors.append("Unknown key \"%s\" at the top of the game file" % key)
	for key in rules.card_face:
		if key not in CARD_FACE_KEYS:
			errors.append("card_face: unknown key \"%s\" (it has %s)" % [key, ", ".join(CARD_FACE_KEYS)])
	for zone_kind in rules.zones:
		var definition: Variant = rules.zones[zone_kind]
		if definition is not Dictionary:
			errors.append("Zone \"%s\" should be an object" % zone_kind)
			continue
		for key in definition:
			if key not in ZONE_KEYS:
				errors.append("Zone \"%s\": unknown key \"%s\"" % [zone_kind, key])
		var columns: Variant = definition.get("columns", {})
		if columns is not Dictionary or columns.values().any(func(formula: Variant) -> bool: return formula is not String):
			errors.append("Zone \"%s\": \"columns\" should be column names with expressions, e.g. { \"points\": \"min(card.value, 10)\" }" % zone_kind)
		elif columns and not definition.has("cards"):
			errors.append("Zone \"%s\": \"columns\" works out columns for its \"cards\", so it needs \"cards\"" % zone_kind)
		var face: Variant = definition.get("face", Zone.FACE_UP)
		if face not in Zone.FACES and face not in Zone.PRIVATE_ALIASES:
			errors.append("Zone \"%s\": \"face\" should be %s" % [zone_kind, ", ".join(Zone.FACES)])
		var per_player: bool = definition.get("per_player", false)
		if per_player and definition.has("copies"):
			errors.append("Zone \"%s\": use \"per_player\" or \"copies\", not both" % zone_kind)
		var copies: int = rules.players if per_player else int(definition.get("copies", 0))
		var grid: Variant = definition.get("grid")
		if grid != null and not (grid is Dictionary and grid.get("columns") is float and grid.get("spacing") is Array and grid.spacing.size() == 2):
			errors.append("Zone \"%s\": \"grid\" should look like { \"columns\": 5, \"spacing\": [0.09, 0.19] }" % zone_kind)
		var covering: Variant = definition.get("covered_by")
		if covering != null and not (covering is Array and covering.size() == copies and covering.all(func(each: Variant) -> bool: return each is Array)):
			errors.append("Zone \"%s\": \"covered_by\" needs one list of copy numbers per copy (%d)" % [zone_kind, copies])
		if definition.has("group") and copies == 0:
			errors.append("Zone \"%s\": \"group\" needs \"copies\" or \"per_player\"" % zone_kind)
		if copies and definition.has("anchors") and definition.anchors.size() < copies:
			errors.append("Zone \"%s\" needs %d anchors in \"anchors\" (one per %s)" % [zone_kind, copies, "player" if per_player else "copy"])


## After the zones and variables exist: everything else, then every step is compiled.
func check_and_compile() -> void:
	_check_teams()
	_check_names_are_unique()
	_check_actions_and_moves()
	_check_melds()
	if errors.is_empty():
		_compile_all_steps()


## A failed step's error, with the building block's parameters when it was given the wrong kind of argument.
static func failure_message(expression: Expression) -> String:
	var message := expression.get_error_text()
	var unknown := unknown_name.search(message)
	if unknown:
		message += " (no column \"%s\" in this card's deck)" % unknown.get_string(1)
	var call := failed_call.search(message)
	if call and call.get_string(1) in building_blocks:
		message += " wrong kind of argument, expected " + signature(call.get_string(1))
	return message


static func signature(building_block: String) -> String:
	var method: Dictionary = building_blocks[building_block]
	var parameters: Array = method.args.map(func(argument: Dictionary) -> String: return argument.name)
	return "%s(%s)" % [building_block, ", ".join(parameters)]


func _check_teams() -> void:
	if rules.team_variables and rules.teams.is_empty():
		errors.append("\"team_variables\" needs \"teams\", e.g. [[1, 3], [2, 4]]")
	for player in range(1, rules.players + 1):
		var team_count := rules.teams.filter(func(team: Variant) -> bool: return team is Array and team.has(float(player))).size()
		if rules.teams and team_count != 1:
			errors.append("teams: player %d should be on exactly one team" % player)


func _check_names_are_unique() -> void:
	var seen := {}
	for name in game.input_names():
		if name in seen:
			errors.append("\"%s\" is used as more than one zone or variable name" % name)
		seen[name] = true
	for name in rules.conditions:
		if name in seen or name in building_blocks:
			errors.append("Condition \"%s\" has the same name as a zone, variable or building block" % name)
		elif rules.conditions[name] is not String:
			errors.append("Condition \"%s\" should be an expression in text" % name)


func _check_actions_and_moves() -> void:
	for action_name in rules.actions:
		_check_entry("Action \"%s\"" % action_name, rules.actions[action_name], {"when": TYPE_STRING, "choose": TYPE_STRING, "pick": [TYPE_FLOAT, TYPE_STRING], "up_to": [TYPE_FLOAT, TYPE_STRING], "allow": TYPE_STRING, "human_only": TYPE_BOOL, "button": TYPE_BOOL, "prefer": TYPE_STRING, "steps": TYPE_ARRAY, "phase": [TYPE_STRING, TYPE_ARRAY]})
		var action: Variant = rules.actions[action_name]
		for key in ["pick", "up_to", "allow"]:
			if action is Dictionary and action.has(key) and not action.has("choose"):
				errors.append("Action \"%s\": \"%s\" needs \"choose\"" % [action_name, key])
	for rule in rules.moves:
		var label := "Move %s" % JSON.stringify(rule)
		_check_entry(label, rule, {"from": [TYPE_STRING, TYPE_ARRAY], "to": [TYPE_STRING, TYPE_ARRAY], "when": TYPE_STRING, "then": TYPE_ARRAY, "phase": [TYPE_STRING, TYPE_ARRAY]})
		for end in ["from", "to"]:
			var zone_names: Variant = rule.get(end) if rule is Dictionary else null
			for zone_name: Variant in zone_names if zone_names is Array else [zone_names]:
				if zone_name is String and zone_name not in game.zones and zone_name not in game.aliases() and zone_name not in game.zone_groups:
					errors.append("%s: unknown zone \"%s\"" % [label, zone_name])
	var uses_phase := (rules.actions.values() + rules.moves).any(func(entry: Variant) -> bool: return entry is Dictionary and entry.has(Game.PHASE_KEY))
	if uses_phase and Game.PHASE_KEY not in game.variables:
		errors.append("\"phase\" on a move or action needs a variable named phase, e.g. \"variables\": { \"phase\": \"draw\" }")
	if rules.seats.size() not in [0, 1, rules.players]:
		errors.append("\"seats\" needs one spot per player (%d), or just seat 1's to place the rest round the table" % rules.players)
	if rules.player_names and rules.player_names.size() < rules.players:
		errors.append("\"player_names\" needs one name per player (%d)" % rules.players)
	if rules.bot:
		_check_entry("bot", rules.bot, {"players": TYPE_ARRAY, "prefer": TYPE_STRING, "peek": TYPE_BOOL, "samples": TYPE_FLOAT})
	for name in rules.effects:
		if rules.triggers[name] != rules.effects[name]:
			errors.append("\"%s\" is both an effect and a trigger" % name)
	for name in rules.triggers:
		if rules.triggers[name] is not Array:
			errors.append("Trigger \"%s\" should be a list of steps" % name)
	for name in rules.show_variables:
		if name not in game.variables:
			errors.append("show_variables: unknown variable \"%s\"" % name)
	for label in rules.status:
		if rules.status[label] is not String:
			errors.append("Status \"%s\" should be an expression in text" % label)


func _check_melds() -> void:
	if rules.melds.is_empty():
		return
	_check_entry("melds", rules.melds, {"types": TYPE_ARRAY, "points": TYPE_STRING})
	for type in rules.melds.get("types", []):
		var label := "Meld type %s" % JSON.stringify(type)
		_check_entry(label, type, {"same": TYPE_STRING, "sequence": TYPE_STRING, "min": TYPE_FLOAT})
		if type is Dictionary and not type.has("same"):
			errors.append("%s needs \"same\"" % label)


func _check_entry(label: String, entry: Variant, field_types: Dictionary) -> void:
	if entry is not Dictionary:
		errors.append("%s should be an object" % label)
		return
	for key in entry:
		if key not in field_types:
			errors.append("%s: unknown key \"%s\"" % [label, key])
			continue
		var allowed: Array = field_types[key] if field_types[key] is Array else [field_types[key]]
		if typeof(entry[key]) not in allowed:
			var descriptions := allowed.map(func(type: int) -> String: return TYPE_DESCRIPTIONS[type])
			errors.append("%s: \"%s\" should be %s" % [label, key, " or ".join(descriptions)])


func _compile_all_steps() -> void:
	var step_lists: Array = rules.triggers.values()
	var conditions: Array = []
	for action: Dictionary in rules.actions.values():
		step_lists.append(action.get("steps", []))
		conditions.append(action.get("when", ""))
		conditions.append(action.get("choose", ""))
		for key in ["pick", "up_to", "allow", "prefer"]:
			if action.get(key) is String:
				conditions.append(action[key])
	for rule: Dictionary in rules.moves:
		step_lists.append(rule.get("then", []))
		conditions.append(rule.get("when", ""))
	conditions.append_array(rules.status.values())
	conditions.append_array(rules.conditions.values())
	for zone: Variant in rules.zones.values():
		conditions.append_array(zone.get("columns", {}).values())
	conditions.append(rules.bot.get("prefer", ""))
	for zone: Zone in game.zones.values():
		step_lists.append(zone.on_click)
		step_lists.append(zone.on_double_click)
		for card in zone.cards:
			for destination: Zone in game.zones.values():
				step_lists.append(game.card_effect(card, destination.kind))
	var unique_steps := {}
	for steps: Array in step_lists:
		_collect_steps(steps, unique_steps)
	for condition: String in conditions:
		if condition:
			unique_steps[condition] = true
	for step: String in unique_steps:
		_compile(step)
		if step not in rules.status.values() and step not in rules.conditions.values() and VIEWER_BLOCK in _with_conditions(step):
			errors.append("Step \"%s\": viewer() only works in \"status\" (each screen has its own, so rules can't depend on it)" % step)


func _collect_steps(steps: Array, into: Dictionary) -> void:
	for step in steps:
		if step is String:
			into[step] = true
		elif _is_valid_conditional(step):
			into[step["if"]] = true
			_collect_steps(step.get("then", []), into)
			_collect_steps(step.get("else", []), into)
		elif _is_valid_loop(step):
			_add_loop_name(step.for)
			into[step.in] = true
			_collect_steps(step.get("do", []), into)
		elif _is_valid_while(step):
			into[step.while] = true
			_collect_steps(step.get("do", []), into)
		else:
			errors.append("Invalid step %s: use a string, { \"if\": ..., \"then\": [...], \"else\": [...] }, { \"for\": \"name\", \"in\": ..., \"do\": [...] }, or { \"while\": ..., \"do\": [...] }" % JSON.stringify(step))


func _add_loop_name(name: String) -> void:
	var names := [name, name + Game.LOOP_NUMBER_SUFFIX]
	if name == Game.PLAYER_VARIABLE:
		names.pop_front()
		if Game.PLAYER_VARIABLE not in game.variables:
			errors.append("Looping over \"player\" needs \"players\" of 2 or more")
	for loop_name in names:
		if loop_name in game.loop_names:
			continue
		if loop_name in game.input_names() or loop_name in rules.conditions:
			errors.append("Loop name \"%s\" is already used by a zone, variable or condition" % loop_name)
		else:
			game.loop_names.append(loop_name)


func _is_valid_conditional(step: Variant) -> bool:
	return step is Dictionary \
		and step.get("if") is String \
		and step.keys().all(func(key: String) -> bool: return key in CONDITIONAL_KEYS) \
		and step.get("then", []) is Array \
		and step.get("else", []) is Array


func _is_valid_loop(step: Variant) -> bool:
	return step is Dictionary \
		and step.get("for") is String and step.for.is_valid_identifier() \
		and step.get("in") is String \
		and step.keys().all(func(key: String) -> bool: return key in LOOP_KEYS) \
		and step.get("do", []) is Array


func _is_valid_while(step: Variant) -> bool:
	return step is Dictionary \
		and step.get("while") is String \
		and step.keys().all(func(key: String) -> bool: return key in WHILE_KEYS) \
		and step.get("do", []) is Array


func _compile(step: String) -> void:
	var input_names := game.input_names()
	var code := _with_conditions(step)
	if code.is_empty():
		return
	var expression := Expression.new()
	if expression.parse(code, input_names) != OK:
		errors.append("Could not parse step \"%s\": %s" % [step, expression.get_error_text()])
		return
	var problems := _problems_in(code, input_names)
	if problems:
		errors.append("Step \"%s\": %s" % [step, ", ".join(problems)])
		return
	game.expressions[step] = expression
	var tree: Variant = _logic_tree(code)
	if tree is Dictionary:
		game.logic[step] = tree
		_compile_logic_parts(tree, input_names)


func _compile_logic_parts(node: Variant, input_names: PackedStringArray) -> void:
	if node is Dictionary:
		for term: Variant in node.get("terms", [node.get("term")]):
			_compile_logic_parts(term, input_names)
	elif node not in game.expressions:
		var expression := Expression.new()
		expression.parse(node, input_names)
		game.expressions[node] = expression


## Splits an expression on its outermost "or", then "and", then "not", into a tree evaluated left to right
## (Game.evaluate), because Expression always evaluates both sides of "and"/"or".
func _logic_tree(code: String) -> Variant:
	code = code.strip_edges()
	for keyword in ["or", "and"]:
		var parts := _split_top_level(code, keyword)
		if parts.size() > 1:
			return {op = keyword, terms = parts.map(_logic_tree)}
	if _is_word_at(code, 0, "not"):
		return {op = "not", term = _logic_tree(code.substr(3))}
	if code.begins_with("(") and _top_level_end(code, 0) == code.length() - 1:
		var inner: Variant = _logic_tree(code.substr(1, code.length() - 2))
		if inner is Dictionary:
			return inner
	return code


static func _split_top_level(code: String, keyword: String) -> Array[String]:
	var parts: Array[String] = []
	var start := 0
	var i := 0
	while i < code.length():
		var next := _top_level_end(code, i)
		if next != i:
			i = next + 1
		elif _is_word_at(code, i, keyword):
			parts.append(code.substr(start, i - start))
			i += keyword.length()
			start = i
		else:
			i += 1
	parts.append(code.substr(start))
	return parts


## If code[i] opens a bracket or quote, the index of its closing partner; otherwise i.
static func _top_level_end(code: String, i: int) -> int:
	var opening := code[i]
	if opening in "'\"":
		var closing := code.find(opening, i + 1)
		return closing if closing != -1 else code.length() - 1
	if opening not in "([{":
		return i
	var depth := 0
	var j := i
	while j < code.length():
		if code[j] in "'\"":
			j = _top_level_end(code, j)
		elif code[j] in "([{":
			depth += 1
		elif code[j] in ")]}":
			depth -= 1
			if depth == 0:
				return j
		j += 1
	return code.length() - 1


static func _is_word_at(code: String, i: int, word: String) -> bool:
	var end := i + word.length()
	return code.substr(i, word.length()) == word \
		and (i == 0 or not _is_word_character(code[i - 1])) \
		and (end >= code.length() or not _is_word_character(code[end]))


static func _is_word_character(character: String) -> bool:
	return character == "_" or character.is_valid_int() or character.to_lower() != character.to_upper()


## Replaces each condition name with its expression in brackets, including conditions inside conditions.
func _with_conditions(step: String) -> String:
	var code := step
	for depth in Game.MAX_NESTING:
		var expanded := _expand_conditions_once(code)
		if expanded == code:
			return code
		code = expanded
	errors.append("Step \"%s\": its conditions refer to each other in a circle" % step)
	return ""


func _expand_conditions_once(code: String) -> String:
	var literals := string_literal.search_all(code)
	var expanded := ""
	var copied_up_to := 0
	for found in name_in_code.search_all(code):
		var name := found.get_string(1)
		var inside_literal := literals.any(func(literal: RegExMatch) -> bool:
			return found.get_start(1) >= literal.get_start() and found.get_start(1) < literal.get_end())
		if name not in rules.conditions or found.get_string(2) != "" or inside_literal:
			continue
		expanded += code.substr(copied_up_to, found.get_start(1) - copied_up_to) + "(%s)" % rules.conditions[name]
		copied_up_to = found.get_end(1)
	return expanded + code.substr(copied_up_to)


func _problems_in(step: String, input_names: PackedStringArray) -> Array[String]:
	var problems: Array[String] = []
	var code := string_literal.sub(step, "''", true)
	for found in name_in_code.search_all(code):
		var name := found.get_string(1)
		var is_call := found.get_string(2) != ""
		if name in KEYWORDS or (is_call and name in MATH_FUNCTIONS):
			continue
		if name in TERNARY_WORDS:
			if "either" not in problems.reduce(func(text: String, problem: String) -> String: return text + problem, ""):
				problems.append("\"x if condition else y\" doesn't work in steps: use either(condition, x, y)")
			continue
		if is_call and name not in building_blocks:
			problems.append("unknown building block \"%s\"" % name)
		elif is_call:
			var problem := _argument_count_problem(name, _argument_count(code, found.get_end()))
			if problem:
				problems.append(problem)
		elif name not in input_names:
			var problem := "unknown trigger \"%s\" (a card's on_ column names a trigger)" if step == name else "unknown zone or variable \"%s\""
			problems.append(problem % name)
	return problems


static func _argument_count_problem(building_block: String, given: int) -> String:
	var method: Dictionary = building_blocks[building_block]
	var most: int = method.args.size()
	var fewest: int = most - method.default_args.size()
	if given >= fewest and given <= most:
		return ""
	var expected := str(most) if fewest == most else "%d to %d" % [fewest, most]
	var noun := "argument" if expected == "1" else "arguments"
	return "%s takes %s %s, got %d (expected %s)" % [building_block, expected, noun, given, signature(building_block)]


static func _argument_count(code: String, after_open_paren: int) -> int:
	var depth := 0
	var commas := 0
	for i in range(after_open_paren, code.length()):
		var character := code[i]
		if character in "([{":
			depth += 1
		elif character in ")]}" and depth > 0:
			depth -= 1
		elif character == ")":
			var has_arguments := not code.substr(after_open_paren, i - after_open_paren).strip_edges().is_empty()
			return commas + 1 if has_arguments else 0
		elif character == "," and depth == 0:
			commas += 1
	return commas + 1


static func _building_blocks() -> Dictionary:
	var actions_script: Script = Actions
	var blocks := {}
	for method: Dictionary in actions_script.get_script_method_list():
		if not method.name.begins_with("_") and not method.flags & METHOD_FLAG_STATIC:
			blocks[method.name] = method
	return blocks
