class_name Game
extends RefCounted

signal failed(text: String)
signal variables_changed
signal state_changed
signal input_applied(input: Dictionary)

const CARD_VARIABLE := "card"
const CHOICE_VARIABLE := "choice"
const PLAYERS_VARIABLE := "players"
const ALL_PREFIX := "all_"
const FROM_VARIABLE := "from"
const TO_VARIABLE := "to"
const PLAYER_VARIABLE := "player"
const OPPONENT_PREFIX := "opponent_"
const TEAM_PREFIX := "team_"
const CARD_EFFECT_PREFIX := "on_"
const HIDDEN_CARD := "a hidden card"
const CARD_EFFECT_SEPARATOR := ";"
const LOOP_NUMBER_SUFFIX := "_number"
const PHASE_KEY := "phase"
const MAX_REPEATS := 1000
const MAX_NESTING := 50


var rules: GameRules
var rules_path: String
var match_seed: int
var rng := RandomNumberGenerator.new()
var cards: Array[Card] = []
var inputs: Array[Dictionary] = []
var local_seat := 0
var given_files := {}
var zones: Dictionary[String, Zone] = {}
var variables: Dictionary = {}
var per_player_names: Array[String] = []
var per_team_names: Array[String] = []
var aliases_by_player: Dictionary = {}
var team_groups_by_team: Dictionary = {}
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
var pick_exact := true
var selected: Array[Card] = []
var chosen: Variant
var asked: Array[Dictionary] = []
var choice_asked := false
var return_player := 0
var performing_asked := false
var history: Array[Dictionary] = []
var game_log := GameLog.new()
var move_from: Zone
var move_to: Zone
var checking_moves := false
var checked_itself := false
var loop_names: Array[String] = []
var loop_values: Dictionary = {}


## A seed makes the match repeatable: the same seed and inputs always give the same game.
## files (path -> text) stands in for the game and deck files, e.g. ones sent by an online host.
func _init(path: String, seed := -1, files := {}) -> void:
	rules_path = path
	match_seed = seed if seed >= 0 else randi()
	rng.seed = match_seed
	given_files = files
	rules = GameRules.load_json(rules_path, files)
	if rules.error:
		errors.append(rules.error)
		return
	var compiler := StepCompiler.new(self)
	compiler.check_zone_definitions()
	if errors:
		return
	for zone_kind in rules.zones:
		var definition: Dictionary = rules.zones[zone_kind]
		var copies := _copies_of(definition)
		if copies == 0:
			_add_zone(Zone.new(zone_kind, definition), definition)
			continue
		var group_name: String = ALL_PREFIX + zone_kind if definition.get("per_player", false) else zone_kind
		if definition.get("per_player", false):
			per_player_names.append(zone_kind)
		zone_groups[group_name] = []
		for number in range(1, copies + 1):
			var zone := Zone.new(zone_kind, definition, number, rules.players)
			_add_zone(zone, definition)
			zone_groups[group_name].append(zone)
			if definition.has("group"):
				zone_groups.get_or_add(definition.group, []).append(zone)
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
	for name in rules.team_variables:
		per_team_names.append(name)
		for team in range(1, rules.teams.size() + 1):
			var team_variable := "%s_%d" % [name, team]
			if team_variable in variables:
				errors.append("\"%s\" is used as more than one zone or variable name" % team_variable)
			variables[team_variable] = rules.team_variables[name]
	meld_rules = Melds.new(rules.melds)
	compiler.check_and_compile()


func start() -> void:
	trigger("start")
	game_log.flush()
	_perform_asked()
	state_changed.emit()


func can_perform(action_name: String) -> bool:
	if sandbox:
		return true
	return not waiting_for_choice() and applies(rules.actions[action_name])


## A choice a rule asked for (e.g. "discard down to 3") must be made before anything else happens.
func waiting_for_choice() -> bool:
	return choosing != "" and choice_asked


func perform(action_name: String) -> void:
	if not can_perform(action_name):
		return
	cancel_choice()
	var before := Snapshot.take(self)
	var action: Dictionary = rules.actions[action_name]
	log_line("[action] " + action_name)
	if action.has("choose"):
		_start_choice(action_name, action)
	else:
		run(action.get("steps", []))
	_finish_input(before)


## The one way in for people, bots and (later) the network. An input is plain data, so it can be sent,
## recorded and replayed: {type = "drag", card = 12, to = "pile", index = -1}, {type = "perform", action = "Pass"},
## {type = "click", card = 7}, {type = "double_click", card = 7}, {type = "click_empty", zone = "stock"},
## {type = "choose", option = "Red"}, {type = "finish"}, {type = "reorder", card = 3, index = 0}, {type = "undo"}.
## With "player", it's refused unless that player may act now; without, it's someone at this screen.
func apply(input: Dictionary) -> bool:
	if not _is_well_formed(input):
		report_failure("Ignored a malformed input: " + JSON.stringify(input))
		return false
	var player := int(input.get("player", local_seat))
	if player and not may_act(player, input):
		log_line("[refused] %s can't do that now: %s" % [player_name(player), input.type])
		game_log.flush()
		return false
	if not run_input(input):
		return false
	inputs.append(input.duplicate())
	input_applied.emit(inputs.back())
	return true


## Applies each input of a move (e.g. press an action, then click the card to choose), as that player.
func apply_move(move: Array, player := 0) -> void:
	for input: Dictionary in move:
		var sent := input.duplicate()
		if player:
			sent.player = player
		apply(sent)


## Who may make an input: the player whose turn (or choice) it is. Human-only buttons are for any person,
## anyone may rearrange their own cards, and undo is for the table to agree on.
func may_act(player: int, input: Dictionary) -> bool:
	match input.type:
		"undo":
			return true
		"reorder":
			return zone_of(cards[int(input.card)]).owner in [0, player]
		"perform":
			if rules.actions[input.action].get("human_only", false):
				return not is_bot(player)
	return sandbox or player == current_player()


func _is_well_formed(input: Dictionary) -> bool:
	if input.has("card") and not (input.card is float or input.card is int) or int(input.get("card", 0)) not in range(cards.size()):
		return false
	match input.get("type"):
		"drag":
			return input.has("card") and input.get("to") in zones
		"click", "double_click", "reorder":
			return input.has("card")
		"click_empty":
			return input.get("zone") in zones
		"perform":
			return input.get("action") in rules.actions
		"choose":
			return input.has("option")
		"finish", "undo":
			return true
	return false


## Carries out an input without checking who sent it or recording it (apply does both; bots trying moves out don't).
func run_input(input: Dictionary) -> bool:
	var card: Card = cards[int(input.card)] if input.has("card") else null
	match input.type:
		"drag":
			return drag(card, zones[input.to], int(input.get("index", -1)))
		"click":
			click(card)
		"double_click":
			double_click(card)
		"click_empty":
			click_empty(zones[input.zone])
		"perform":
			perform(input.action)
		"choose":
			# Sent numbers arrive as decimals (4.0), so match options by value.
			var matches := choices.filter(func(option: Variant) -> bool: return typeof(option) == typeof(input.option) and option == input.option \
				or (option is int or option is float) and (input.option is int or input.option is float) and option == input.option)
			if matches.is_empty():
				return false
			choose_option(matches[0])
		"finish":
			finish_choice()
		"reorder":
			reorder(card, int(input.index))
		"undo":
			undo()
	return true


## Everything that decides what happens next, as one number, so two copies of a match can be compared.
func state_hash() -> int:
	return Snapshot.state_hash(self)


## A match is its rules, seed and inputs; replaying them rebuilds it exactly.
func record() -> Dictionary:
	return {rules = rules_path, seed = match_seed, inputs = inputs.duplicate(true)}


static func replay(match_record: Dictionary) -> Game:
	var game := Game.new(match_record.rules, int(match_record.seed))
	game.start()
	for input: Dictionary in match_record.inputs:
		game.apply(input)
	return game


func ask(action_name: String, player := 0) -> void:
	if action_name in rules.actions:
		asked.append({action = action_name, player = player})
	else:
		report_failure("No action named \"%s\" to ask for" % action_name)


## Everyone makes the same choice, people first so nobody waits on the bots, then then_trigger runs.
## Choices go somewhere hidden (like a face-down zone), so nobody sees the others' until the trigger reveals them.
func ask_all(action_name: String, then_trigger: String) -> void:
	if then_trigger and then_trigger not in rules.triggers:
		report_failure("No trigger named \"%s\" in \"triggers\"" % then_trigger)
		return
	var players := range(1, rules.players + 1)
	for player in players.filter(func(each: int) -> bool: return not is_bot(each)) + players.filter(is_bot):
		ask(action_name, player)
	if then_trigger:
		asked.append({trigger = then_trigger})


## A response window: each other player in turn order is asked, then then_trigger runs. A response that opens
## a new window (a "Nope" to a "Nope") calls off the rest of this one, so the trigger runs once, when nobody responds.
func ask_others(action_name: String, then_trigger: String) -> void:
	if then_trigger and then_trigger not in rules.triggers:
		report_failure("No trigger named \"%s\" in \"triggers\"" % then_trigger)
		return
	asked.assign(asked.filter(func(request: Dictionary) -> bool: return not request.get("window", false)))
	for seat in range(1, rules.players):
		asked.append({action = action_name, player = (current_player() - 1 + seat) % rules.players + 1, window = true})
	if then_trigger:
		asked.append({trigger = then_trigger, window = true})


func choosing_cards() -> bool:
	return choosing != "" and choices.all(func(option: Variant) -> bool: return option is Card)


func choose_option(option: Variant) -> void:
	if choosing_cards() or option not in choices:
		return
	var before := Snapshot.take(self)
	_complete_choice(option)
	_finish_input(before)


func finish_choice() -> void:
	if not choosing_cards() or pick_exact:
		return
	var before := Snapshot.take(self)
	_complete_choice(selected.duplicate())
	_finish_input(before)


func _start_choice(action_name: String, action: Dictionary) -> void:
	choices = _choice_list(evaluate(action.choose, null))
	var rule := _pick_rule(action, choices.size())
	pick_count = rule[0]
	pick_exact = rule[1]
	selected = []
	if choices.is_empty() or choices.size() < pick_count:
		log_line("  nothing to choose")
		choices = []
		return
	choosing = action_name
	choice_asked = performing_asked
	if not choosing_cards():
		log_line("  choose one: " + ", ".join(choices.map(format_value)))
	elif pick_exact:
		log_line("  %s: choose %d (click the highlighted cards%s)" % [player_name(current_player()), pick_count, "" if choice_asked else "; click anything else to cancel"])
	else:
		log_line("  %s: choose up to %d (click the highlighted cards, then Done)" % [player_name(current_player()), pick_count])


## How many to pick: [count, exactly]. "up_to" (a number or an expression) allows anything from none to that many.
func _pick_rule(action: Dictionary, option_count: int) -> Array:
	if action.has("up_to"):
		var limit: Variant = action.up_to if action.up_to is float else evaluate(action.up_to, null)
		return [mini(int(limit), option_count), false]
	var pick: Variant = action.get("pick", 1.0)
	return [int(pick if pick is float else evaluate(pick, null)), true]


func _complete_choice(choice: Variant) -> void:
	var action_name := choosing
	if not _allows(action_name, choice):
		log_line("[not allowed] %s: requires %s" % [describe(choice), rules.actions[action_name].allow])
		return
	var back_to := return_player
	_clear_choice()
	log_line("[%s] chose %s" % [action_name, describe(choice)])
	var outer_choice: Variant = chosen
	chosen = choice
	run(rules.actions[action_name].get("steps", []), choice if choice is Card else null)
	chosen = outer_choice
	if back_to:
		set_variable(PLAYER_VARIABLE, back_to)


## "allow" refuses a choice as a whole, e.g. cards that aren't all the same rank.
func _allows(action_name: String, choice: Variant) -> bool:
	var allow: String = rules.actions[action_name].get("allow", "")
	if allow.is_empty():
		return true
	var outer_choice: Variant = chosen
	chosen = choice
	var allowed := holds(allow, choice if choice is Card else null)
	chosen = outer_choice
	return allowed


func _choice_list(options: Variant) -> Array:
	var are_values: bool = options is Array and options.any(func(option: Variant) -> bool:
		return option is not Card and option is not Zone and option is not Array)
	return options.duplicate() if are_values else Actions.cards_in(options)


## Like format_value, but a card the viewer can't see is "a hidden card", so the log doesn't give away hands.
func describe(value: Variant) -> String:
	if value is Card:
		return value.title if shows_face(value) else HIDDEN_CARD
	if value is Array:
		return ", ".join(value.map(describe))
	return format_value(value)


func _finish_input(before: Dictionary) -> void:
	game_log.flush()
	_perform_asked()
	_remember(before)
	state_changed.emit()


## Asked-for actions run one at a time, each once the previous choice is finished.
func _perform_asked() -> void:
	if performing_asked:
		return
	performing_asked = true
	while nesting == 0 and asked and not choosing:
		var request: Dictionary = asked.pop_front()
		if request.has("trigger"):
			trigger(request.trigger)
			continue
		var turn := current_player()
		var history_size := history.size()
		if request.player and request.player != turn:
			set_variable(PLAYER_VARIABLE, request.player)
		perform(request.action)
		history.resize(history_size)
		if choosing:
			return_player = turn if current_player() != turn else 0
		elif current_player() != turn:
			set_variable(PLAYER_VARIABLE, turn)
	performing_asked = false


func cancel_choice() -> void:
	if choosing and not choice_asked:
		log_line("[cancelled] " + choosing)
		_clear_choice()


func _clear_choice() -> void:
	choosing = ""
	choices = []
	selected = []
	choice_asked = false
	return_player = 0
	state_changed.emit()


func drag(card: Card, destination: Zone, index := -1) -> bool:
	if waiting_for_choice() and not sandbox:
		log_line("[waiting] %s must finish \"%s\" first" % [player_name(current_player()), choosing])
		game_log.flush()
		return false
	cancel_choice()
	var before := Snapshot.take(self)
	var source := zone_of(card)
	var rule := {} if sandbox else _matching_move(card, source, destination)
	if rule.is_empty() and not can_move(card, destination):
		log_line("[not allowed] %s: %s → %s (%s)" % [card.title, source.name, destination.name, _refusal_reason(source, destination)])
		game_log.flush()
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
		report_failure("movable() was used inside a move rule that movable() was checking; that inner check counts as no moves")
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
	var before := Snapshot.take(self)
	var zone := zone_of(card)
	zone.reorder(card, index)
	_remember(before)
	log_line("  %s: position %d in %s (manual order)" % [card.title, index + 1, zone.name])
	game_log.flush()
	state_changed.emit()


## A trigger called from a step runs with that step's card, so it can say "if it's a King, remove it".
func trigger(event: String, card: Card = null) -> void:
	if event in rules.triggers:
		log_line("[trigger] " + event)
		run(rules.triggers[event], card)


## Fires "<zone>_empty"; for a per-player zone with no trigger of its own, fires "<kind>_empty"
## as its owner, so "deck_empty" can say move(discard, deck) for whoever ran out.
func zone_empty(zone: Zone) -> void:
	var own_trigger := zone.name + "_empty"
	if zone.owner == 0 or own_trigger in rules.triggers:
		trigger(own_trigger)
		return
	var turn: Variant = variables[PLAYER_VARIABLE]
	variables[PLAYER_VARIABLE] = zone.owner
	trigger(zone.kind + "_empty")
	variables[PLAYER_VARIABLE] = turn


## Whether any of the copies listed in the zone's "covered_by" still holds a card (Pyramid, TriPeaks).
func covered(zone: Zone) -> bool:
	return zone.covered_by.any(func(number: float) -> bool:
		var covering: Zone = zones.get("%s_%d" % [zone.kind, int(number)])
		return covering != null and not covering.is_empty())


func matching(cards: Array, condition: String) -> Array:
	if condition not in rules.conditions:
		report_failure("No condition named \"%s\" in \"conditions\"" % condition)
		return []
	return cards.filter(func(card: Card) -> bool: return holds(rules.conditions[condition], card))


func current_player() -> int:
	return int(variables.get(PLAYER_VARIABLE, 1))


func next_player() -> int:
	return current_player() % rules.players + 1


func team_of(player: int) -> int:
	for i in rules.teams.size():
		if rules.teams[i] is Array and rules.teams[i].has(float(player)):
			return i + 1
	return 0


func end_turn() -> void:
	set_variable(PLAYER_VARIABLE, next_player())
	trigger("turn_start")


func click(card: Card) -> void:
	var before := Snapshot.take(self)
	var zone := zone_of(card)
	if choosing_cards() and card in choices:
		_select(card)
	elif choosing:
		cancel_choice()
	elif zone.on_click:
		log_line("[click] %s in %s" % [card.title, zone.name])
		run(zone.on_click, card)
	_finish_input(before)


func _select(card: Card) -> void:
	if pick_exact and pick_count == 1:
		_complete_choice(card)
	elif card in selected:
		selected.erase(card)
	elif selected.size() < pick_count:
		selected.append(card)
		if pick_exact and selected.size() == pick_count:
			_complete_choice(selected.duplicate())


func double_click(card: Card) -> void:
	var before := Snapshot.take(self)
	cancel_choice()
	var zone := zone_of(card)
	if zone.on_double_click:
		log_line("[double-click] %s in %s" % [card.title, zone.name])
		run(zone.on_double_click, card)
	_finish_input(before)


func click_empty(zone: Zone) -> void:
	var before := Snapshot.take(self)
	cancel_choice()
	if zone.on_click:
		log_line("[click] " + zone.name)
		run(zone.on_click)
	_finish_input(before)


func undo() -> void:
	if history.is_empty():
		return
	Snapshot.restore(self, history.pop_back())
	while is_bot_turn() and history:
		Snapshot.restore(self, history.pop_back())
	log_line("[undo]")
	game_log.flush()
	variables_changed.emit()
	state_changed.emit()


func is_bot_turn() -> bool:
	return is_bot(current_player())


## Whether the person at this screen may pick up the card: online, only from shared zones and their own.
func may_handle(card: Card) -> bool:
	return not local_seat or sandbox or zone_of(card).owner in [0, local_seat]


## Whether the person at this screen acts now: online, only on their seat's turn; sharing a screen, on any person's.
func is_local_turn() -> bool:
	return current_player() == local_seat if local_seat else not is_bot_turn()


func player_name(player: int) -> String:
	return seat_name(player) + (" (bot)" if is_bot(player) else "")


## The name in "player_names" (online, the name each person joined with).
func seat_name(player: int) -> String:
	return rules.player_names[player - 1] if player >= 1 and player <= rules.player_names.size() else "Player %d" % player


func is_bot(player: int) -> bool:
	return rules.bot.get("players", []).any(func(bot_player: float) -> bool: return int(bot_player) == player)


## Every move the current player could make, for bots. A move is a list of inputs (see apply), e.g.
## pressing an action and then clicking the cards to choose.
func legal_moves() -> Array[Array]:
	if choosing:
		return _choice_moves(choosing, choices, pick_count, pick_exact)
	var moves: Array[Array] = []
	for source: Zone in zones.values():
		for card in _reachable_cards(source):
			for destination: Zone in zones.values():
				if destination != source and not _matching_move(card, source, destination).is_empty():
					moves.append([{type = "drag", card = card.id, to = destination.name}])
		if source.on_click and source.is_empty():
			moves.append([{type = "click_empty", zone = source.name}])
		elif source.on_click:
			for card in _reachable_cards(source):
				moves.append([{type = "click", card = card.id}])
		if source.on_double_click:
			for card in _reachable_cards(source):
				moves.append([{type = "double_click", card = card.id}])
	for action_name: String in rules.actions:
		var action: Dictionary = rules.actions[action_name]
		var only_asked: bool = not action.get("button", true)
		if action.get("human_only", false) or only_asked or not applies(action):
			continue
		var press := {type = "perform", action = action_name}
		if not action.has("choose"):
			moves.append([press])
			continue
		var options := _choice_list(evaluate(action.choose, null))
		var rule := _pick_rule(action, options.size())
		for choice_move in _choice_moves(action_name, options, rule[0], rule[1]):
			moves.append([press] + choice_move)
	return moves


## What a player could pick up: only the top card of a pile.
static func _reachable_cards(zone: Zone) -> Array[Card]:
	if zone.layout == "pile":
		return [zone.cards.back()] if zone.cards else []
	return zone.cards


func _choice_moves(action_name: String, options: Array, pick: int, exactly: bool) -> Array[Array]:
	var moves: Array[Array] = []
	if options.size() < pick:
		return moves
	var clicks := func(chosen_cards: Array) -> Array:
		return chosen_cards.map(func(card: Card) -> Dictionary: return {type = "click", card = card.id})
	if not exactly:
		for size in range(pick + 1):
			for combination: Array in _combinations(options, size):
				if _allows(action_name, combination):
					moves.append(clicks.call(combination) + [{type = "finish"}])
	elif not options.all(func(option: Variant) -> bool: return option is Card):
		for option in options:
			if _allows(action_name, option):
				moves.append([{type = "choose", option = option}])
	elif pick == 1:
		for card: Card in options:
			if _allows(action_name, card):
				moves.append(clicks.call([card]))
	else:
		for combination: Array in _combinations(options, pick):
			if _allows(action_name, combination):
				moves.append(clicks.call(combination))
	return moves


static func _combinations(items: Array, size: int) -> Array:
	if size == 0:
		return [[]]
	if items.size() < size:
		return []
	var rest := items.slice(1)
	var with_first := _combinations(rest, size - 1).map(func(combination: Array) -> Array: return [items[0]] + combination)
	return with_first + _combinations(rest, size)


func set_quiet(quiet: bool) -> void:
	set_block_signals(quiet)
	for zone: Zone in zones.values():
		zone.set_block_signals(quiet)


func _remember(before: Dictionary) -> void:
	if nesting == 0 and before != Snapshot.take(self):
		history.append(before)


func run(steps: Array, card: Card = null) -> bool:
	if nesting >= MAX_NESTING:
		report_failure("Stopped: steps triggered each other %d levels deep (endless loop?)" % MAX_NESTING)
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
		var result: Variant = evaluate(text, card)
		game_log.flush_items()
		if evaluation_failed:
			return false
		if step is Dictionary:
			var branch := "then" if result else "else"
			log_line("  if %s → %s" % [text, branch])
			if not run(step.get(branch, []), card):
				return false
	return true


func _run_loop(loop: Dictionary, card: Card) -> bool:
	var items: Variant = evaluate(loop.in, card)
	if evaluation_failed:
		return false
	var item_list: Variant = _loop_items(items)
	if item_list == null:
		report_failure("Can't loop over \"%s\": it should be a zone, a list, or a number" % loop.in)
		return false
	var name: String = loop.for
	var number_name := name + LOOP_NUMBER_SUFFIX
	var outer_values := [_loop_value(name), _loop_value(number_name)]
	log_line("  for %s in %s (%d)" % [name, loop.in, item_list.size()])
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
		if not holds(loop.while, card):
			log_line("  while %s: repeated %d times" % [loop.while, repeats])
			return not evaluation_failed
		if not run(loop.get("do", []), card):
			return false
	report_failure("Stopped: \"while %s\" was still true after %d repeats" % [loop.while, MAX_REPEATS])
	return false


func _loop_items(items: Variant) -> Variant:
	if items is Zone:
		return items.cards.duplicate()
	if items is Array:
		return items.duplicate()
	if items is int or items is float:
		return range(1, int(items) + 1)
	return null


func holds(condition: String, card: Card = null) -> bool:
	if condition.is_empty():
		return true
	var result: Variant = evaluate(condition, card)
	return not evaluation_failed and (true if result else false)


func _matching_move(card: Card, source: Zone, destination: Zone) -> Dictionary:
	return _during_move(source, destination, func() -> Dictionary:
		for rule: Dictionary in _rules_between(source, destination):
			if applies(rule, card):
				return rule
		return {})


func _rules_between(source: Zone, destination: Zone) -> Array:
	return rules.moves.filter(func(rule: Dictionary) -> bool:
		return _names(rule.get("from"), source) and _names(rule.get("to"), destination))


func _names(rule_zone: Variant, zone: Zone) -> bool:
	if rule_zone is Array:
		return rule_zone.any(func(each: Variant) -> bool: return _names(each, zone))
	return rule_zone == null or _resolve(rule_zone) == zone.name or (rule_zone in zone_groups and zone in zone_groups[rule_zone])


## Whether a move rule or action applies now: in its "phase" (one or a list), if it has one, and while "when" holds.
func applies(entry: Dictionary, card: Card = null) -> bool:
	if entry.has(PHASE_KEY):
		var phases: Array = entry.phase if entry.phase is Array else [entry.phase]
		if variables.get(PHASE_KEY) not in phases:
			return false
	return holds(entry.get("when", ""), card)


static func _requirements(entry: Dictionary) -> String:
	var parts: Array[String] = []
	if entry.has(PHASE_KEY):
		parts.append("phase is " + (entry.phase if entry.phase is String else " or ".join(entry.phase)))
	if entry.get("when", ""):
		parts.append(entry.when)
	return " and ".join(parts) if parts else "always"


func _refusal_reason(source: Zone, destination: Zone) -> String:
	var conditions: Array = _rules_between(source, destination).map(_requirements)
	if conditions.is_empty():
		return "no move rule allows this"
	return "requires: " + " OR ".join(conditions)


func status_values() -> Dictionary:
	var values := {}
	for label in rules.status:
		values[label] = format_value(evaluate(rules.status[label], null))
	return values


func evaluate(text: String, card: Card = null) -> Variant:
	var failure := {}
	var result: Variant = _evaluate_logic(logic.get(text, text), card, failure)
	evaluation_failed = not failure.is_empty()
	if evaluation_failed:
		report_failure("Step failed: \"%s\": %s" % [text, failure.message])
	return result


## "and"/"or" stop as soon as the answer is known, so "count(to) > 0 and top(to).data.x" is safe on an empty zone.
func _evaluate_logic(node: Variant, card: Card, failure: Dictionary) -> Variant:
	if node is String:
		var expression := expressions[node]
		var result: Variant = expression.execute(_input_values(card), Actions.new(self, card))
		if expression.has_execute_failed():
			failure.message = StepCompiler.failure_message(expression)
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


func move(card: Card, destination: Zone) -> void:
	var source := zone_of(card)
	var seen_before := shows_face(card)
	source.remove(card)
	destination.add(card)
	game_log.item("%s → %s" % [source.name, destination.name], card.title if seen_before or shows_face(card) else "")
	run(card_effect(card, destination.kind), card)


func mark(card: Card, key: String, value: Variant) -> void:
	card.marks[key] = value
	game_log.item("mark %s = %s" % [key, format_value(value)], describe(card))


func unmark(card: Card, key: String) -> void:
	if card.marks.erase(key):
		game_log.item("unmark " + key, describe(card))


func flip(card: Card) -> void:
	card.face_up = not card.face_up
	var zone := zone_of(card)
	var seen := shows_face(card) or (not card.face_up and zone.face != Zone.FACE_DOWN)
	game_log.item("face %s in %s" % ["up" if card.face_up else "down", zone.name], card.title if seen else "")
	zone.changed.emit(zone)


func log_line(text: String) -> void:
	game_log.line(text)


func report_failure(text: String) -> void:
	game_log.flush()
	failed.emit(text)


## Whether this screen shows the card's face.
func shows_face(card: Card) -> bool:
	return can_see(viewer(), card)


## Whether that player can see the card's face: not if it's face down, nor in another player's private zone
## ("private"), nor in their own zone held facing away from them ("others").
func can_see(player: int, card: Card) -> bool:
	if not card.face_up:
		return false
	var zone := zone_of(card)
	match zone.face:
		Zone.FACE_PRIVATE:
			return zone.owner == player
		Zone.FACE_OTHERS:
			return zone.owner != player
	return true


## Who the table is shown to: this screen's seat when playing online; otherwise the only human at a table
## of bots, or whoever's turn it is (sharing one screen).
func viewer() -> int:
	if local_seat:
		return local_seat
	var humans := range(1, rules.players + 1).filter(func(player: int) -> bool: return not is_bot(player))
	return humans[0] if humans.size() == 1 else current_player()


func add_to(variable: String, amount: Variant) -> void:
	var name := _resolve(variable)
	if name not in variables:
		report_failure("Unknown variable \"%s\" (declare it in \"variables\")" % name)
		return
	set_variable(variable, variables[name] + amount)


func set_variable(variable: String, value: Variant) -> void:
	var name := _resolve(variable)
	if name not in variables:
		report_failure("Unknown variable \"%s\" (declare it in \"variables\")" % name)
		return
	variables[name] = value
	log_line("  %s = %s" % [name, "[%s]" % describe(value) if value is Array else describe(value)])
	variables_changed.emit()


static func format_value(value: Variant) -> String:
	if value is Card:
		return value.title
	if value is Array:
		return "[%s]" % ", ".join(value.map(format_value))
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


## "hand" -> "hand_2" and so on for the current player. Only the current player changes them,
## so they're worked out once per player rather than for every step.
func aliases() -> Dictionary:
	var player := current_player()
	if player in aliases_by_player:
		return aliases_by_player[player]
	var aliases := {}
	for name in per_player_names:
		aliases[name] = "%s_%d" % [name, player]
		aliases[OPPONENT_PREFIX + name] = "%s_%d" % [name, next_player()]
	for name in per_team_names:
		aliases[name] = "%s_%d" % [name, team_of(player)]
		aliases[OPPONENT_PREFIX + name] = "%s_%d" % [name, team_of(next_player())]
	aliases_by_player[player] = aliases
	return aliases


## "team_hand" is the hands of everyone on the current player's team.
func _team_groups() -> Dictionary:
	var groups := {}
	if rules.teams.is_empty():
		return groups
	var team: int = team_of(current_player())
	if team in team_groups_by_team:
		return team_groups_by_team[team]
	team_groups_by_team[team] = groups
	for group_name: String in zone_groups:
		if group_name.begins_with(ALL_PREFIX):
			groups[TEAM_PREFIX + group_name.trim_prefix(ALL_PREFIX)] = zone_groups[group_name].filter(func(zone: Zone) -> bool:
				return team_of(zone.owner) == team)
	return groups


func _resolve(name: String) -> String:
	return aliases().get(name, name)


func input_names() -> PackedStringArray:
	return PackedStringArray(zones.keys() + variables.keys() + aliases().keys() + zone_groups.keys() + _team_groups().keys() + loop_names \
		+ [CARD_VARIABLE, FROM_VARIABLE, TO_VARIABLE, CHOICE_VARIABLE, PLAYERS_VARIABLE])


func _input_values(card: Card) -> Array:
	var aliased := aliases().values().map(func(name: String) -> Variant:
		return zones[name] if name in zones else variables[name])
	var looped := loop_names.map(func(name: String) -> Variant: return loop_values.get(name))
	return zones.values() + variables.values() + aliased + zone_groups.values() + _team_groups().values() + looped \
		+ [card, move_from, move_to, chosen, range(1, rules.players + 1)]


func _add_zone(zone: Zone, definition: Dictionary) -> void:
	if definition.has("cards"):
		_load_cards(zone, definition.cards)
	zones[zone.name] = zone


func _copies_of(definition: Dictionary) -> int:
	return rules.players if definition.get("per_player", false) else int(definition.get("copies", 0))


func _load_cards(zone: Zone, path: String) -> void:
	if path in given_files or FileAccess.file_exists(path):
		var deck := DeckLoader.parse_csv(given_files[path]) if path in given_files else DeckLoader.load_csv(path)
		for card in deck:
			card.id = cards.size()
			cards.append(card)
		zone.add_all(deck)
	else:
		errors.append("Deck not found: " + path)


func runcard_effect(card: Card, zone_kind: String) -> void:
	run(card_effect(card, zone_kind), card)


func card_effect(card: Card, zone_kind: String) -> Array:
	var steps: String = card.data.get(CARD_EFFECT_PREFIX + zone_kind, "").strip_edges()
	if steps.begins_with("["):
		var json_steps: Variant = JSON.parse_string(steps)
		return json_steps if json_steps is Array else [steps]
	var step_list := []
	for part in steps.split(CARD_EFFECT_SEPARATOR, false):
		var step := part.strip_edges()
		if rules.triggers.get(step) is Array:
			step_list.append_array(rules.triggers[step])
		else:
			step_list.append(step)
	return step_list


