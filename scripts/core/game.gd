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
const CLICKED_ZONE_VARIABLE := "clicked_zone"
const PLAYER_VARIABLE := "player"
const OPPONENT_PREFIX := "opponent_"
const TEAM_PREFIX := "team_"
const CARD_EFFECT_PREFIX := "on_"
const HIDDEN_CARD := "a hidden card"
const CARD_EFFECT_SEPARATOR := ";"
const LOOP_NUMBER_SUFFIX := "_number"
const CHOOSE_PROMPT := "Choose"
## Ways to do an action other than its toolbar button.
const DRAG_KEY := "drag"
const CLICK_KEY := "click"
const DOUBLE_CLICK_KEY := "double_click"


var rules: GameRules
var rules_path: String
var match_seed: int
var rng := RandomNumberGenerator.new()
var cards: Array[Card] = []
var inputs: Array[Dictionary] = []
var local_seat := 0
var started := false
var given_files := {}
var zones: Dictionary[String, Zone] = {}
var variables: Dictionary = {}
var per_player_names: Array[String] = []
var per_team_names: Array[String] = []
var aliases_by_player: Dictionary = {}
var team_groups_by_team: Dictionary = {}
var zone_groups: Dictionary[String, Array] = {}
var meld_rules: Melds
## Each step's text, parsed by StepParser.
var step_trees: Dictionary[String, Dictionary] = {}
var evaluation_failed := false
var errors: Array[String] = []
var sandbox := false
var runner: StepRunner
## The choice being made: what it's called (an action's name, or choose(...)'s prompt), the action whose steps
## run once it's made ("" for a choose(...) step, which carries on instead), and what can be picked.
var choosing := ""
var choice_action := ""
var choices: Array = []
var pick_count := 1
var pick_exact := true
var selected: Array[Card] = []
var chosen: Variant
var asked: Array[Dictionary] = []
var choice_asked := false
var return_player := 0
var performing_asked := false
## The actions a waiting offer(...) lets the current player do; and every action named in an offer anywhere
## (filled in by StepCompiler), which can only be done while offered. See is_offered_now.
var offered: Array = []
var offered_actions := {}
## Set by end_game(): no more turns, and nothing waits for an offer.
var over := false
var history: Array[Dictionary] = []
var game_log := GameLog.new()
var move_from: Zone
var move_to: Zone
var clicked_zone: Zone
var checking_moves := false
var checked_itself := false
## Names that steps remember without declaring them: loop items (and their _number) and names set with =.
var local_names: Array[String] = []
## The actions done by dragging (with "drag"), and by clicking or double-clicking (by name, or true on a drag
## action), in the file's order.
var drag_action_names: Array[String] = []
var click_action_names := {CLICK_KEY: [], DOUBLE_CLICK_KEY: []}


## A seed makes the match repeatable: the same seed and inputs always give the same game.
## files (path -> text) stands in for the game and deck files, e.g. ones sent by an online host.
func _init(path: String, seed := -1, files := {}) -> void:
	runner = StepRunner.new(self)
	rules_path = path
	match_seed = seed if seed >= 0 else randi()
	rng.seed = match_seed
	given_files = files
	rules = GameRules.load_json(rules_path, files)
	if rules.error:
		errors.append(rules.error)
		return
	for action_name: String in rules.actions:
		if rules.actions[action_name].get(DRAG_KEY) is Dictionary:
			drag_action_names.append(action_name)
		for click_key in click_action_names:
			if rules.actions[action_name].has(click_key):
				click_action_names[click_key].append(action_name)
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
	if errors.is_empty():
		_compute_columns()


func start() -> void:
	started = true
	trigger("start")
	game_log.flush()
	_perform_asked()
	state_changed.emit()


## Whether a toolbar button can be pressed now. A drag or click action with "button": true needs a card it
## could be done with, which pressing the button then asks for.
func can_perform(action_name: String) -> bool:
	if sandbox:
		return true
	if waiting_for_choice() or not is_offered_now(action_name):
		return false
	if _is_card_action(rules.actions[action_name]):
		return not playable_cards(action_name).is_empty()
	return holds(rules.actions[action_name].get("when", ""))


## Actions named in an offer(...) can only be done while an offer naming them is waiting; the others whenever
## their "when" holds.
func is_offered_now(action_name: String) -> bool:
	return action_name not in offered_actions or action_name in offered


## Whether the current player could do the action now if it were offered, by whichever means it has: what
## offer(...) checks before waiting.
func can_do(action_name: String) -> bool:
	if not rules.actions.has(action_name):
		return false
	var action: Dictionary = rules.actions[action_name]
	if _is_card_action(action):
		return not playable_cards(action_name).is_empty() or _clickable_empty_zones(action_name).size() > 0
	return holds(action.get("when", ""))


static func _is_card_action(action: Dictionary) -> bool:
	return action.has(DRAG_KEY) or action.has(CLICK_KEY) or action.has(DOUBLE_CLICK_KEY)


## Toolbar buttons: actions done by pressing them (not by a drag or click, unless they also say "button": true).
func shows_button(action_name: String) -> bool:
	var action: Dictionary = rules.actions[action_name]
	return action.get("button", not _is_card_action(action))


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
	elif _is_card_action(action):
		_open_choice(action_name, action_name, playable_cards(action_name), 1, true, performing_asked)
	else:
		_accept_offer(action_name)
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
			# Sent numbers arrive as decimals (4.0), so match options by value; zones are sent by name.
			var matches := choices.filter(func(option: Variant) -> bool: return typeof(option) == typeof(input.option) and option == input.option \
				or (option is int or option is float) and (input.option is int or input.option is float) and option == input.option \
				or option is Zone and option.name == input.option)
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


## What a choice offers: the cards of a zone (or of a mix of zones and cards), the zones of a list of zones (pick
## a pile), or the items of a list of values (pick a color).
static func choice_options(options: Variant) -> Array:
	var offered := []
	if options is Array and options.size() > 0 and options.all(func(option: Variant) -> bool: return option is Zone):
		offered.assign(options)
	elif options is Array and options.any(func(option: Variant) -> bool: return option is not Card and option is not Zone and option is not Array):
		offered.assign(options)
	else:
		# Untyped, so a zone can be looked for among cards (a click on a pile while cards are offered).
		offered.assign(Actions.cards_in(options))
	return offered


## How an option is written on its button: a zone by its label, or its name when the label doesn't tell it apart.
func option_text(option: Variant) -> String:
	if option is Zone:
		var same_label := choices.filter(func(other: Variant) -> bool: return other is Zone and other.label == option.label)
		return option.label if option.label and same_label.size() == 1 else option.name
	return format_value(option)


## An offer(...) step is waiting: the current player may do one of these actions (and anything not offered).
func open_offer(request: Dictionary) -> void:
	offered = request.actions
	log_line("  %s may: %s" % [player_name(current_player()), ", ".join(offered)])


## The waiting offer gets its answer (the action being done), so the steps carry on once the action's steps run.
func _accept_offer(action_name: String) -> void:
	if action_name in offered:
		offered = []
		runner.accept(action_name)


## The game is over: no more turns, and offers give null straight away.
func end_game() -> void:
	over = true
	offered = []
	log_line("[game over]")


## Whether turns should go on: the "turn" steps start again once nothing else is running, asked or being chosen.
func next_turn_due() -> bool:
	return not rules.turn.is_empty() and not over and asked.is_empty() and choosing == ""


## A choose(...) step is waiting: the current player picks, and must (nothing else happens until they do).
## The step's answer is a single option, or for several (exactly or up to so many) a list.
func open_inline_choice(request: Dictionary) -> void:
	_open_choice(request.prompt if request.prompt else CHOOSE_PROMPT, "", request.options, request.amount, request.exact, true)


func finish_choice() -> void:
	if not choosing_cards() or pick_exact:
		return
	var before := Snapshot.take(self)
	_complete_choice(selected.duplicate())
	_finish_input(before)


func _start_choice(action_name: String, action: Dictionary) -> void:
	var options := choice_options(evaluate(action.choose, null))
	var rule := _pick_rule(action, options.size())
	if options.is_empty() or options.size() < rule[0]:
		log_line("  nothing to choose")
		return
	_open_choice(action_name, action_name, options, rule[0], rule[1], performing_asked)


func _open_choice(title: String, action_name: String, options: Array, pick: int, exactly: bool, must: bool) -> void:
	choosing = title
	choice_action = action_name
	choices = options
	pick_count = pick
	pick_exact = exactly
	selected = []
	choice_asked = must
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


## Runs the action's steps with the choice, or carries on the choose(...) step that was waiting for it. The turn
## goes back to whoever had it once those steps are done (or at the end of a choice they wait for in turn).
func _complete_choice(choice: Variant) -> void:
	var action_name := choice_action
	if not _allows(action_name, choice):
		log_line("[not allowed] %s: requires %s" % [describe(choice), rules.actions[action_name].allow])
		return
	var back_to := return_player
	log_line("[%s] chose %s" % [choosing, describe(choice)])
	_clear_choice()
	if action_name and _is_card_action(rules.actions[action_name]):
		_play_card_action(action_name, choice)
	elif action_name:
		_accept_offer(action_name)
		var outer_choice: Variant = chosen
		chosen = choice
		run(rules.actions[action_name].get("steps", []), choice if choice is Card else null)
		chosen = outer_choice
	else:
		runner.answer(choice)
	if back_to and choosing:
		return_player = back_to
	elif back_to:
		set_variable(PLAYER_VARIABLE, back_to)


## "allow" refuses a choice as a whole, e.g. cards that aren't all the same rank.
func _allows(action_name: String, choice: Variant) -> bool:
	var allow: String = rules.actions.get(action_name, {}).get("allow", "")
	if allow.is_empty():
		return true
	var outer_choice: Variant = chosen
	chosen = choice
	var allowed := holds(allow, choice if choice is Card else null)
	chosen = outer_choice
	return allowed


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
	while not runner.running and asked and not choosing:
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
	runner.drive()


func cancel_choice() -> void:
	if choosing and not choice_asked:
		log_line("[cancelled] " + choosing)
		_clear_choice()


func _clear_choice() -> void:
	choosing = ""
	choice_action = ""
	choices = []
	selected = []
	choice_asked = false
	return_player = 0
	state_changed.emit()


## A card dragged onto a zone: done by the first drag action that allows it (see _drag_action). With no drag
## actions in the game at all, any drag is allowed.
func drag(card: Card, destination: Zone, index := -1) -> bool:
	if waiting_for_choice() and not sandbox:
		log_line("[waiting] %s must finish \"%s\" first" % [player_name(current_player()), choosing])
		game_log.flush()
		return false
	cancel_choice()
	var source := zone_of(card)
	var action_name := "" if sandbox else _drag_action(card, source, destination)
	if action_name.is_empty() and not can_move(card, destination):
		log_line("[not allowed] %s: %s → %s (%s)" % [card.title, source.name, destination.name, _refusal_reason(source, destination)])
		game_log.flush()
		return false
	var before := Snapshot.take(self)
	_drag_by(action_name, card, destination, index)
	_finish_input(before)
	return true


func can_move(card: Card, destination: Zone) -> bool:
	return sandbox or drag_action_names.is_empty() or not _drag_action(card, zone_of(card), destination).is_empty()


## Moves the card (and any carried with it), then runs the action's steps; card effects run first, as each card
## arrives. They're all held back until the cards have moved, so an effect that waits for a choice holds up the rest.
func _drag_by(action_name: String, card: Card, destination: Zone, index := -1) -> void:
	if action_name:
		log_line("[action] " + action_name)
		_accept_offer(action_name)
	runner.hold()
	_during_move(zone_of(card), destination, func() -> void:
		for carried_card in carried(card):
			move(carried_card, destination)
		if index >= 0 and destination.sort_columns.is_empty() and card in destination.cards:
			destination.reorder(card, mini(index, destination.cards.size() - 1))
		if action_name:
			run(rules.actions[action_name].get("steps", []), card))
	runner.release()


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
## as its owner, so "deck_empty" can say move(discard, deck) for whoever ran out. It runs at once,
## in the middle of the draw that found the zone empty.
func zone_empty(zone: Zone) -> void:
	var own_trigger := zone.name + "_empty"
	var as_owner := zone.owner != 0 and own_trigger not in rules.triggers
	var event := zone.kind + "_empty" if as_owner else own_trigger
	if event not in rules.triggers:
		return
	log_line("[trigger] " + event)
	var turn: Variant = variables.get(PLAYER_VARIABLE)
	if as_owner:
		variables[PLAYER_VARIABLE] = zone.owner
	runner.run_now(rules.triggers[event], null)
	if as_owner:
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
	elif zone in choices:
		_complete_choice(zone)
	elif choosing:
		cancel_choice()
	else:
		_click_by(_click_action(zone, card, CLICK_KEY), zone, card)
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
	if not waiting_for_choice():
		_click_by(_click_action(zone, card, DOUBLE_CLICK_KEY), zone, card)
	_finish_input(before)


func click_empty(zone: Zone) -> void:
	var before := Snapshot.take(self)
	if zone in choices:
		_complete_choice(zone)
	elif not waiting_for_choice():
		cancel_choice()
		_click_by(_click_action(zone, null, CLICK_KEY), zone, null)
	_finish_input(before)


## A click found an action (see _click_action): a drag action drags the card to its destination; any other
## action runs its steps, with card and clicked_zone.
func _click_by(found: Dictionary, zone: Zone, card: Card) -> void:
	if found.is_empty():
		return
	if found.has("destination"):
		_drag_by(found.action, card, found.destination)
		return
	log_line("[action] " + found.action)
	_accept_offer(found.action)
	_run_clicked(zone, rules.actions[found.action].get("steps", []), card)


## Whether double-clicking a card in the zone does something (otherwise two quick clicks are two clicks).
func has_double_click(zone: Zone) -> bool:
	return click_action_names[DOUBLE_CLICK_KEY].any(func(action_name: String) -> bool: return _binds(rules.actions[action_name], DOUBLE_CLICK_KEY, zone))


func _run_clicked(zone: Zone, steps: Array, card: Card = null) -> void:
	var outer_zone := clicked_zone
	clicked_zone = zone
	run(steps, card)
	clicked_zone = outer_zone


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
		return _choice_moves(choice_action, choices, pick_count, pick_exact)
	var moves: Array[Array] = []
	for source: Zone in zones.values():
		for card in _reachable_cards(source):
			for destination: Zone in zones.values():
				if destination != source and not _drag_action(card, source, destination).is_empty():
					moves.append([{type = "drag", card = card.id, to = destination.name}])
			if not _click_action(source, card, CLICK_KEY).is_empty():
				moves.append([{type = "click", card = card.id}])
			if not _click_action(source, card, DOUBLE_CLICK_KEY).is_empty():
				moves.append([{type = "double_click", card = card.id}])
		if source.is_empty() and not _click_action(source, null, CLICK_KEY).is_empty():
			moves.append([{type = "click_empty", zone = source.name}])
	for action_name: String in rules.actions:
		var action: Dictionary = rules.actions[action_name]
		if _is_card_action(action) or not shows_button(action_name) or action.get("human_only", false) or not can_perform(action_name):
			continue
		var press := {type = "perform", action = action_name}
		if not action.has("choose"):
			moves.append([press])
			continue
		var options := choice_options(evaluate(action.choose, null))
		var rule := _pick_rule(action, options.size())
		for choice_move in _choice_moves(action_name, options, rule[0], rule[1]):
			moves.append([press] + choice_move)
	return moves


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
				moves.append([{type = "choose", option = option.name if option is Zone else option}])
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
	if not runner.running and before != Snapshot.take(self):
		history.append(before)


## Steps started from outside (an input) run at once, up to any choose(...) that waits; steps started by a step
## (a trigger, a card's effect) run right after it. See StepRunner.
func run(steps: Array, card: Card = null) -> void:
	runner.schedule(steps, card)
	runner.drive()


## What "from", "to", "clicked_zone" and "choice" mean for the steps being started, so they keep their meaning
## if those steps wait for a choice.
func move_context() -> Dictionary:
	return {from = move_from, to = move_to, clicked_zone = clicked_zone, choice = chosen}


func set_move_context(context: Dictionary) -> void:
	move_from = context.from
	move_to = context.to
	clicked_zone = context.clicked_zone
	chosen = context.choice


func holds(condition: String, card: Card = null) -> bool:
	if condition.is_empty():
		return true
	var result: Variant = evaluate(condition, card)
	return not evaluation_failed and (true if result else false)


## The first drag action (in the file's order) that allows this card from source to destination now, or "".
func _drag_action(card: Card, source: Zone, destination: Zone) -> String:
	return _during_move(source, destination, func() -> String:
		for action_name in _drag_actions_between(source, destination):
			if is_offered_now(action_name) and holds(rules.actions[action_name].get("when", ""), card):
				return action_name
		return "")


func _drag_actions_between(source: Zone, destination: Zone) -> Array:
	return drag_action_names.filter(func(action_name: String) -> bool:
		var drag_zones: Dictionary = rules.actions[action_name][DRAG_KEY]
		return _names(drag_zones.get("from"), source) and _names(drag_zones.get("to"), destination))


## What clicking (or double-clicking) the card does, as {action} (an action clicked on that zone) or
## {action, destination} (a drag action that says "click": true, dragging the card to its first allowed zone).
## The first action in the file that allows it now wins; {} if none.
func _click_action(zone: Zone, card: Card, click_key: String) -> Dictionary:
	for action_name: String in click_action_names[click_key]:
		var action: Dictionary = rules.actions[action_name]
		if not is_offered_now(action_name) or not _binds(action, click_key, zone):
			continue
		if action.has(DRAG_KEY):
			var destination := _destination_for(action_name, card)
			if destination:
				return {action = action_name, destination = destination}
		elif _clicked_with(zone, func() -> bool: return holds(action.get("when", ""), card)):
			return {action = action_name}
	return {}


## Whether a click (or double-click) on the zone is one of the action's: its "click" names the zone, or it's a
## drag from the zone with "click": true.
func _binds(action: Dictionary, click_key: String, zone: Zone) -> bool:
	var binding: Variant = action.get(click_key)
	if binding is bool:
		return binding and action.has(DRAG_KEY) and _names(action[DRAG_KEY].get("from"), zone)
	return binding != null and _names(binding, zone)


## The first zone a drag action would take the card to now, or null.
func _destination_for(action_name: String, card: Card) -> Zone:
	if card == null:
		return null
	var source := zone_of(card)
	for destination: Zone in zones.values():
		if destination != source and action_name in _drag_actions_between(source, destination) \
				and _during_move(source, destination, func() -> bool: return holds(rules.actions[action_name].get("when", ""), card)):
			return destination
	return null


## The cards the action could be done with now: dragged somewhere it allows, or clicked.
func playable_cards(action_name: String) -> Array:
	var action: Dictionary = rules.actions[action_name]
	var playable := []
	for zone: Zone in zones.values():
		for card in _reachable_cards(zone):
			var can_click: bool = not action.has(DRAG_KEY) and (_binds(action, CLICK_KEY, zone) or _binds(action, DOUBLE_CLICK_KEY, zone)) \
				and _clicked_with(zone, func() -> bool: return holds(action.get("when", ""), card))
			if can_click or action.has(DRAG_KEY) and _destination_for(action_name, card) != null:
				playable.append(card)
	return playable


## Empty zones whose click the action is, while its "when" holds with no card.
func _clickable_empty_zones(action_name: String) -> Array:
	var action: Dictionary = rules.actions[action_name]
	return zones.values().filter(func(zone: Zone) -> bool:
		return zone.is_empty() and not action.has(DRAG_KEY) and _binds(action, CLICK_KEY, zone) \
			and _clicked_with(zone, func() -> bool: return holds(action.get("when", ""), null)))


## The action done with this card from a "button": true press: dragged to its first allowed zone, or clicked.
func _play_card_action(action_name: String, card: Card) -> void:
	var destination := _destination_for(action_name, card)
	if destination:
		_drag_by(action_name, card, destination)
	else:
		_click_by({action = action_name}, zone_of(card), card)


## Works something out with clicked_zone set, as an action's "when" sees it.
func _clicked_with(zone: Zone, work: Callable) -> Variant:
	var outer_zone := clicked_zone
	clicked_zone = zone
	var result: Variant = work.call()
	clicked_zone = outer_zone
	return result


func _names(rule_zone: Variant, zone: Zone) -> bool:
	if rule_zone is Array:
		return rule_zone.any(func(each: Variant) -> bool: return _names(each, zone))
	return rule_zone == null or _resolve(rule_zone) == zone.name or (rule_zone in zone_groups and zone in zone_groups[rule_zone])


func _refusal_reason(source: Zone, destination: Zone) -> String:
	var reasons: Array = _drag_actions_between(source, destination).map(func(action_name: String) -> String:
		if not is_offered_now(action_name):
			return "%s: not offered now" % action_name
		return "%s: %s" % [action_name, rules.actions[action_name].get("when", "always")])
	if reasons.is_empty():
		return "no action drags from %s to %s" % [source.name, destination.name]
	return "requires " + " OR ".join(reasons)


func status_values() -> Dictionary:
	var values := {}
	for label in rules.status:
		values[label] = format_value(evaluate(rules.status[label], null))
	return values


func evaluate(text: String, card: Card = null) -> Variant:
	var evaluator := StepEvaluator.new(self, card)
	var result: Variant = evaluator.evaluate(tree_of(text))
	evaluation_failed = not evaluator.failure.is_empty()
	if evaluation_failed:
		report_failure("Step failed: \"%s\": %s" % [text, evaluator.failure])
	return result


## Steps are parsed when the game loads (StepCompiler); one that wasn't (it had a load error) is parsed now.
func tree_of(text: String) -> Dictionary:
	if text not in step_trees:
		var parsed := StepParser.parse(text)
		step_trees[text] = parsed.get("tree", {kind = "unreadable", error = parsed.get("error", "")})
	return step_trees[text]


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


## A step that fails while the game loads (e.g. working out "columns") is a load error, shown before play starts.
func report_failure(text: String) -> void:
	game_log.flush()
	if not started:
		if text not in errors:
			errors.append(text)
		return
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


## A step's "name = value": a game variable, or else a name the running steps remember.
func assign(name: String, value: Variant) -> void:
	if _resolve(name) in variables:
		set_variable(name, value)
	else:
		runner.set_local(name, value)


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
	if value is Zone:
		return value.name
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
	return PackedStringArray(zones.keys() + variables.keys() + aliases().keys() + zone_groups.keys() + _team_groups().keys() + local_names \
		+ [CARD_VARIABLE, FROM_VARIABLE, TO_VARIABLE, CLICKED_ZONE_VARIABLE, CHOICE_VARIABLE, PLAYERS_VARIABLE])


## What a name in a step stands for right now (except card, which belongs to the step): a zone or variable (hand
## and score are the current player's), a name the running steps remember (a loop's item, pile = ...), a group of
## zones, or what's being moved, clicked or chosen.
func value_of(name: String) -> Variant:
	var resolved := _resolve(name)
	if resolved in zones:
		return zones[resolved]
	if resolved in variables:
		return variables[resolved]
	if name in local_names:
		return runner.local(name) if runner.has_local(name) else null
	match name:
		FROM_VARIABLE:
			return move_from
		TO_VARIABLE:
			return move_to
		CLICKED_ZONE_VARIABLE:
			return clicked_zone
		CHOICE_VARIABLE:
			return chosen
		PLAYERS_VARIABLE:
			return range(1, rules.players + 1)
	if name in zone_groups:
		return zone_groups[name]
	return _team_groups().get(name)


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


## "columns" next to a zone's "cards": columns added or changed for every card as the game loads, each worked
## out from the card (in order, so a later column can use an earlier one), e.g. "points": "min(card.value, 10)".
func _compute_columns() -> void:
	for zone: Zone in zones.values():
		var columns: Dictionary = rules.zones[zone.kind].get("columns", {})
		for column in columns:
			for card in zone.cards:
				card.data[column] = format_value(evaluate(columns[column], card))
			if zone.cards and zone.cards.all(func(card: Card) -> bool: return Card.is_number(card.data[column])):
				zone.cards[0].number_columns[column] = true
			elif zone.cards:
				zone.cards[0].number_columns.erase(column)


func run_card_effect(card: Card, zone_kind: String) -> void:
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


