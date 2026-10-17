class_name Actions
extends RefCounted

var game: Game


func _init(game_to_control: Game) -> void:
	game = game_to_control


func draw(from: Zone, to: Zone, amount: int = 1) -> void:
	for i in amount:
		if from.is_empty():
			game.trigger(from.name + "_empty")
		if from.is_empty():
			return
		game.move(from.cards.back(), to)


func deal(from: Zone, to: Variant, amount: int = 1) -> void:
	var hands := zones_in(to)
	for round in amount:
		for hand: Zone in hands:
			draw(from, hand)


func move(cards: Variant, to: Zone) -> void:
	for card: Card in cards_in(cards):
		game.move(card, to)


func try_move(card: Card, to: Variant) -> void:
	if not card:
		return
	if to is Zone:
		game.drag(card, to)
		return
	for destination: Zone in to:
		if destination != game.zone_of(card) and game.can_move(card, destination):
			game.drag(card, destination)
			return
	game.log_line("[not allowed] %s: no move rule allows any of %s" % [card.title, ", ".join(to.map(func(zone: Zone) -> String: return zone.name))])


func all(zone: Zone) -> Array[Card]:
	return zone.cards.duplicate()


func flip(cards: Variant) -> void:
	for card: Card in cards_in(cards):
		game.flip(card)


func shuffle(zone: Zone) -> void:
	zone.shuffle()
	game.log_line("  shuffle " + zone.name)


func sort(zone: Zone, columns: Array) -> void:
	var column_names: Array[String] = []
	column_names.assign(columns)
	zone.keep_sorted_by(column_names)
	game.log_line("  sort %s by %s" % [zone.name, ", ".join(column_names)] if column_names else "  %s: manual order" % zone.name)


func assign(variable: String, value: Variant) -> void:
	game.set_variable(variable, value)


func end_turn() -> void:
	game.end_turn()


func ask(action: String) -> void:
	game.ask(action)


func next_player(seats: int = 1) -> int:
	return posmod(game.current_player() - 1 + seats, game.rules.players) + 1


func zone(kind: String, player: int) -> Zone:
	var name := "%s_%d" % [kind, player]
	if name not in game.zones:
		game.failed.emit("No zone \"%s\"" % name)
		return null
	return game.zones[name]


func numbers(low: int, high: int) -> Array:
	return range(low, high + 1)


func trigger(event: String) -> void:
	if event in game.rules.triggers:
		game.trigger(event)
	else:
		game.failed.emit("No trigger named \"%s\" in \"triggers\"" % event)


func count(cards: Variant) -> int:
	return cards_in(cards).size()


func top(cards: Variant) -> Card:
	var card_list := cards_in(cards)
	return card_list.back() if card_list else null


func sum(cards: Variant, column: String) -> float:
	var total := 0.0
	for card: Card in cards_in(cards):
		total += number(card, column)
	return total


func number(card: Card, column: String) -> float:
	return card.data.get(column, "0").to_float() if card else 0.0


func text(card: Card, column: String) -> String:
	return card.data.get(column, "") if card else ""


func highest(cards: Variant, column: String) -> Card:
	var best: Card = null
	for candidate: Card in cards_in(cards):
		if best == null or number(candidate, column) > number(best, column):
			best = candidate
	return best


func lowest(cards: Variant, column: String) -> Card:
	var best: Card = null
	for candidate: Card in cards_in(cards):
		if best == null or number(candidate, column) < number(best, column):
			best = candidate
	return best


func owner(card: Card) -> int:
	return game.zone_of(card).owner if card else 0


func movable(cards: Variant, to: Variant) -> Array:
	return game.movable(cards_in(cards), zones_in(to))


func face_down(cards: Variant) -> Array:
	return cards_in(cards).filter(func(card: Card) -> bool: return not card.face_up)


func without(cards: Variant, removed: Variant) -> Array:
	var removed_cards := cards_in(removed)
	return cards_in(cards).filter(func(card: Card) -> bool: return card not in removed_cards)


func where(cards: Variant, column: String, value: Variant) -> Array:
	return cards_in(cards).filter(func(card: Card) -> bool: return card.data.get(column) == Game.format_value(value))


func melds(cards: Variant) -> Array:
	return game.meld_rules.best(cards_in(cards))


func deadwood(cards: Variant) -> Array:
	return game.meld_rules.deadwood(cards_in(cards))


func layoff(cards: Variant, onto_melds: Array) -> Array:
	return game.meld_rules.layoff(cards_in(cards), onto_melds)


static func zones_in(source: Variant) -> Array:
	if source is Zone:
		return [source]
	var flattened := []
	for item in source:
		flattened.append_array(zones_in(item))
	return flattened


static func cards_in(source: Variant) -> Array:
	if source == null:
		return []
	if source is Zone:
		return source.cards.duplicate()
	if source is Card:
		return [source]
	var flattened := []
	for item in source:
		flattened.append_array(cards_in(item))
	return flattened
