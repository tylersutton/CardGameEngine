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
## How deep conditions with parameters may call each other (one calling itself would go on forever).
const MAX_CONDITION_DEPTH := 50


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
## The choice being made (and those asked for), and what players can do (actions and offers).
var choice: Choice
var plays: Plays
## What "choice" means in the steps being run: what was just chosen.
var chosen: Variant
## Set by end_game(): no more turns, and nothing waits for an offer.
var over := false
## Who won, as given to end_game(...) (empty: nobody, or not over yet).
var winners: Array = []
## Players out of the game (knock_out): turns skip them.
var knocked_out: Array = []
var history: Array[Dictionary] = []
## Whether inputs are kept for undo: a bot trying inputs out turns it off, as it undoes them itself.
var remembering := true
var game_log := GameLog.new()
var move_from: Zone
var move_to: Zone
var clicked_zone: Zone
var checking_moves := false
var checked_itself := false
## Names that steps remember without declaring them: loop items (and their _number) and names set with =.
var local_names: Array[String] = []


## A seed makes the match repeatable: the same seed and inputs always give the same game.
## files (path -> text) stands in for the game and deck files, e.g. ones sent by an online host.
func _init(path: String, seed := -1, files := {}) -> void:
	runner = StepRunner.new(self)
	choice = Choice.new(self)
	plays = Plays.new(self, {})
	rules_path = path
	match_seed = seed if seed >= 0 else randi()
	rng.seed = match_seed
	given_files = files
	rules = GameRules.load_json(rules_path, files)
	if rules.error:
		errors.append(rules.error)
		return
	plays = Plays.new(self, rules.actions)
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
	choice.perform_asked()
	state_changed.emit()


func perform(action_name: String) -> void:
	if not plays.can_perform(action_name):
		return
	choice.cancel()
	var before := _undo_point()
	var action: Dictionary = rules.actions[action_name]
	log_line("[action] " + action_name)
	if action.has("choose"):
		choice.start_for_action(action_name)
	elif Plays.is_card_action(action):
		choice.open(action_name, action_name, plays.playable_cards(action_name), 1, true, choice.performing_asked)
	else:
		plays.accept_offer(action_name)
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
			var matches := choice.options.filter(func(option: Variant) -> bool: return typeof(option) == typeof(input.option) and option == input.option \
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


func choose_option(option: Variant) -> void:
	if choice.cards_offered() or option not in choice.options:
		return
	var before := _undo_point()
	choice.complete(option)
	_finish_input(before)


## The game is over: no more turns, and offers give null straight away.
func end_game(game_winners: Array = []) -> void:
	over = true
	plays.offered = []
	winners = game_winners
	log_line("[game over] " + result_text())


## "Winner: You", "Winners: You and North", or "Game over" when end_game() named nobody.
func result_text() -> String:
	if winners.is_empty():
		return "Game over"
	var names := winners.map(func(winner: int) -> String: return seat_name(winner))
	return ("Winner: " if winners.size() == 1 else "Winners: ") + " and ".join(names)


func knock_out(players_out: Array) -> void:
	for player: int in players_out:
		if player not in knocked_out:
			knocked_out.append(player)
			log_line("  %s is out" % player_name(player))
	clear_player_caches()


func bring_back(players_back: Array) -> void:
	for player: int in players_back:
		knocked_out.erase(player)
	clear_player_caches()


func players_left() -> Array:
	return range(1, rules.players + 1).filter(func(player: int) -> bool: return player not in knocked_out)


## The player that many seats on from this one (back, if negative), skipping players knocked out.
func seat_after(player: int, seats: int) -> int:
	var left := players_left()
	if left.is_empty():
		return player
	var step := 1 if seats >= 0 else -1
	var seat := player
	for i in absi(seats):
		seat = posmod(seat - 1 + step, rules.players) + 1
		while seat not in left:
			seat = posmod(seat - 1 + step, rules.players) + 1
	return seat


## Each player's value of a player variable or condition, worked out as if it were their turn: for most(...),
## players_with_most(...) and players_where(...).
func player_values(name: String) -> Array:
	var turn: Variant = variables.get(PLAYER_VARIABLE)
	var values := []
	for player in range(1, rules.players + 1):
		variables[PLAYER_VARIABLE] = player
		values.append(evaluate(name))
	variables[PLAYER_VARIABLE] = turn
	return values


## Whether turns should go on: the "turn" steps start again once nothing else is running, asked or being chosen.
func next_turn_due() -> bool:
	return not rules.turn.is_empty() and not over and choice.asked.is_empty() and not choice.is_open()


func finish_choice() -> void:
	if not choice.cards_offered() or choice.exactly:
		return
	var before := _undo_point()
	choice.complete(choice.selected.duplicate())
	_finish_input(before)


## Like format_value, but a card the viewer can't see is "a hidden card", so the log doesn't give away hands.
func describe(value: Variant) -> String:
	if value is Card:
		return value.title if shows_face(value) else HIDDEN_CARD
	if value is Array:
		return ", ".join(value.map(describe))
	return format_value(value)


func _finish_input(before: Dictionary) -> void:
	game_log.flush()
	choice.perform_asked()
	_remember(before)
	state_changed.emit()


## A card dragged onto a zone: done by the first drag action that allows it (see Plays.drag_action). With no drag
## actions in the game at all, any drag is allowed.
func drag(card: Card, destination: Zone, index := -1) -> bool:
	if choice.must_be_made() and not sandbox:
		log_line("[waiting] %s must finish \"%s\" first" % [player_name(current_player()), choice.title])
		game_log.flush()
		return false
	choice.cancel()
	var source := zone_of(card)
	var action_name := "" if sandbox else plays.drag_action(card, source, destination)
	if action_name.is_empty() and not plays.can_move(card, destination):
		log_line("[not allowed] %s: %s → %s (%s)" % [card.title, source.name, destination.name, plays.refusal_reason(source, destination)])
		game_log.flush()
		return false
	var before := _undo_point()
	_drag_by(action_name, card, destination, index)
	_finish_input(before)
	return true


## Moves the card (and any carried with it), then runs the action's steps; card effects run first, as each card
## arrives. They're all held back until the cards have moved, so an effect that waits for a choice holds up the rest.
func _drag_by(action_name: String, card: Card, destination: Zone, index := -1) -> void:
	if action_name:
		log_line("[action] " + action_name)
		plays.accept_offer(action_name)
	runner.hold()
	with_move(zone_of(card), destination, func() -> void:
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
			return destination != zone_of(card) and plays.can_move(card, destination)))
	checking_moves = false
	if checked_itself:
		checked_itself = false
		report_failure("movable() was used inside a move rule that movable() was checking; that inner check counts as no moves")
	return result


## The card plus, in zones that drag stacks, every card on top of it.
func carried(card: Card) -> Array[Card]:
	var zone := zone_of(card)
	return zone.cards.slice(zone.cards.find(card)) if zone.drags_stack else [card]


func with_move(source: Zone, destination: Zone, action: Callable) -> Variant:
	var outer_move := [move_from, move_to]
	move_from = source
	move_to = destination
	var result: Variant = action.call()
	move_from = outer_move[0]
	move_to = outer_move[1]
	return result


func reorder(card: Card, index: int) -> void:
	var before := _undo_point()
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
	if condition not in rules.conditions or rules.condition_parameters[condition]:
		report_failure("No condition named \"%s\" (without parameters) in \"conditions\"" % condition)
		return []
	return cards.filter(func(card: Card) -> bool: return holds(rules.conditions[condition], card))


func current_player() -> int:
	return int(variables.get(PLAYER_VARIABLE, 1))


func next_player() -> int:
	return seat_after(current_player(), 1)


func team_of(player: int) -> int:
	for i in rules.teams.size():
		if rules.teams[i] is Array and rules.teams[i].has(float(player)):
			return i + 1
	return 0


func end_turn() -> void:
	set_variable(PLAYER_VARIABLE, next_player())
	trigger("turn_start")


func click(card: Card) -> void:
	var before := _undo_point()
	var zone := zone_of(card)
	if choice.cards_offered() and card in choice.options:
		choice.select(card)
	elif zone in choice.options:
		choice.complete(zone)
	elif choice.is_open():
		choice.cancel()
	else:
		_click_by(plays.click_action(zone, card, Plays.CLICK_KEY), zone, card)
	_finish_input(before)


func double_click(card: Card) -> void:
	var before := _undo_point()
	choice.cancel()
	var zone := zone_of(card)
	if not choice.must_be_made():
		_click_by(plays.click_action(zone, card, Plays.DOUBLE_CLICK_KEY), zone, card)
	_finish_input(before)


func click_empty(zone: Zone) -> void:
	var before := _undo_point()
	if zone in choice.options:
		choice.complete(zone)
	elif not choice.must_be_made():
		choice.cancel()
		_click_by(plays.click_action(zone, null, Plays.CLICK_KEY), zone, null)
	_finish_input(before)


## A click found an action (see Plays.click_action): a drag action drags the card to its destination; any other
## action runs its steps, with card and clicked_zone.
func _click_by(found: Dictionary, zone: Zone, card: Card) -> void:
	if found.is_empty():
		return
	if found.has("destination"):
		_drag_by(found.action, card, found.destination)
		return
	log_line("[action] " + found.action)
	plays.accept_offer(found.action)
	_run_clicked(zone, rules.actions[found.action].get("steps", []), card)


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


func set_quiet(quiet: bool) -> void:
	set_block_signals(quiet)
	for zone: Zone in zones.values():
		zone.set_block_signals(quiet)


## The state before an input, to go back to with undo; nothing while a bot tries inputs out (see remembering).
func _undo_point() -> Dictionary:
	return Snapshot.take(self) if remembering else {}


func _remember(before: Dictionary) -> void:
	if remembering and not runner.running and before != Snapshot.take(self):
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


## What per-player names mean changes when players are knocked out (opponent_ skips them), so the answers worked
## out from them are forgotten.
func clear_player_caches() -> void:
	aliases_by_player.clear()
	plays.clear_routes()


## The action done with this card from a "button": true press: dragged to its first allowed zone, or clicked.
func play_card_action(action_name: String, card: Card) -> void:
	var destination := plays.destination_for(action_name, card)
	if destination:
		_drag_by(action_name, card, destination)
	else:
		_click_by({action = action_name}, zone_of(card), card)


## Works something out with "choice" set to what was chosen, as an action's "allow" and steps see it.
func with_choice(made: Variant, work: Callable) -> Variant:
	var outer_choice: Variant = chosen
	chosen = made
	var result: Variant = work.call()
	chosen = outer_choice
	return result


## Works something out with clicked_zone set, as an action's "when" sees it.
func with_clicked_zone(zone: Zone, work: Callable) -> Variant:
	var outer_zone := clicked_zone
	clicked_zone = zone
	var result: Variant = work.call()
	clicked_zone = outer_zone
	return result


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


## Whether name[n] means player n's (or team n's) zone or variable: hand[2], score[dealer].
func is_per_player(name: String) -> bool:
	return name in per_player_names or name in per_team_names


## Player (or team) number's own zone or variable: the "<name>_<number>" one.
func numbered_name(name: String, number: int) -> String:
	var limit: int = rules.teams.size() if name in per_team_names else rules.players
	if number < 1 or number > limit:
		return ""
	return "%s_%d" % [name, number]


## A step's "name = value": a game variable, or else a name the running steps remember.
func assign(name: String, value: Variant) -> void:
	if resolve(name) in variables:
		set_variable(name, value)
	else:
		runner.set_local(name, value)


func set_variable(variable: String, value: Variant) -> void:
	var name := resolve(variable)
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
	return zones.get(card.zone_name)


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


func resolve(name: String) -> String:
	return aliases().get(name, name)


func input_names() -> PackedStringArray:
	return PackedStringArray(zones.keys() + variables.keys() + aliases().keys() + zone_groups.keys() + _team_groups().keys() + local_names + rules.constants.keys() \
		+ [CARD_VARIABLE, FROM_VARIABLE, TO_VARIABLE, CLICKED_ZONE_VARIABLE, CHOICE_VARIABLE, PLAYERS_VARIABLE])


## What a name in a step stands for right now (except card, which belongs to the step): a zone or variable (hand
## and score are the current player's), a name the running steps remember (a loop's item, pile = ...), a group of
## zones, or what's being moved, clicked or chosen.
func value_of(name: String) -> Variant:
	var resolved := resolve(name)
	if resolved in zones:
		return zones[resolved]
	if resolved in variables:
		return variables[resolved]
	if name in rules.constants:
		return rules.constants[name]
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


