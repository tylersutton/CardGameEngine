class_name Game
extends RefCounted

signal logged(text: String)
signal failed(text: String)
signal variables_changed
signal state_changed

const CARD_VARIABLE := "card"
const CHOICE_VARIABLE := "choice"
const PLAYERS_VARIABLE := "players"
const ALL_PREFIX := "all_"
const FROM_VARIABLE := "from"
const TO_VARIABLE := "to"
const PLAYER_VARIABLE := "player"
const OPPONENT_PREFIX := "opponent_"
const CARD_EFFECT_PREFIX := "on_"
const CARD_EFFECT_SEPARATOR := ";"
const KEYWORDS := ["true", "false", "null", "and", "or", "not"]
const CONDITIONAL_KEYS := ["if", "then", "else"]
const LOOP_KEYS := ["for", "in", "do"]
const LOOP_NUMBER_SUFFIX := "_number"
const WHILE_KEYS := ["while", "do"]
const MAX_REPEATS := 1000
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
var zone_groups: Dictionary[String, Array] = {}
var meld_rules: Melds
var expressions: Dictionary[String, Expression] = {}
var logic: Dictionary = {}
var evaluation_failed := false
var errors: Array[String] = []
var sandbox := false
var nesting := 0
var choosing := ""
var choices: Array = []
var pick_count := 1
var selected: Array[Card] = []
var chosen: Variant
var asked := ""
var history: Array[Dictionary] = []
var move_from: Zone
var move_to: Zone
var checking_moves := false
var checked_itself := false
var loop_names: Array[String] = []
var loop_values: Dictionary = {}


func _init(rules_path: String) -> void:
	rules = GameRules.load_json(rules_path)
	if rules.error:
		errors.append(rules.error)
		return
	for zone_kind in rules.zones:
		var definition: Dictionary = rules.zones[zone_kind]
		var copies := _copies_of(zone_kind, definition)
		if copies == 0:
			_add_zone(Zone.new(zone_kind, definition), definition)
			continue
		var group_name: String = ALL_PREFIX + zone_kind if definition.get("per_player", false) else zone_kind
		if definition.get("per_player", false):
			per_player_names.append(zone_kind)
		zone_groups[group_name] = []
		for number in range(1, copies + 1):
			var zone := Zone.new(zone_kind, definition, number)
			_add_zone(zone, definition)
			zone_groups[group_name].append(zone)
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
	_perform_asked()
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
		_start_choice(action_name, action)
	else:
		run(action.get("steps", []))
	_finish_input(before)


func ask(action_name: String) -> void:
	if action_name in rules.actions:
		asked = action_name
	else:
		failed.emit("No action named \"%s\" to ask for" % action_name)


func choosing_cards() -> bool:
	return choosing != "" and choices.all(func(option: Variant) -> bool: return option is Card)


func choose_option(option: Variant) -> void:
	if choosing_cards() or option not in choices:
		return
	var before := _snapshot()
	_complete_choice(option)
	_finish_input(before)


func _start_choice(action_name: String, action: Dictionary) -> void:
	choices = _choice_list(_evaluate(action.choose, null))
	pick_count = int(action.get("pick", 1))
	selected = []
	if choices.size() < pick_count:
		logged.emit("  nothing to choose")
		choices = []
		return
	choosing = action_name
	if choosing_cards():
		logged.emit("  choose %d: click the highlighted cards (click anything else to cancel)" % pick_count)
	else:
		logged.emit("  choose one: " + ", ".join(choices.map(format_value)))


func _complete_choice(choice: Variant) -> void:
	var action_name := choosing
	_clear_choice()
	logged.emit("[%s] chose %s" % [action_name, _describe(choice)])
	var outer_choice: Variant = chosen
	chosen = choice
	run(rules.actions[action_name].get("steps", []), choice if choice is Card else null)
	chosen = outer_choice


func _choice_list(options: Variant) -> Array:
	var are_values: bool = options is Array and options.any(func(option: Variant) -> bool:
		return option is not Card and option is not Zone and option is not Array)
	return options.duplicate() if are_values else Actions.cards_in(options)


static func _describe(choice: Variant) -> String:
	if choice is Array:
		return ", ".join(choice.map(format_value))
	return format_value(choice)


func _finish_input(before: Dictionary) -> void:
	_remember(before)
	_perform_asked()
	state_changed.emit()


func _perform_asked() -> void:
	if nesting == 0 and asked:
		var action_name := asked
		asked = ""
		perform(action_name)


func cancel_choice() -> void:
	if choosing:
		logged.emit("[cancelled] " + choosing)
		_clear_choice()


func _clear_choice() -> void:
	choosing = ""
	choices = []
	selected = []
	state_changed.emit()


func drag(card: Card, destination: Zone, index := -1) -> bool:
	cancel_choice()
	var before := _snapshot()
	var source := zone_of(card)
	var rule := {} if sandbox else _matching_move(card, source, destination)
	if rule.is_empty() and not can_move(card, destination):
		logged.emit("[not allowed] %s: %s → %s (%s)" % [card.title, source.name, destination.name, _refusal_reason(source, destination)])
		return false
	_during_move(source, destination, func() -> void:
		for carried_card in carried(card):
			move(carried_card, destination)
		if index >= 0 and destination.sort_columns.is_empty() and card in destination.cards:
			destination.reorder(card, mini(index, destination.cards.size() - 1))
		run(rule.get("then", []), card))
	_finish_input(before)
	return true


func can_move(card: Card, destination: Zone) -> bool:
	return sandbox or rules.moves.is_empty() or not _matching_move(card, zone_of(card), destination).is_empty()


func movable(cards: Array, destinations: Array) -> Array:
	if checking_moves:
		checked_itself = true
		return []
	checking_moves = true
	var result := cards.filter(func(card: Card) -> bool:
		return destinations.any(func(destination: Zone) -> bool:
			return destination != zone_of(card) and can_move(card, destination)))
	checking_moves = false
	if checked_itself:
		checked_itself = false
		failed.emit("movable() was used inside a move rule that movable() was checking; that inner check counts as no moves")
	return result


## The card plus, in zones that drag stacks, every card on top of it.
func carried(card: Card) -> Array[Card]:
	var zone := zone_of(card)
	return zone.cards.slice(zone.cards.find(card)) if zone.drags_stack else [card]


func _during_move(source: Zone, destination: Zone, action: Callable) -> Variant:
	var outer_move := [move_from, move_to]
	move_from = source
	move_to = destination
	var result: Variant = action.call()
	move_from = outer_move[0]
	move_to = outer_move[1]
	return result


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
	if choosing_cards() and card in choices:
		_select(card)
	elif choosing:
		cancel_choice()
	elif zone.on_click:
		logged.emit("[click] %s in %s" % [card.title, zone.name])
		run(zone.on_click, card)
	_finish_input(before)


func _select(card: Card) -> void:
	if pick_count == 1:
		_complete_choice(card)
	elif card in selected:
		selected.erase(card)
	else:
		selected.append(card)
		if selected.size() == pick_count:
			_complete_choice(selected.duplicate())


func click_empty(zone: Zone) -> void:
	var before := _snapshot()
	cancel_choice()
	if zone.on_click:
		logged.emit("[click] " + zone.name)
		run(zone.on_click)
	_finish_input(before)


func undo() -> void:
	if history.is_empty():
		return
	cancel_choice()
	asked = ""
	_restore(history.pop_back())
	while is_bot_turn() and history:
		_restore(history.pop_back())
	logged.emit("[undo]")
	variables_changed.emit()
	state_changed.emit()


func is_bot_turn() -> bool:
	return is_bot(current_player())


func player_name(player: int) -> String:
	var name: String = rules.player_names[player - 1] if player <= rules.player_names.size() else "Player %d" % player
	return name + " (bot)" if is_bot(player) else name


func is_bot(player: int) -> bool:
	return rules.bot.get("players", []).any(func(bot_player: float) -> bool: return int(bot_player) == player)


func legal_inputs() -> Array[Callable]:
	if choosing:
		return _choice_inputs(choices, pick_count)
	var inputs: Array[Callable] = []
	for source: Zone in zones.values():
		for card in _reachable_cards(source):
			for destination: Zone in zones.values():
				if destination != source and not _matching_move(card, source, destination).is_empty():
					inputs.append(drag.bind(card, destination))
		if source.on_click and source.is_empty():
			inputs.append(click_empty.bind(source))
		elif source.on_click:
			for card in _reachable_cards(source):
				inputs.append(click.bind(card))
	for action_name: String in rules.actions:
		var action: Dictionary = rules.actions[action_name]
		if action.get("human_only", false) or not _holds(action.get("when", "")):
			continue
		if not action.has("choose"):
			inputs.append(perform.bind(action_name))
			continue
		for complete_choice in _choice_inputs(_choice_list(_evaluate(action.choose, null)), int(action.get("pick", 1))):
			inputs.append(func() -> void:
				perform(action_name)
				complete_choice.call())
	return inputs


## What a player could pick up: only the top card of a pile.
static func _reachable_cards(zone: Zone) -> Array[Card]:
	if zone.layout == "pile":
		return [zone.cards.back()] if zone.cards else []
	return zone.cards


## Cards whose identity the player can't know: face down, or in another player's hidden hand.
func hidden_from(player: int) -> Array[Card]:
	var hidden: Array[Card] = []
	for zone: Zone in zones.values():
		if zone.owner == player:
			continue
		for card in zone.cards:
			if not card.face_up or zone.face in [Zone.FACE_OWNER, Zone.FACE_HUMAN]:
				hidden.append(card)
	return hidden


func _choice_inputs(options: Array, pick: int) -> Array[Callable]:
	var inputs: Array[Callable] = []
	if options.size() < pick:
		return inputs
	if not options.all(func(option: Variant) -> bool: return option is Card):
		for option in options:
			inputs.append(choose_option.bind(option))
	elif pick == 1:
		for card: Card in options:
			inputs.append(click.bind(card))
	else:
		for combination: Array in _combinations(options, pick):
			inputs.append(func() -> void:
				for card: Card in combination:
					click(card))
	return inputs


static func _combinations(items: Array, size: int) -> Array:
	if size == 0:
		return [[]]
	if items.size() < size:
		return []
	var rest := items.slice(1)
	var with_first := _combinations(rest, size - 1).map(func(combination: Array) -> Array: return [items[0]] + combination)
	return with_first + _combinations(rest, size)


## Runs the input silently, measures the result as the current player, then undoes it.
## Returns null if the input only reordered cards. With deals (card -> data), hidden cards are
## given those identities for each try and the results averaged, so the measure can't peek.
func try_out(input: Callable, measure: String, deals: Array = []) -> Variant:
	if deals.is_empty():
		return _try_once(input, measure)
	var total := 0.0
	for deal: Dictionary in deals:
		var real_data := {}
		for card: Card in deal:
			real_data[card] = card.data
			card.data = deal[card]
		var value: Variant = _try_once(input, measure)
		for card: Card in real_data:
			card.data = real_data[card]
		if value is not float and value is not int:
			return null
		total += value
	return total / deals.size()


func _try_once(input: Callable, measure: String) -> Variant:
	var player := current_player()
	var before := _snapshot()
	var contents_before := _contents()
	var history_size := history.size()
	var choice_state := [choosing, choices, pick_count, selected.duplicate(), asked]
	_set_quiet(true)
	input.call()
	var value: Variant = null
	if _contents() != contents_before:
		if PLAYER_VARIABLE in variables:
			variables[PLAYER_VARIABLE] = player
		value = _evaluate(measure, null)
	_restore(before)
	history.resize(history_size)
	choosing = choice_state[0]
	choices = choice_state[1]
	pick_count = choice_state[2]
	selected.assign(choice_state[3])
	asked = choice_state[4]
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
		if step is Dictionary and step.has("for"):
			if not _run_loop(step, card):
				return false
			continue
		if step is Dictionary and step.has("while"):
			if not _run_while(step, card):
				return false
			continue
		var text: String = step["if"] if step is Dictionary else step
		var result: Variant = _evaluate(text, card)
		if evaluation_failed:
			return false
		if step is Dictionary:
			var branch := "then" if result else "else"
			logged.emit("  if %s → %s" % [text, branch])
			if not run(step.get(branch, []), card):
				return false
	return true


func _run_loop(loop: Dictionary, card: Card) -> bool:
	var items: Variant = _evaluate(loop.in, card)
	if evaluation_failed:
		return false
	var item_list: Variant = _loop_items(items)
	if item_list == null:
		failed.emit("Can't loop over \"%s\": it should be a zone, a list, or a number" % loop.in)
		return false
	var name: String = loop.for
	var number_name := name + LOOP_NUMBER_SUFFIX
	var outer_values := [_loop_value(name), _loop_value(number_name)]
	logged.emit("  for %s in %s (%d)" % [name, loop.in, item_list.size()])
	var completed := true
	for i in item_list.size():
		_set_loop_value(name, item_list[i])
		_set_loop_value(number_name, i + 1)
		if not run(loop.get("do", []), card):
			completed = false
			break
	_set_loop_value(name, outer_values[0])
	_set_loop_value(number_name, outer_values[1])
	return completed


## Looping over "player" changes whose turn it is, so per-player names follow the loop.
func _set_loop_value(name: String, value: Variant) -> void:
	if name == PLAYER_VARIABLE:
		variables[name] = value
	else:
		loop_values[name] = value


func _loop_value(name: String) -> Variant:
	return variables.get(name) if name == PLAYER_VARIABLE else loop_values.get(name)


func _run_while(loop: Dictionary, card: Card) -> bool:
	for repeats in MAX_REPEATS:
		if not _holds(loop.while, card):
			logged.emit("  while %s: repeated %d times" % [loop.while, repeats])
			return not evaluation_failed
		if not run(loop.get("do", []), card):
			return false
	failed.emit("Stopped: \"while %s\" was still true after %d repeats" % [loop.while, MAX_REPEATS])
	return false


func _loop_items(items: Variant) -> Variant:
	if items is Zone:
		return items.cards.duplicate()
	if items is Array:
		return items.duplicate()
	if items is int or items is float:
		return range(1, int(items) + 1)
	return null


func _holds(condition: String, card: Card = null) -> bool:
	if condition.is_empty():
		return true
	var result: Variant = _evaluate(condition, card)
	return not evaluation_failed and (true if result else false)


func _matching_move(card: Card, source: Zone, destination: Zone) -> Dictionary:
	return _during_move(source, destination, func() -> Dictionary:
		for rule: Dictionary in _rules_between(source, destination):
			if _holds(rule.get("when", ""), card):
				return rule
		return {})


func _rules_between(source: Zone, destination: Zone) -> Array:
	return rules.moves.filter(func(rule: Dictionary) -> bool:
		return _names(rule.get("from"), source) and _names(rule.get("to"), destination))


func _names(rule_zone: Variant, zone: Zone) -> bool:
	return rule_zone == null or _resolve(rule_zone) == zone.name or (rule_zone in zone_groups and zone.kind == rule_zone)


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
	var failure := {}
	var result: Variant = _evaluate_logic(logic.get(text, text), card, failure)
	evaluation_failed = not failure.is_empty()
	if evaluation_failed:
		failed.emit("Step failed: \"%s\": %s" % [text, failure.message])
	return result


## "and"/"or" stop as soon as the answer is known, so "count(to) > 0 and top(to).data.x" is safe on an empty zone.
func _evaluate_logic(node: Variant, card: Card, failure: Dictionary) -> Variant:
	if node is String:
		var expression := expressions[node]
		var result: Variant = expression.execute(_input_values(card), Actions.new(self))
		if expression.has_execute_failed():
			failure.message = _failure_message(expression)
		return result
	if node.op == "not":
		var value: Variant = _evaluate_logic(node.term, card, failure)
		return null if failure else not value
	var stops_on: bool = node.op == "or"
	for term: Variant in node.terms:
		var value: Variant = _evaluate_logic(term, card, failure)
		if failure:
			return null
		if (true if value else false) == stops_on:
			return stops_on
	return not stops_on


static func _failure_message(expression: Expression) -> String:
	var message := expression.get_error_text()
	var call := failed_call.search(message)
	if call and call.get_string(1) in building_blocks:
		message += " wrong kind of argument, expected " + signature(call.get_string(1))
	return message


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
	match zone.face:
		Zone.FACE_OWNER:
			return card.face_up and zone.owner == current_player()
		Zone.FACE_HUMAN:
			return card.face_up and not is_bot(zone.owner)
	return card.face_up


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
	return PackedStringArray(zones.keys() + variables.keys() + _aliases().keys() + zone_groups.keys() + loop_names \
		+ [CARD_VARIABLE, FROM_VARIABLE, TO_VARIABLE, CHOICE_VARIABLE, PLAYERS_VARIABLE])


func _input_values(card: Card) -> Array:
	var aliased := _aliases().values().map(func(name: String) -> Variant:
		return zones[name] if name in zones else variables[name])
	var looped := loop_names.map(func(name: String) -> Variant: return loop_values.get(name))
	return zones.values() + variables.values() + aliased + zone_groups.values() + looped \
		+ [card, move_from, move_to, chosen, range(1, rules.players + 1)]


func _add_zone(zone: Zone, definition: Dictionary) -> void:
	if definition.has("cards"):
		_load_cards(zone, definition.cards)
	zones[zone.name] = zone


func _copies_of(zone_kind: String, definition: Dictionary) -> int:
	var per_player: bool = definition.get("per_player", false)
	if per_player and definition.has("copies"):
		errors.append("Zone \"%s\": use \"per_player\" or \"copies\", not both" % zone_kind)
	var copies: int = rules.players if per_player else int(definition.get("copies", 0))
	if copies and definition.has("anchors") and definition.anchors.size() < copies:
		errors.append("Zone \"%s\" needs %d anchors in \"anchors\" (one per %s)" % [zone_kind, copies, "player" if per_player else "copy"])
		return 0
	return copies


func _check_names_are_unique() -> void:
	var seen := {}
	for name in _input_names():
		if name in seen:
			errors.append("\"%s\" is used as more than one zone or variable name" % name)
		seen[name] = true
	for name in rules.conditions:
		if name in seen or name in building_blocks:
			errors.append("Condition \"%s\" has the same name as a zone, variable or building block" % name)
		elif rules.conditions[name] is not String:
			errors.append("Condition \"%s\" should be an expression in text" % name)


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
		_check_entry("Action \"%s\"" % action_name, rules.actions[action_name], {"when": TYPE_STRING, "choose": TYPE_STRING, "pick": TYPE_FLOAT, "human_only": TYPE_BOOL, "button": TYPE_BOOL, "steps": TYPE_ARRAY})
		var action: Variant = rules.actions[action_name]
		if action is Dictionary and action.has("pick") and not action.has("choose"):
			errors.append("Action \"%s\": \"pick\" needs \"choose\"" % action_name)
	for rule in rules.moves:
		var label := "Move %s" % JSON.stringify(rule)
		_check_entry(label, rule, {"from": TYPE_STRING, "to": TYPE_STRING, "when": TYPE_STRING, "then": TYPE_ARRAY})
		for end in ["from", "to"]:
			var zone_name: Variant = rule.get(end) if rule is Dictionary else null
			if zone_name is String and zone_name not in zones and zone_name not in _aliases() and zone_name not in zone_groups:
				errors.append("%s: unknown zone \"%s\"" % [label, zone_name])
	for list_name in ["seats", "player_names"]:
		var list: Array = rules.get(list_name)
		if list and list.size() < rules.players:
			errors.append("\"%s\" needs one entry per player (%d)" % [list_name, rules.players])
	if rules.bot:
		_check_entry("bot", rules.bot, {"players": TYPE_ARRAY, "prefer": TYPE_STRING, "peek": TYPE_BOOL, "samples": TYPE_FLOAT})
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
	conditions.append_array(rules.conditions.values())
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
	var names := [name, name + LOOP_NUMBER_SUFFIX]
	if name == PLAYER_VARIABLE:
		names.pop_front()
		if PLAYER_VARIABLE not in variables:
			errors.append("Looping over \"player\" needs \"players\" of 2 or more")
	for loop_name in names:
		if loop_name in loop_names:
			continue
		if loop_name in _input_names() or loop_name in rules.conditions:
			errors.append("Loop name \"%s\" is already used by a zone, variable or condition" % loop_name)
		else:
			loop_names.append(loop_name)


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
	return step is Dictionary 		and step.get("while") is String 		and step.keys().all(func(key: String) -> bool: return key in WHILE_KEYS) 		and step.get("do", []) is Array


func _compile(step: String) -> void:
	var input_names := _input_names()
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
	expressions[step] = expression
	var tree: Variant = _logic_tree(code)
	if tree is Dictionary:
		logic[step] = tree
		_compile_logic_parts(tree, input_names)


func _compile_logic_parts(node: Variant, input_names: PackedStringArray) -> void:
	if node is Dictionary:
		for term: Variant in node.get("terms", [node.get("term")]):
			_compile_logic_parts(term, input_names)
	elif node not in expressions:
		var expression := Expression.new()
		expression.parse(node, input_names)
		expressions[node] = expression


## Splits an expression on its outermost "or", then "and", then "not", into a tree evaluated left to right.
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
	for depth in MAX_NESTING:
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
