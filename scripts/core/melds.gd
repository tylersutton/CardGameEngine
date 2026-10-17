class_name Melds
extends RefCounted

const DEFAULT_MIN_SIZE := 3

var types: Array
var points_column: String


func _init(config: Dictionary) -> void:
	types = config.get("types", [])
	points_column = config.get("points", "points")


func best(cards: Array) -> Array:
	return _search(cards, 0, _candidates(cards), {}).melds


func deadwood(cards: Array) -> Array:
	var melded := {}
	for meld: Array in best(cards):
		for card in meld:
			melded[card] = true
	return cards.filter(func(card: Card) -> bool: return card not in melded)


func layoff(cards: Array, melds: Array) -> Array:
	var best_choice := []
	var fewest_points := INF
	for choice: Array in _subsets(_extensions(cards, melds)):
		if _extensions(choice, melds).size() != choice.size():
			continue
		var points := _deadwood_points(cards.filter(func(card: Card) -> bool: return card not in choice))
		if points < fewest_points:
			best_choice = choice
			fewest_points = points
	return best_choice


func _deadwood_points(cards: Array) -> float:
	return _search(cards, 0, _candidates(cards), {}).deadwood


func _extensions(cards: Array, melds: Array) -> Array:
	var extended_melds := melds.map(func(meld: Array) -> Array: return meld.duplicate())
	var laid_off := []
	var placed_a_card := true
	while placed_a_card:
		placed_a_card = false
		for card in cards:
			if card in laid_off:
				continue
			for meld: Array in extended_melds:
				if is_meld(meld + [card]):
					meld.append(card)
					laid_off.append(card)
					placed_a_card = true
					break
	return laid_off


func is_meld(cards: Array) -> bool:
	return types.any(func(type: Dictionary) -> bool: return _fits(cards, type))


func _fits(cards: Array, type: Dictionary) -> bool:
	if cards.size() < type.get("min", DEFAULT_MIN_SIZE):
		return false
	var shared_value: String = cards[0].data.get(type.same, "")
	if not cards.all(func(card: Card) -> bool: return card.data.get(type.same, "") == shared_value):
		return false
	if not type.has("sequence"):
		return true
	var values := cards.map(func(card: Card) -> int: return int(card.data.get(type.sequence, "0")))
	values.sort()
	for i in range(1, values.size()):
		if values[i] != values[i - 1] + 1:
			return false
	return true


func _candidates(cards: Array) -> Array:
	var candidates := []
	for type: Dictionary in types:
		for group: Array in _group_by(cards, type.same).values():
			var possible_melds := []
			if type.has("sequence"):
				group.sort_custom(func(a: Card, b: Card) -> bool: return int(a.data[type.sequence]) < int(b.data[type.sequence]))
				for start in group.size():
					for end in range(start + 1, group.size() + 1):
						possible_melds.append(group.slice(start, end))
			else:
				possible_melds = _subsets(group)
			candidates.append_array(possible_melds.filter(func(meld: Array) -> bool: return _fits(meld, type)))
	return candidates


func _search(cards: Array, index: int, candidates: Array, used: Dictionary) -> Dictionary:
	if index == cards.size():
		return {melds = [], deadwood = 0.0}
	var card: Card = cards[index]
	if card in used:
		return _search(cards, index + 1, candidates, used)
	var rest := _search(cards, index + 1, candidates, used)
	var best_result := {melds = rest.melds, deadwood = rest.deadwood + _points(card)}
	for meld: Array in candidates:
		if card not in meld or meld.any(func(other: Card) -> bool: return other in used):
			continue
		for other in meld:
			used[other] = true
		var result := _search(cards, index + 1, candidates, used)
		for other in meld:
			used.erase(other)
		if result.deadwood < best_result.deadwood:
			best_result = {melds = [meld] + result.melds, deadwood = result.deadwood}
	return best_result


func _points(card: Card) -> float:
	return card.data.get(points_column, "0").to_float()


static func _group_by(cards: Array, column: String) -> Dictionary:
	var groups := {}
	for card: Card in cards:
		var key: String = card.data.get(column, "")
		if key not in groups:
			groups[key] = []
		groups[key].append(card)
	return groups


static func _subsets(items: Array) -> Array:
	var subsets := [[]]
	for item in items:
		subsets += subsets.map(func(subset: Array) -> Array: return subset + [item])
	return subsets
