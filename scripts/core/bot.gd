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
	if not game.is_bot_turn():
		return false
	if game.current_player() != turn_player or game.history.size() != history_after_last_input:
		turn_player = game.current_player()
		inputs_this_turn = 0
	if inputs_this_turn >= MAX_INPUTS_PER_TURN:
		game.report_failure("Bot %d stopped after %d inputs this turn (is \"prefer\" pushing it in circles?)" % [turn_player, MAX_INPUTS_PER_TURN])
		return false
	var best_move: Array = []
	var best_value := -INF
	var deals := _deals()
	var prefer: String = game.rules.bot.prefer
	if game.choosing:
		prefer = game.rules.actions[game.choosing].get("prefer", prefer)
	for move in game.legal_moves():
		var value: Variant = game.try_out(move, prefer, deals)
		if (value is float or value is int) and value > best_value:
			best_move = move
			best_value = value
	if best_move.is_empty():
		return false
	inputs_this_turn += 1
	game.log_line("[bot %d]" % turn_player)
	game.apply_move(best_move, turn_player)
	history_after_last_input = game.history.size()
	return true


## Random identities for the cards this bot can't see, shared by every input it compares.
## Its own face-down cards are only shuffled among themselves: it knows what it owns, just not the order.
## (Cards held facing away from it, face "others", could be anything it can't see.)
func _deals() -> Array:
	var player := game.current_player()
	var hidden := game.hidden_from(player)
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
