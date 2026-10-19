class_name StepCompiler
extends RefCounted
## Checks a game file when it loads and parses every step and condition (StepParser), so mistakes (unknown names,
## wrong argument counts, misspelled keys) show up before anything runs. Used once per load.

const CONDITIONAL_KEYS := ["if", "then", "else"]
const LOOP_KEYS := ["for", "in", "do"]
const WHILE_KEYS := ["while", "do"]
const VIEWER_BLOCK := "viewer"
const CHOOSE_BLOCKS := ["choose", "choose_exactly", "choose_up_to"]
## Where a text is used (see _collect_steps), strictest first.
const EXPRESSION := 0
const STEP_CONDITION := 1
const STEP := 2
const ZONE_KEYS := ["label", "labels", "outline", "layout", "face", "anchor", "anchors", "width", "rotation", "rotations", "cards",
	"on_click", "on_double_click", "spread", "drag", "per_player", "copies", "grid", "group", "covered_by", "columns"]
const CARD_FACE_KEYS := ["corner", "title", "body", "text", "color", "background", "zoom"]
const TYPE_DESCRIPTIONS := {TYPE_STRING: "text", TYPE_ARRAY: "a list", TYPE_FLOAT: "a number", TYPE_BOOL: "true or false"}

static var building_blocks := _building_blocks()

var game: Game
var rules: GameRules
var errors: Array[String]
## Every name a step may use (zones, variables, loop names, conditions...), for quick lookups while checking.
var known_names := {}
## Names set with = that aren't game variables (see _learn_remembered_names).
var remembered_names: Array[String] = []


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


## Problems with one step or condition on its own, e.g. for the editor as it's typed.
func problems_with(step: String) -> Array[String]:
	_learn_names()
	var parsed := StepParser.parse(step)
	if parsed.has("error"):
		return [parsed.error]
	var problems: Array[String] = []
	_find_problems(parsed.tree, step, STEP, problems)
	return problems


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
		if action is Dictionary and action.has("pick") and action.has("up_to"):
			errors.append("Action \"%s\": use \"pick\" (exactly that many) or \"up_to\" (any number up to it), not both" % action_name)
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
	var expressions: Array = []
	for action: Dictionary in rules.actions.values():
		step_lists.append(action.get("steps", []))
		expressions.append(action.get("when", ""))
		expressions.append(action.get("choose", ""))
		for key in ["pick", "up_to", "allow", "prefer"]:
			if action.get(key) is String:
				expressions.append(action[key])
	for rule: Dictionary in rules.moves:
		step_lists.append(rule.get("then", []))
		expressions.append(rule.get("when", ""))
	expressions.append_array(rules.status.values())
	expressions.append_array(rules.conditions.values())
	for zone: Variant in rules.zones.values():
		expressions.append_array(zone.get("columns", {}).values())
	expressions.append(rules.bot.get("prefer", ""))
	for zone: Zone in game.zones.values():
		step_lists.append(zone.on_click)
		step_lists.append(zone.on_double_click)
		for card in zone.cards:
			for destination: Zone in game.zones.values():
				step_lists.append(game.card_effect(card, destination.kind))
	var places := {}
	for steps: Array in step_lists:
		_collect_steps(steps, places)
	for expression: String in expressions:
		_note_place(places, expression, EXPRESSION)
	var trees := {}
	for text: String in places:
		var parsed := StepParser.parse(text)
		if parsed.has("error"):
			errors.append("Could not read step \"%s\": %s" % [text, parsed.error])
		else:
			trees[text] = parsed.tree
	_learn_remembered_names(trees.values())
	_learn_names()
	_check_condition_circles()
	if errors:
		return
	for text: String in trees:
		var problems: Array[String] = []
		_find_problems(trees[text], text, places[text], problems)
		if problems:
			errors.append("Step \"%s\": %s" % [text, ", ".join(problems)])
		else:
			game.step_trees[text] = trees[text]
	_check_remembered_names_are_used(trees.values())
	for text: String in trees:
		if text not in rules.status.values() and text not in rules.conditions.values() and _calls(trees[text], VIEWER_BLOCK):
			errors.append("Step \"%s\": viewer() only works in \"status\" (each screen has its own, so rules can't depend on it)" % text)


## Where each text is used, keeping the strictest place: a whole step may change things and wait for a choice,
## a step's own condition (an "if", a loop's "in", a "while") may wait but not change things, and anything else
## (a "when", a condition, a status) may do neither.
func _collect_steps(steps: Array, places: Dictionary) -> void:
	for step in steps:
		if step is String:
			_note_place(places, step, STEP)
		elif step is Dictionary and _is_blank_condition(step):
			errors.append("A step %s has nothing to check: fill in its condition, or remove it" % JSON.stringify(step).left(60))
		elif _is_valid_conditional(step):
			_note_place(places, step["if"], STEP_CONDITION)
			_collect_steps(step.get("then", []), places)
			_collect_steps(step.get("else", []), places)
		elif _is_valid_loop(step):
			_add_loop_name(step.for)
			_note_place(places, step.in, STEP_CONDITION)
			_collect_steps(step.get("do", []), places)
		elif _is_valid_while(step):
			_note_place(places, step.while, STEP_CONDITION)
			_collect_steps(step.get("do", []), places)
		else:
			errors.append("Invalid step %s: use a string, { \"if\": ..., \"then\": [...], \"else\": [...] }, { \"for\": \"name\", \"in\": ..., \"do\": [...] }, or { \"while\": ..., \"do\": [...] }" % JSON.stringify(step))


static func _note_place(places: Dictionary, text: String, place: int) -> void:
	if text.strip_edges():
		places[text] = mini(places.get(text, place), place)


## An "if", "for each ... in" or "repeat while" whose condition was left empty (e.g. just added in the editor).
static func _is_blank_condition(step: Dictionary) -> bool:
	for key in ["if", "in", "while"]:
		if step.has(key) and step[key] is String and step[key].strip_edges().is_empty():
			return true
	return false


func _add_loop_name(name: String) -> void:
	var names := [name, name + Game.LOOP_NUMBER_SUFFIX]
	if name == Game.PLAYER_VARIABLE:
		names.pop_front()
		if Game.PLAYER_VARIABLE not in game.variables:
			errors.append("Looping over \"player\" needs \"players\" of 2 or more")
	for loop_name in names:
		if loop_name in game.local_names:
			continue
		if loop_name in game.input_names() or loop_name in rules.conditions:
			errors.append("Loop name \"%s\" is already used by a zone, variable or condition" % loop_name)
		else:
			game.local_names.append(loop_name)


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


func _learn_names() -> void:
	known_names.clear()
	for name in Array(game.input_names()) + rules.conditions.keys():
		known_names[name] = true


func _find_problems(node: Dictionary, text: String, place: int, problems: Array[String]) -> void:
	match node.kind:
		"name":
			if node.name not in known_names:
				var problem := "unknown trigger \"%s\" (a card's on_ column names a trigger)" if text.strip_edges() == node.name else "unknown zone or variable \"%s\""
				problems.append(problem % node.name)
		"call":
			var problem := _call_problem(node.name, node.args.size())
			if problem:
				problems.append(problem)
			if node.name in CHOOSE_BLOCKS and place == EXPRESSION:
				problems.append("%s waits for a player to choose, so it only works in steps (not in a when, a condition or a status)" % node.name)
		"assign":
			if place != STEP:
				problems.append("= changes a variable, so it only works as a step of its own; to compare, use ==")
			elif game.aliases().get(node.name, node.name) not in game.variables and node.name not in game.local_names:
				problems.append(_assign_problem(node.name))
	for child in _children(node):
		_find_problems(child, text, place, problems)


## A name set with = that isn't a game variable is remembered by the steps that set it (pile = choose(...)).
## Zones, conditions and the like can't be set, which _find_problems reports.
func _learn_remembered_names(trees: Array) -> void:
	for tree: Dictionary in trees:
		var target: String = tree.name if tree.kind == "assign" else ""
		if target.is_empty() or target in game.local_names or game.aliases().get(target, target) in game.variables:
			continue
		if target not in game.input_names() and target not in rules.conditions and target not in building_blocks:
			game.local_names.append(target)
			remembered_names.append(target)


## A remembered name that's never read is most likely a misspelled variable (phse = 'draw').
func _check_remembered_names_are_used(trees: Array) -> void:
	var read := {}
	for tree: Dictionary in trees:
		for name in _names_in(tree):
			read[name] = true
	for name in remembered_names:
		if name not in read:
			errors.append("\"%s\" is set but never used: is it misspelled? (Values the game keeps go in \"variables\")" % name)


static func _children(node: Dictionary) -> Array:
	match node.kind:
		"list":
			return node.items
		"call":
			return node.args
		"member":
			return [node.target]
		"index":
			return [node.target, node.index]
		"unary":
			return [node.term]
		"binary":
			return [node.left, node.right]
		"choose":
			return [node.condition, node.if_true, node.if_false]
		"assign":
			return [node.value]
	return []


func _call_problem(name: String, given: int) -> String:
	if name in StepEvaluator.MATH_ARGUMENTS:
		var needed: int = StepEvaluator.MATH_ARGUMENTS[name]
		if name in ["min", "max"] and given < needed:
			return "%s takes two or more numbers, got %d" % [name, given]
		if name not in ["min", "max"] and given != needed:
			return "%s takes one number, got %d" % [name, given]
		return ""
	if name not in building_blocks:
		return "unknown building block \"%s\"" % name
	return argument_count_problem(name, given)


func _assign_problem(name: String) -> String:
	if name in known_names and game.value_of(name) is Zone:
		return "\"%s\" is a zone, so = can't change it (move cards with move or draw)" % name
	if name in known_names:
		return "\"%s\" can't be changed with =: only variables can (from \"variables\", \"player_variables\" or \"team_variables\")" % name
	return "unknown variable \"%s\" (declare it in \"variables\")" % name


## Whether the tree, or a condition it names, calls that building block.
func _calls(node: Dictionary, building_block: String, conditions_seen := {}) -> bool:
	if node.is_empty():
		return false
	if node.kind == "call" and node.name == building_block:
		return true
	if node.kind == "name" and node.name in rules.conditions and node.name not in conditions_seen:
		conditions_seen[node.name] = true
		if _calls(game.step_trees.get(rules.conditions[node.name], {}), building_block, conditions_seen):
			return true
	return _children(node).any(func(child: Dictionary) -> bool: return _calls(child, building_block, conditions_seen))


## A condition that names itself, directly or through others, would never finish being worked out.
func _check_condition_circles() -> void:
	for name: String in rules.conditions:
		if _condition_names(name, [name]).has(name):
			errors.append("Condition \"%s\" refers to itself (through %s), so it can never be worked out" % [name, " → ".join(_condition_path(name, [name]))])


## Every condition reachable from this one (stopping at any already on the path).
func _condition_names(name: String, path: Array) -> Dictionary:
	var reached := {}
	var parsed := StepParser.parse(rules.conditions[name])
	for used in _names_in(parsed.get("tree", {})):
		if used not in rules.conditions:
			continue
		reached[used] = true
		if used not in path:
			reached.merge(_condition_names(used, path + [used]))
	return reached


func _condition_path(name: String, path: Array) -> Array:
	var parsed := StepParser.parse(rules.conditions[name])
	for used in _names_in(parsed.get("tree", {})):
		if used == path[0]:
			return path + [used]
		if used in rules.conditions and used not in path:
			var found := _condition_path(used, path + [used])
			if found:
				return found
	return []


static func _names_in(node: Dictionary) -> Array:
	if node.is_empty():
		return []
	var names := [node.name] if node.kind == "name" else []
	for child in _children(node):
		names.append_array(_names_in(child))
	return names


static func argument_count_problem(building_block: String, given: int) -> String:
	var method: Dictionary = building_blocks[building_block]
	var most: int = method.args.size()
	var fewest: int = most - method.default_args.size()
	if given >= fewest and given <= most:
		return ""
	var expected := str(most) if fewest == most else "%d to %d" % [fewest, most]
	var noun := "argument" if expected == "1" else "arguments"
	return "%s takes %s %s, got %d (expected %s)" % [building_block, expected, noun, given, signature(building_block)]


static func _building_blocks() -> Dictionary:
	var actions_script: Script = Actions
	var blocks := {}
	for method: Dictionary in actions_script.get_script_method_list():
		if not method.name.begins_with("_") and not method.flags & METHOD_FLAG_STATIC:
			blocks[method.name] = method
	return blocks
