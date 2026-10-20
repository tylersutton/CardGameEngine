class_name Plays
extends RefCounted
## What players can do: the game's actions, each done by a drag, a click or its toolbar button; which action an
## input is; which actions are offered right now (offer(...)); and every legal input, for bots. Game carries the
## inputs out.

## Ways to do an action other than its toolbar button.
const DRAG_KEY := "drag"
const CLICK_KEY := "click"
const DOUBLE_CLICK_KEY := "double_click"

## Held weakly: the game holds its plays, and a reference each way would keep both alive forever.
var game: Game:
	get: return game_reference.get_ref()
var game_reference: WeakRef
## The actions a waiting offer(...) lets the current player do; and every action named in an offer anywhere
## (filled in by StepCompiler), which can only be done while offered. See is_offered_now.
var offered: Array = []
var offered_actions := {}
## The actions done by dragging (with "drag"), and by clicking or double-clicking (by name, or true on a drag
## action), in the file's order.
var drag_action_names: Array[String] = []
var click_action_names := {CLICK_KEY: [], DOUBLE_CLICK_KEY: []}
## Per current player, each zone's drag routes (see _drag_routes), which only change with the names' meanings.
var drag_routes_by_player: Dictionary = {}


func _init(game_to_serve: Game, actions: Dictionary) -> void:
	game_reference = weakref(game_to_serve)
	for action_name: String in actions:
		if actions[action_name].get(DRAG_KEY) is Dictionary:
			drag_action_names.append(action_name)
		for click_key in click_action_names:
			if actions[action_name].has(click_key):
				click_action_names[click_key].append(action_name)


static func is_card_action(action: Dictionary) -> bool:
	return action.has(DRAG_KEY) or action.has(CLICK_KEY) or action.has(DOUBLE_CLICK_KEY)


## Toolbar buttons: actions done by pressing them (not by a drag or click, unless they also say "button": true).
func shows_button(action_name: String) -> bool:
	var action: Dictionary = game.rules.actions[action_name]
	return action.get("button", not is_card_action(action))


## Whether a toolbar button can be pressed now. A drag or click action with "button": true needs a card it
## could be done with, which pressing the button then asks for.
func can_perform(action_name: String) -> bool:
	if game.sandbox:
		return true
	if game.choice.must_be_made() or not is_offered_now(action_name):
		return false
	if is_card_action(game.rules.actions[action_name]):
		return not playable_cards(action_name).is_empty()
	return game.holds(game.rules.actions[action_name].get("when", ""))


## Actions named in an offer(...) can only be done while an offer naming them is waiting; the others whenever
## their "when" holds.
func is_offered_now(action_name: String) -> bool:
	return action_name not in offered_actions or action_name in offered


## Whether the current player could do the action now if it were offered, by whichever means it has: what
## offer(...) checks before waiting.
func can_do(action_name: String) -> bool:
	if not game.rules.actions.has(action_name):
		return false
	var action: Dictionary = game.rules.actions[action_name]
	if is_card_action(action):
		return not playable_cards(action_name).is_empty() or _clickable_empty_zones(action_name).size() > 0
	return game.holds(action.get("when", ""))


## An offer(...) step is waiting: the current player may do one of these actions (and anything not offered).
func open_offer(request: Dictionary) -> void:
	offered = request.actions
	game.log_line("  %s may: %s" % [game.player_name(game.current_player()), ", ".join(offered)])


## The waiting offer gets its answer (the action being done), so the steps carry on once the action's steps run.
func accept_offer(action_name: String) -> void:
	if action_name in offered:
		offered = []
		game.runner.accept(action_name)


func can_move(card: Card, destination: Zone) -> bool:
	return game.sandbox or drag_action_names.is_empty() or not drag_action(card, game.zone_of(card), destination).is_empty()


## The first drag action (in the file's order) that allows this card from source to destination now, or "".
func drag_action(card: Card, source: Zone, destination: Zone) -> String:
	return _first_allowed(_drag_actions_between(source, destination), card, source, destination)


## What clicking (or double-clicking) the card does, as {action} (an action clicked on that zone) or
## {action, destination} (a drag action that says "click": true, dragging the card to its first allowed zone).
## The first action in the file that allows it now wins; {} if none.
func click_action(zone: Zone, card: Card, click_key: String) -> Dictionary:
	for action_name: String in click_action_names[click_key]:
		var action: Dictionary = game.rules.actions[action_name]
		if not is_offered_now(action_name) or not _binds(action, click_key, zone):
			continue
		if action.has(DRAG_KEY):
			var destination := destination_for(action_name, card)
			if destination:
				return {action = action_name, destination = destination}
		elif game.with_clicked_zone(zone, func() -> bool: return game.holds(action.get("when", ""), card)):
			return {action = action_name}
	return {}


## Whether double-clicking a card in the zone does something (otherwise two quick clicks are two clicks).
func has_double_click(zone: Zone) -> bool:
	return click_action_names[DOUBLE_CLICK_KEY].any(func(action_name: String) -> bool: return _binds(game.rules.actions[action_name], DOUBLE_CLICK_KEY, zone))


## The first zone a drag action would take the card to now, or null.
func destination_for(action_name: String, card: Card) -> Zone:
	if card == null:
		return null
	var source := game.zone_of(card)
	var drag_zones: Dictionary = game.rules.actions[action_name][DRAG_KEY]
	if not names(drag_zones.get("from"), source):
		return null
	for destination: Zone in game.zones.values():
		if destination != source and names(drag_zones.get("to"), destination) \
				and game.with_move(source, destination, func() -> bool: return game.holds(game.rules.actions[action_name].get("when", ""), card)):
			return destination
	return null


## The cards the action could be done with now: dragged somewhere it allows, or clicked.
func playable_cards(action_name: String) -> Array:
	var action: Dictionary = game.rules.actions[action_name]
	var playable := []
	for zone: Zone in game.zones.values():
		for card in reachable_cards(zone):
			var can_click: bool = not action.has(DRAG_KEY) and (_binds(action, CLICK_KEY, zone) or _binds(action, DOUBLE_CLICK_KEY, zone)) \
				and game.with_clicked_zone(zone, func() -> bool: return game.holds(action.get("when", ""), card))
			if can_click or action.has(DRAG_KEY) and destination_for(action_name, card) != null:
				playable.append(card)
	return playable


## Why a drag isn't allowed, for the log: each drag action between the zones and what it requires.
func refusal_reason(source: Zone, destination: Zone) -> String:
	var reasons: Array = _drag_actions_between(source, destination).map(func(action_name: String) -> String:
		if not is_offered_now(action_name):
			return "%s: not offered now" % action_name
		return "%s: %s" % [action_name, game.rules.actions[action_name].get("when", "always")])
	if reasons.is_empty():
		return "no action drags from %s to %s" % [source.name, destination.name]
	return "requires " + " OR ".join(reasons)


## Every move the current player could make, for bots. A move is a list of inputs (see Game.apply), e.g.
## pressing an action and then clicking the cards to choose.
func legal_moves() -> Array[Array]:
	if game.choice.is_open():
		return game.choice.moves()
	var moves: Array[Array] = []
	for source: Zone in game.zones.values():
		var routes := _drag_routes(source)
		for card in reachable_cards(source):
			for route: Array in routes:
				if not _first_allowed(route[1], card, source, route[0]).is_empty():
					moves.append([{type = "drag", card = card.id, to = route[0].name}])
			if not click_action(source, card, CLICK_KEY).is_empty():
				moves.append([{type = "click", card = card.id}])
			if not click_action(source, card, DOUBLE_CLICK_KEY).is_empty():
				moves.append([{type = "double_click", card = card.id}])
		if source.is_empty() and not click_action(source, null, CLICK_KEY).is_empty():
			moves.append([{type = "click_empty", zone = source.name}])
	for action_name: String in game.rules.actions:
		var action: Dictionary = game.rules.actions[action_name]
		if is_card_action(action) or not shows_button(action_name) or action.get("human_only", false) or not can_perform(action_name):
			continue
		var press := {type = "perform", action = action_name}
		if not action.has("choose"):
			moves.append([press])
			continue
		for choice_move in game.choice.moves_for_action(action_name):
			moves.append([press] + choice_move)
	return moves


## What a player could pick up: only the top card of a pile.
static func reachable_cards(zone: Zone) -> Array[Card]:
	if zone.layout == "pile":
		return [zone.cards.back()] if zone.cards else []
	return zone.cards


## Whether a drag's "from" or "to" (or a click's zones) names the zone: by name, as a per-player name like hand,
## as a group of copies, in a list, or (left out) any zone.
func names(rule_zone: Variant, zone: Zone) -> bool:
	if rule_zone is Array:
		return rule_zone.any(func(each: Variant) -> bool: return names(each, zone))
	return rule_zone == null or game.resolve(rule_zone) == zone.name or (rule_zone in game.zone_groups and zone in game.zone_groups[rule_zone])


func clear_routes() -> void:
	drag_routes_by_player.clear()


func _first_allowed(action_names: Array, card: Card, source: Zone, destination: Zone) -> String:
	return game.with_move(source, destination, func() -> String:
		for action_name: String in action_names:
			if is_offered_now(action_name) and game.holds(game.rules.actions[action_name].get("when", ""), card):
				return action_name
		return "")


## Where cards could be dragged from this zone: [destination, the drag actions between them] for each
## destination some drag action names (worked out once for all the zone's cards, for bots' legal moves).
func _drag_routes(source: Zone) -> Array:
	var known: Dictionary = drag_routes_by_player.get_or_add(game.current_player(), {})
	if source.name in known:
		return known[source.name]
	var routes := []
	for destination: Zone in game.zones.values():
		if destination != source:
			var action_names := _drag_actions_between(source, destination)
			if action_names:
				routes.append([destination, action_names])
	known[source.name] = routes
	return routes


func _drag_actions_between(source: Zone, destination: Zone) -> Array:
	return drag_action_names.filter(func(action_name: String) -> bool:
		var drag_zones: Dictionary = game.rules.actions[action_name][DRAG_KEY]
		return names(drag_zones.get("from"), source) and names(drag_zones.get("to"), destination))


## Whether a click (or double-click) on the zone is one of the action's: its "click" names the zone, or it's a
## drag from the zone with "click": true.
func _binds(action: Dictionary, click_key: String, zone: Zone) -> bool:
	var binding: Variant = action.get(click_key)
	if binding is bool:
		return binding and action.has(DRAG_KEY) and names(action[DRAG_KEY].get("from"), zone)
	return binding != null and names(binding, zone)


## Empty zones whose click the action is, while its "when" holds with no card.
func _clickable_empty_zones(action_name: String) -> Array:
	var action: Dictionary = game.rules.actions[action_name]
	return game.zones.values().filter(func(zone: Zone) -> bool:
		return zone.is_empty() and not action.has(DRAG_KEY) and _binds(action, CLICK_KEY, zone) \
			and game.with_clicked_zone(zone, func() -> bool: return game.holds(action.get("when", ""), null)))
