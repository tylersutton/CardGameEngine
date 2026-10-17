class_name Game
extends RefCounted

signal logged(text: String)
signal failed(text: String)
signal variables_changed
signal state_changed

const CARD_VARIABLE := "card"
const PLAYER_VARIABLE := "player"
const OPPONENT_PREFIX := "opponent_"
const CARD_EFFECT_PREFIX := "on_"
const CARD_EFFECT_SEPARATOR := ";"
const KEYWORDS := ["true", "false", "null", "and", "or", "not"]
const CONDITIONAL_KEYS := ["if", "then", "else"]
const TYPE_DESCRIPTIONS := {TYPE_STRING: "text", TYPE_ARRAY: "a list", TYPE_FLOAT: "a number", TYPE_BOOL: "true or false"}
const MAX_NESTING := 50

static var building_blocks := _building_blocks()
static var failed_call := RegEx.create_from_string(r"On call to '(\w+)'")
static var string_literal := RegEx.create_from_string("'[^']*'|\"[^\"]*\"")
static var name_in_code := RegEx.create_from_string(r"(?<![.\w])([A-Za-z_]\w*)\s*(\()?")

var rules: GameRules
var zones: Dictionary[String, Zone] = {}
var variables: Dictionary = {}
var per_player_names: Array[String] = []
var meld_rules: Melds
var expressions: Dictionary[String, Expression] = {}
var errors: Array[String] = []
var sandbox := false
var nesting := 0
var choosing := ""
var choices: Array = []
var history: Array[Dictionary] = []


func _init(rules_path: String) -> void:
	rules = GameRules.load_json(rules_path)
	if rules.error:
		errors.append(rules.error)
		return
	for zone_kind in rules.zones:
		var definition: Dictionary = rules.zones[zone_kind]
		if not definition.get("per_player", false):
			_add_zone(Zone.new(zone_kind, definition), definition)
		elif definition.get("anchors", []).size() < rules.players and definition.has("anchors"):
			errors.append("Zone \"%s\" needs one anchor per player in \"anchors\"" % zone_kind)
			per_player_names.append(zone_kind)
		else:
			per_player_names.append(zone_kind)
			for player in range(1, rules.players + 1):
				_add_zone(Zone.new(zone_kind, definition, player), definition)
	variables = rules.variables.duplicate(true)
	if rules.players > 1:
		variables[PLAYER_VARIABLE] = 1
	for name in rules.player_variables:
		per_player_names.append(name)
		for player in range(1, rules.players + 1):
			var player_variable := "%s_%d" % [name, player]
			if player_variable in variables:
				errors.append("\"%s\" is used as more than one zone or variable name" % player_variable)
			variables[player_variable] = rules.player_variables[name]
	meld_rules = Melds.new(rules.melds)
	_check_names_are_unique()
	_check_actions_and_moves()
	_check_melds()
	if errors.is_empty():
		_compile_all_steps()


func start() -> void:
	trigger("start")
	state_changed.emit()


func can_perform(action_name: String) -> bool:
	return sandbox or _holds(rules.actions[action_name].get("when", ""))


func perform(action_name: String) -> void:
	if not can_perform(action_name):
		return
	cancel_choice()
	var before := _snapshot()
	var action: Dictionary = rules.actions[action_name]
	logged.emit("[action] " + action_name)
	if action.has("choose"):
		choices = Actions.cards_in(_evaluate(action.choose, null))
		if choices:
			choosing = action_name
			logged.emit("  choose a card (click one of the highlighted cards; click anything else to cancel)")
		else:
			logged.emit("  nothing to choose")
	else:
		run(action.get("steps", []))
	_remember(before)
	state_changed.emit()


func cancel_choice() -> void:
	if choosing:
		logged.emit("[cancelled] " + choosing)
		_clear_choice()


func _clear_choice() -> void:
	choosing = ""
	choices = []
	state_changed.emit()


func drag(card: Card, destination: Zone) -> bool:
	cancel_choice()
	var before := _snapshot()
	var source := zone_of(card)
	if sandbox or rules.moves.is_empty():
		move(card, destination)
	else:
		var rule := _matching_move(card, source, destination)
		if rule.is_empty():
			logged.emit("[not allowed] %s: %s → %s (%s)" % [card.title, source.name, destination.name, _refusal_reason(source, destination)])
			return false
		move(card, destination)
		run(rule.get("then", []), card)
	_remember(before)
	state_changed.emit()
	return true


func reorder(card: Card, index: int) -> void:
	var before := _snapshot()
	var zone := zone_of(card)
	zone.reorder(card, index)
	_remember(before)
	logged.emit("  %s: position %d in %s (manual order)" % [card.title, index + 1, zone.name])
	state_changed.emit()


func trigger(event: String) -> void:
	if event in rules.triggers:
		logged.emit("[trigger] " + event)
		run(rules.triggers[event])


func current_player() -> int:
	return int(variables.get(PLAYER_VARIABLE, 1))


func next_player() -> int:
	return current_player() % rules.players + 1


func end_turn() -> void:
	set_variable(PLAYER_VARIABLE, next_player())
	trigger("turn_start")


func click(card: Card) -> void:
	var before := _snapshot()
	var zone := zone_of(card)
	if choosing and card in choices:
		var action_name := choosing
		_clear_choice()
		logged.emit("[%s] chose %s" % [action_name, card.title])
		run(rules.actions[action_name].get("steps", []), card)
	elif choosing:
		cancel_choice()
	elif zone.on_click:
		logged.emit("[click] %s in %s" % [card.title, zone.name])
		run(zone.on_click, card)
	_remember(before)
	state_changed.emit()


func undo() -> void:
	if history.is_empty():
		return
	cancel_choice()
	_restore(history.pop_back())
	while is_bot_turn() and history:
		_restore(history.pop_back())
	logged.emit("[undo]")
	variables_changed.emit()
	state_changed.emit()


func is_bot_turn() -> bool:
	return rules.bot.get("players", []).any(func(bot_player: float) -> bool: return int(bot_player) == current_player())


func legal_inputs() -> Array[Callable]:
	var inputs: Array[Callable] = []
	for source: Zone in zones.values():
		var reachable_cards: Array = [source.cards.back()] if source.layout == "pile" and source.cards else source.cards
		for card: Card in reachable_cards:
			for destination: Zone in zones.values():
				if destination != source and not _matching_move(card, source, destination).is_empty():
					inputs.append(drag.bind(card, destination))
	for action_name: String in rules.actions:
		var action: Dictionary = rules.actions[action_name]
		if action.get("human_only", false) or not _holds(action.get("when", "")):
			continue
		if not action.has("choose"):
			inputs.append(perform.bind(action_name))
			continue
		for choice: Card in Actions.cards_in(_evaluate(action.choose, null)):
			inputs.append(func() -> void:
				perform(action_name)
				click(choice))
	return inputs


## Runs the input silently, measures the result as the current player, then undoes it.
## Returns null if the input only reordered cards.
func try_out(input: Callable, measure: String) -> Variant:
	var player := current_player()
	var before := _snapshot()
	var contents_before := _contents()
	var history_size := history.size()
	_set_quiet(true)
	input.call()
	var value: Variant = null
	if _contents() != contents_before:
		if PLAYER_VARIABLE in variables:
			variables[PLAYER_VARIABLE] = player
		value = _evaluate(measure, null)
	_restore(before)
	history.resize(history_size)
	_set_quiet(false)
	return value


func _restore(snapshot: Dictionary) -> void:
	for name in zones:
		zones[name].restore(snapshot.zones[name], snapshot.sort_columns[name])
	for card: Card in snapshot.faces:
		card.face_up = snapshot.faces[card]
	variables.clear()
	variables.merge(snapshot.variables.duplicate(true))


func _contents() -> Dictionary:
	var contents := {variables = variables.duplicate(true)}
	for name in zones:
		var face_up_ids := zones[name].cards.map(func(card: Card) -> int: return card.get_instance_id() * (1 if card.face_up else -1))
		face_up_ids.sort()
		contents[name] = face_up_ids
	return contents


func _set_quiet(quiet: bool) -> void:
	set_block_signals(quiet)
	for zone: Zone in zones.values():
		zone.set_block_signals(quiet)


func _snapshot() -> Dictionary:
	var snapshot := {zones = {}, sort_columns = {}, faces = {}, variables = variables.duplicate(true)}
	for name in zones:
		snapshot.zones[name] = zones[name].cards.duplicate()
		snapshot.sort_columns[name] = zones[name].sort_columns.duplicate()
		for card in zones[name].cards:
			snapshot.faces[card] = card.face_up
	return snapshot


func _remember(before: Dictionary) -> void:
	if nesting == 0 and before != _snapshot():
		history.append(before)


func run(steps: Array, card: Card = null) -> bool:
	if nesting >= MAX_NESTING:
		failed.emit("Stopped: steps triggered each other %d levels deep (endless loop?)" % MAX_NESTING)
		return false
	nesting += 1
	var completed := _run_steps(steps, card)
	nesting -= 1
	return completed


func _run_steps(steps: Array, card: Card) -> bool:
	for step in steps:
		var text: String = step["if"] if step is Dictionary else step
		var result: Variant = _evaluate(text, card)
		if expressions[text].has_execute_failed():
			return false
		if step is Dictionary:
			var branch := "then" if result else "else"
			logged.emit("  if %s → %s" % [text, branch])
			if not run(step.get(branch, []), card):
				return false
	return true


func _holds(condition: String, card: Card = null) -> bool:
	if condition.is_empty():
		return true
	var result: Variant = _evaluate(condition, card)
	return not expressions[condition].has_execute_failed() and (true if result else false)


func _matching_move(card: Card, source: Zone, destination: Zone) -> Dictionary:
	for rule: Dictionary in _rules_between(source, destination):
		if _holds(rule.get("when", ""), card):
			return rule
	return {}


func _rules_between(source: Zone, destination: Zone) -> Array:
	return rules.moves.filter(func(rule: Dictionary) -> bool:
		return _resolve(rule.get("from", source.name)) == source.name \
			and _resolve(rule.get("to", destination.name)) == destination.name)


func _refusal_reason(source: Zone, destination: Zone) -> String:
	var conditions: Array = _rules_between(source, destination).map(func(rule: Dictionary) -> String: return rule.get("when", ""))
	if conditions.is_empty():
		return "no move rule allows this"
	return "requires: " + " OR ".join(conditions)


func status_values() -> Dictionary:
	var values := {}
	for label in rules.status:
		values[label] = format_value(_evaluate(rules.status[label], null))
	return values


func _evaluate(text: String, card: Card) -> Variant:
	var expression := expressions[text]
	var result: Variant = expression.execute(_input_values(card), Actions.new(self))
	if expression.has_execute_failed():
		var message := expression.get_error_text()
		var call := failed_call.search(message)
		if call and call.get_string(1) in building_blocks:
			message += " wrong kind of argument, expected " + signature(call.get_string(1))
		failed.emit("Step failed: \"%s\": %s" % [text, message])
	return result


func move(card: Card, destination: Zone) -> void:
	var source := zone_of(card)
	source.remove(card)
	destination.add(card)
	logged.emit("  %s: %s → %s" % [card.title, source.name, destination.name])
	run(_card_effect(card, destination), card)


func flip(card: Card) -> void:
	card.face_up = not card.face_up
	var zone := zone_of(card)
	logged.emit("  %s: face %s in %s" % [card.title, "up" if card.face_up else "down", zone.name])
	zone.changed.emit(zone)


func shows_face(card: Card) -> bool:
	var zone := zone_of(card)
	return card.face_up and (zone.face != Zone.FACE_OWNER or zone.owner == current_player())


func set_variable(variable: String, value: Variant) -> void:
	var name := _resolve(variable)
	if name not in variables:
		failed.emit("Unknown variable \"%s\" (declare it in \"variables\")" % name)
		return
	variables[name] = value
	logged.emit("  %s = %s" % [name, format_value(value)])
	variables_changed.emit()


static func format_value(value: Variant) -> String:
	if value is Card:
		return value.title
	if value == null:
		return "—"
	if value is float and value == floorf(value):
		return str(int(value))
	return str(value)


func zone_of(card: Card) -> Zone:
	for zone: Zone in zones.values():
		if card in zone.cards:
			return zone
	return null


func _aliases() -> Dictionary:
	var aliases := {}
	for name in per_player_names:
		aliases[name] = "%s_%d" % [name, current_player()]
		aliases[OPPONENT_PREFIX + name] = "%s_%d" % [name, next_player()]
	return aliases


func _resolve(name: String) -> String:
	return _aliases().get(name, name)


func _input_names() -> PackedStringArray:
	return PackedStringArray(zones.keys() + variables.keys() + _aliases().keys() + [CARD_VARIABLE])


func _input_values(card: Card) -> Array:
	var aliased := _aliases().values().map(func(name: String) -> Variant:
		return zones[name] if name in zones else variables[name])
	return zones.values() + variables.values() + aliased + [card]


func _add_zone(zone: Zone, definition: Dictionary) -> void:
	if definition.has("cards"):
		_load_cards(zone, definition.cards)
	zones[zone.name] = zone


func _check_names_are_unique() -> void:
	var seen := {}
	for name in _input_names():
		if name in seen:
			errors.append("\"%s\" is used as more than one zone or variable name" % name)
		seen[name] = true


func _load_cards(zone: Zone, path: String) -> void:
	if FileAccess.file_exists(path):
		zone.add_all(DeckLoader.load_csv(path))
	else:
		errors.append("Deck not found: " + path)


func _card_effect(card: Card, zone: Zone) -> Array:
	var steps: String = card.data.get(CARD_EFFECT_PREFIX + zone.kind, "").strip_edges()
	if steps.begins_with("["):
		var json_steps: Variant = JSON.parse_string(steps)
		return json_steps if json_steps is Array else [steps]
	var step_list := []
	for part in steps.split(CARD_EFFECT_SEPARATOR, false):
		var step := part.strip_edges()
		if rules.effects.get(step) is Array:
			step_list.append_array(rules.effects[step])
		else:
			step_list.append(step)
	return step_list


func _check_actions_and_moves() -> void:
	for action_name in rules.actions:
		_check_entry("Action \"%s\"" % action_name, rules.actions[action_name], {"when": TYPE_STRING, "choose": TYPE_STRING, "human_only": TYPE_BOOL, "steps": TYPE_ARRAY})
	for rule in rules.moves:
		var label := "Move %s" % JSON.stringify(rule)
		_check_entry(label, rule, {"from": TYPE_STRING, "to": TYPE_STRING, "when": TYPE_STRING, "then": TYPE_ARRAY})
		for end in ["from", "to"]:
			var zone_name: Variant = rule.get(end) if rule is Dictionary else null
			if zone_name is String and zone_name not in zones and zone_name not in _aliases():
				errors.append("%s: unknown zone \"%s\"" % [label, zone_name])
	if rules.bot:
		_check_entry("bot", rules.bot, {"players": TYPE_ARRAY, "prefer": TYPE_STRING})
		if not rules.bot.has("prefer"):
			errors.append("bot needs \"prefer\": an expression the bot tries to make as high as possible")
	for effect_name in rules.effects:
		if rules.effects[effect_name] is not Array:
			errors.append("Effect \"%s\" should be a list of steps" % effect_name)
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
		elif typeof(entry[key]) != field_types[key]:
			errors.append("%s: \"%s\" should be %s" % [label, key, TYPE_DESCRIPTIONS[field_types[key]]])


func _compile_all_steps() -> void:
	var step_lists: Array = rules.triggers.values() + rules.effects.values()
	var conditions: Array = []
	for action: Dictionary in rules.actions.values():
		step_lists.append(action.get("steps", []))
		conditions.append(action.get("when", ""))
		conditions.append(action.get("choose", ""))
	for rule: Dictionary in rules.moves:
		step_lists.append(rule.get("then", []))
		conditions.append(rule.get("when", ""))
	conditions.append_array(rules.status.values())
	conditions.append(rules.bot.get("prefer", ""))
	for zone: Zone in zones.values():
		step_lists.append(zone.on_click)
		for card in zone.cards:
			for destination: Zone in zones.values():
				step_lists.append(_card_effect(card, destination))
	var unique_steps := {}
	for steps: Array in step_lists:
		_collect_steps(steps, unique_steps)
	for condition: String in conditions:
		if condition:
			unique_steps[condition] = true
	for step: String in unique_steps:
		_compile(step)


func _collect_steps(steps: Array, into: Dictionary) -> void:
	for step in steps:
		if step is String:
			into[step] = true
		elif _is_valid_conditional(step):
			into[step["if"]] = true
			_collect_steps(step.get("then", []), into)
			_collect_steps(step.get("else", []), into)
		else:
			errors.append("Invalid step %s: use a string, or { \"if\": ..., \"then\": [...], \"else\": [...] }" % JSON.stringify(step))


func _is_valid_conditional(step: Variant) -> bool:
	return step is Dictionary \
		and step.get("if") is String \
		and step.keys().all(func(key: String) -> bool: return key in CONDITIONAL_KEYS) \
		and step.get("then", []) is Array \
		and step.get("else", []) is Array


func _compile(step: String) -> void:
	var input_names := _input_names()
	var expression := Expression.new()
	if expression.parse(step, input_names) != OK:
		errors.append("Could not parse step \"%s\": %s" % [step, expression.get_error_text()])
		return
	var problems := _problems_in(step, input_names)
	if problems:
		errors.append("Step \"%s\": %s" % [step, ", ".join(problems)])
		return
	expressions[step] = expression


func _problems_in(step: String, input_names: PackedStringArray) -> Array[String]:
	var problems: Array[String] = []
	var code := string_literal.sub(step, "''", true)
	for found in name_in_code.search_all(code):
		var name := found.get_string(1)
		var is_call := found.get_string(2) != ""
		if name in KEYWORDS:
			continue
		if is_call and name not in building_blocks:
			problems.append("unknown building block \"%s\"" % name)
		elif is_call:
			var problem := _argument_count_problem(name, _argument_count(code, found.get_end()))
			if problem:
				problems.append(problem)
		elif name not in input_names:
			var problem := "unknown effect \"%s\" (define it in \"effects\")" if step == name else "unknown zone or variable \"%s\""
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


static func signature(building_block: String) -> String:
	var method: Dictionary = building_blocks[building_block]
	var parameters: Array = method.args.map(func(argument: Dictionary) -> String: return argument.name)
	return "%s(%s)" % [building_block, ", ".join(parameters)]


static func _building_blocks() -> Dictionary:
	var actions_script: Script = Actions
	var blocks := {}
	for method: Dictionary in actions_script.get_script_method_list():
		if not method.name.begins_with("_") and not method.flags & METHOD_FLAG_STATIC:
			blocks[method.name] = method
	return blocks
