class_name Bot
extends RefCounted

const MAX_INPUTS_PER_TURN := 30
const DEFAULT_SAMPLES := 4

var game: Game
var turn_player := 0
var inputs_this_turn := 0
var history_after_last_input := -1


func _init(game_to_play: Game) -> void:
	game = game_to_play


func take_input() -> bool:
	if not game.is_bot_turn() or game.over:
		return false
	if game.current_player() != turn_player or game.history.size() != history_after_last_input:
		turn_player = game.current_player()
		inputs_this_turn = 0
	if inputs_this_turn >= MAX_INPUTS_PER_TURN:
		var hint := " (is \"prefer\" pushing it in circles?)" if game.rules.bot.has("prefer") else ""
		game.report_failure("Bot %d stopped after %d inputs this turn%s" % [turn_player, MAX_INPUTS_PER_TURN, hint])
		return false
	var prefer: String = game.rules.bot.get("prefer", "")
	if game.choice.is_open():
		prefer = game.rules.actions.get(game.choice.action, {}).get("prefer", prefer)
	var best_move := _best_move(prefer) if prefer else _any_move()
	if best_move.is_empty():
		return false
	inputs_this_turn += 1
	game.log_line("[bot %d]" % turn_player)
	game.apply_move(best_move, turn_player)
	history_after_last_input = game.history.size()
	return true


func _best_move(prefer: String) -> Array:
	var best_move: Array = []
	var best_value := -INF
	var deals := _deals()
	var start := _starting_point()
	for move in game.plays.legal_moves():
		var value: Variant = try_out(move, prefer, deals, start)
		if (value is float or value is int) and value > best_value:
			best_move = move
			best_value = value
	return best_move


## With no "prefer", any legal move that changes something (not one that only sorts, say): enough to play a
## prototype through. Picked with the bot's own random numbers, not the match's, so a bot's thinking never changes
## the match (or online copies would differ).
func _any_move() -> Array:
	var moves := game.plays.legal_moves()
	moves.shuffle()
	for move in moves:
		if try_out(move, "0") != null:
			return move
	return []


## Random identities for the cards this bot can't see, shared by every input it compares.
## Its own face-down cards are only shuffled among themselves: it knows what it owns, just not the order.
## (Cards held facing away from it, face "others", could be anything it can't see.)
func _deals() -> Array:
	var player := game.current_player()
	var hidden := hidden_from(player)
	if game.rules.bot.get("peek", false) or hidden.is_empty():
		return []
	var known_set := func(card: Card) -> bool:
		var zone := game.zone_of(card)
		return zone.owner == player and zone.face != Zone.FACE_OTHERS
	var own := hidden.filter(known_set)
	var others := hidden.filter(func(card: Card) -> bool: return not known_set.call(card))
	var deals := []
	for sample in int(game.rules.bot.get("samples", DEFAULT_SAMPLES)):
		var deal := {}
		for group: Array in [own, others]:
			var identities := group.map(func(card: Card) -> Dictionary: return card.data)
			identities.shuffle()
			var most_marked_first := group.duplicate()
			most_marked_first.sort_custom(func(a: Card, b: Card) -> bool: return a.marks.size() > b.marks.size())
			for card: Card in most_marked_first:
				var fitting := identities.find_custom(_fits_marks.bind(card))
				deal[card] = identities.pop_at(maxi(fitting, 0))
		deals.append(deal)
	return deals


## A mark named after a column (a "color" mark on a card with a color column) is what the bot knows about it.
static func _fits_marks(data: Dictionary, card: Card) -> bool:
	for key in card.marks:
		if key in data and data[key] != Game.format_value(card.marks[key]):
			return false
	return true


## Cards whose identity the player can't know: face down (even their own, like their deck's order),
## or in a zone hidden from them.
func hidden_from(player: int) -> Array[Card]:
	var hidden: Array[Card] = []
	for card in game.cards:
		if not game.can_see(player, card):
			hidden.append(card)
	return hidden


## Runs the move silently, measures the result as the current player, then undoes it.
## Returns null if the move changed nothing (e.g. only reordered cards). With deals (card -> data),
## hidden cards are given those identities for each try and the results averaged, so the measure can't peek.
func try_out(move: Array, measure: String, deals: Array = [], start := {}) -> Variant:
	if start.is_empty():
		start = _starting_point()
	if deals.is_empty():
		return _try_once(move, measure, start)
	var total := 0.0
	for deal: Dictionary in deals:
		var real_data := {}
		for card: Card in deal:
			real_data[card] = card.data
			card.data = deal[card]
		var value: Variant = _try_once(move, measure, start)
		for card: Card in real_data:
			card.data = real_data[card]
		if value is not float and value is not int:
			return null
		total += value
	return total / deals.size()


## The game before trying moves out, shared by every move compared from it: to go back to, and to tell whether a
## move changed anything.
func _starting_point() -> Dictionary:
	return {state = Snapshot.take(game), contents = Snapshot.describe(game, false)}


func _try_once(move: Array, measure: String, start: Dictionary) -> Variant:
	var player := game.current_player()
	var before: Dictionary = start.state
	var contents_before: Dictionary = start.contents
	var history_size := game.history.size()
	var was_quiet := game.is_blocking_signals()
	var was_remembering := game.remembering
	game.game_log.flush()
	game.set_quiet(true)
	game.remembering = false
	for input: Dictionary in move:
		game.run_input(input)
	var value: Variant = null
	if game.choice.is_open() and before.choice.title == "" and game.current_player() == player:
		value = _best_choice_value(measure)
	elif Snapshot.describe(game, false) != contents_before or game.current_player() != player:
		if Game.PLAYER_VARIABLE in game.variables:
			game.variables[Game.PLAYER_VARIABLE] = player
		value = game.evaluate(measure)
	Snapshot.restore(game, before)
	game.history.resize(history_size)
	game.game_log.discard()
	game.set_quiet(was_quiet)
	game.remembering = was_remembering
	return value


## When a move opens a choice (e.g. playing Chapel), it's worth what the best way of making that choice is worth.
func _best_choice_value(measure: String) -> Variant:
	var best: Variant = null
	var start := _starting_point()
	for choice_move in game.plays.legal_moves():
		var value: Variant = _try_once(choice_move, measure, start)
		if (value is float or value is int) and (best == null or value > best):
			best = value
	return best
