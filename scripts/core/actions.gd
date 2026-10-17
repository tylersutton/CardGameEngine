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


func move(cards: Variant, to: Zone) -> void:
	for card: Card in cards_in(cards):
		game.move(card, to)


func try_move(card: Card, to: Zone) -> void:
	if card:
		game.drag(card, to)


func all(zone: Zone) -> Array[Card]:
	return zone.cards.duplicate()


func flip(cards: Variant) -> void:
	for card: Card in cards_in(cards):
		game.flip(card)


func shuffle(zone: Zone) -> void:
	zone.shuffle()
	game.logged.emit("  shuffle " + zone.name)


func sort(zone: Zone, columns: Array) -> void:
	var column_names: Array[String] = []
	column_names.assign(columns)
	zone.keep_sorted_by(column_names)
	game.logged.emit("  sort %s by %s" % [zone.name, ", ".join(column_names)] if column_names else "  %s: manual order" % zone.name)


func assign(variable: String, value: Variant) -> void:
	game.set_variable(variable, value)


func end_turn() -> void:
	game.end_turn()


func trigger(event: String) -> void:
	if event in game.rules.triggers:
		game.trigger(event)
	else:
		game.failed.emit("No trigger named \"%s\" in \"triggers\"" % event)


func count(cards: Variant) -> int:
	return cards_in(cards).size()


func top(zone: Zone) -> Card:
	return zone.cards.back() if zone.cards else null


func sum(cards: Variant, column: String) -> float:
	var total := 0.0
	for card: Card in cards_in(cards):
		total += number(card, column)
	return total


func number(card: Card, column: String) -> float:
	return card.data.get(column, "0").to_float() if card else 0.0


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
