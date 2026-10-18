class_name Actions
extends RefCounted

var game: Game


func _init(game_to_control: Game) -> void:
	game = game_to_control


func draw(from: Zone, to: Zone, amount: int = 1) -> void:
	for i in amount:
		if from.is_empty():
			game.zone_empty(from)
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


func mark(cards: Variant, key: String, value: Variant) -> void:
	for card: Card in cards_in(cards):
		game.mark(card, key, value)


func unmark(cards: Variant, key: String) -> void:
	for card: Card in cards_in(cards):
		game.unmark(card, key)


func mark_of(card: Card, key: String) -> Variant:
	return card.marks.get(key) if card else null


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


func ask(action: String, player: int = 0) -> void:
	game.ask(action, player)


func ask_all(action: String, then_trigger: String = "") -> void:
	game.ask_all(action, then_trigger)


func next_player(seats: int = 1) -> int:
	return posmod(game.current_player() - 1 + seats, game.rules.players) + 1


func zone(kind: String, player: int) -> Zone:
	var name := "%s_%d" % [kind, player]
	if name not in game.zones:
		game.report_failure("No zone \"%s\"" % name)
		return null
	return game.zones[name]


func numbers(low: int, high: int) -> Array:
	return range(low, high + 1)


func trigger(event: String) -> void:
	if event in game.rules.triggers:
		game.trigger(event)
	else:
		game.report_failure("No trigger named \"%s\" in \"triggers\"" % event)


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


func covered(zone_or_card: Variant) -> bool:
	var zone: Zone = game.zone_of(zone_or_card) if zone_or_card is Card else zone_or_card
	return zone != null and game.covered(zone)


func matching(cards: Variant, condition: String) -> Array:
	return game.matching(cards_in(cards), condition)


func run_effect(card: Card, zone_kind: String) -> void:
	if card:
		game.run_card_effect(card, zone_kind)


func tops(zones: Variant) -> Array:
	return zones_in(zones).filter(func(zone: Zone) -> bool: return not zone.is_empty()).map(func(zone: Zone) -> Card: return zone.cards.back())


func face_down(cards: Variant) -> Array:
	return cards_in(cards).filter(func(card: Card) -> bool: return not card.face_up)


func without(cards: Variant, removed: Variant) -> Array:
	var removed_cards := cards_in(removed)
	return cards_in(cards).filter(func(card: Card) -> bool: return card not in removed_cards)


func where(cards: Variant, column: String, value: Variant) -> Array:
	return cards_in(cards).filter(func(card: Card) -> bool: return card.data.get(column) == Game.format_value(value))


func last(cards: Variant, amount: int) -> Array:
	var card_list := cards_in(cards)
	return card_list.slice(maxi(card_list.size() - amount, 0))


func size(list: Array) -> int:
	return list.size()


func groups(cards: Variant, column: String) -> Array:
	var by_value := Melds.group_by(cards_in(cards), column).values()
	by_value.sort_custom(func(a: Array, b: Array) -> bool: return a.size() > b.size())
	return by_value


func pairs(cards: Variant, column: String) -> Array:
	var found := []
	for group: Array in groups(cards, column):
		for i in group.size():
			for j in range(i + 1, group.size()):
				found.append([group[i], group[j]])
	return found


## Every set of cards whose column adds up to total. Tries every subset, so keep it to small hands.
func sums(cards: Variant, column: String, total: float) -> Array:
	return Melds.subsets(cards_in(cards)).filter(func(subset: Array) -> bool: return subset and sum(subset, column) == total)


## Every longest run of consecutive values, at least length long. A repeated value gives one run per copy
## (3, 4, 4, 5 is two runs), so count(runs(...)) scores runs the cribbage way.
func runs(cards: Variant, column: String, length: int) -> Array:
	var by_value := {}
	for card: Card in cards_in(cards):
		by_value.get_or_add(int(number(card, column)), []).append(card)
	var values := by_value.keys()
	values.sort()
	var found := []
	var stretch := []
	for value in values + [null]:
		if stretch and (value == null or value != stretch.back() + 1):
			if stretch.size() >= length:
				found.append_array(_one_from_each(stretch.map(func(stretch_value: int) -> Array: return by_value[stretch_value])))
			stretch = []
		if value != null:
			stretch.append(value)
	return found


func team(player: int) -> int:
	return game.team_of(player)


func melds(cards: Variant) -> Array:
	return game.meld_rules.best(cards_in(cards))


func deadwood(cards: Variant) -> Array:
	return game.meld_rules.deadwood(cards_in(cards))


func layoff(cards: Variant, onto_melds: Array) -> Array:
	return game.meld_rules.layoff(cards_in(cards), onto_melds)


static func _one_from_each(card_groups: Array) -> Array:
	var picks := [[]]
	for group: Array in card_groups:
		var longer := []
		for pick: Array in picks:
			for card: Card in group:
				longer.append(pick + [card])
		picks = longer
	return picks


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
