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
		game.failed.emit("Bot %d stopped after %d inputs this turn (is \"prefer\" pushing it in circles?)" % [turn_player, MAX_INPUTS_PER_TURN])
		return false
	var best_input: Callable
	var best_value := -INF
	var deals := _deals()
	for input in game.legal_inputs():
		var value: Variant = game.try_out(input, game.rules.bot.prefer, deals)
		if (value is float or value is int) and value > best_value:
			best_input = input
			best_value = value
	if not best_input.is_valid():
		return false
	inputs_this_turn += 1
	game.log_line("[bot %d]" % turn_player)
	best_input.call()
	history_after_last_input = game.history.size()
	return true


## Random identities for the cards this bot can't see, shared by every input it compares.
func _deals() -> Array:
	var hidden := game.hidden_from(game.current_player())
	if game.rules.bot.get("peek", false) or hidden.is_empty():
		return []
	var deals := []
	for sample in int(game.rules.bot.get("samples", DEFAULT_SAMPLES)):
		var identities := hidden.map(func(card: Card) -> Dictionary: return card.data)
		identities.shuffle()
		var deal := {}
		for i in hidden.size():
			deal[hidden[i]] = identities[i]
		deals.append(deal)
	return deals
