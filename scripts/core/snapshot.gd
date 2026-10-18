class_name Snapshot
## Copies of a game's state: whole snapshots for undo and bot try-outs, and a single number (the state hash)
## for checking that two copies of a match (e.g. online) are identical.


static func take(game: Game) -> Dictionary:
	var choice := {choosing = game.choosing, choices = game.choices.duplicate(), pick_count = game.pick_count, pick_exact = game.pick_exact,
		selected = game.selected.duplicate(), asked = game.asked.duplicate(true), choice_asked = game.choice_asked, return_player = game.return_player}
	var snapshot := {zones = {}, sort_columns = {}, faces = {}, marks = {}, variables = game.variables.duplicate(true), choice = choice, rng = game.rng.state}
	for name in game.zones:
		var zone: Zone = game.zones[name]
		snapshot.zones[name] = zone.cards.duplicate()
		snapshot.sort_columns[name] = zone.sort_columns.duplicate()
		for card in zone.cards:
			snapshot.faces[card] = card.face_up
			if card.marks:
				snapshot.marks[card] = card.marks.duplicate()
	return snapshot


static func restore(game: Game, snapshot: Dictionary) -> void:
	for name in game.zones:
		game.zones[name].restore(snapshot.zones[name], snapshot.sort_columns[name])
	for card: Card in snapshot.faces:
		card.face_up = snapshot.faces[card]
		card.marks = snapshot.marks.get(card, {}).duplicate()
	game.variables.clear()
	game.variables.merge(snapshot.variables.duplicate(true))
	game.choosing = snapshot.choice.choosing
	game.choices = snapshot.choice.choices.duplicate()
	game.pick_count = snapshot.choice.pick_count
	game.pick_exact = snapshot.choice.pick_exact
	game.selected.assign(snapshot.choice.selected)
	game.asked.assign(snapshot.choice.asked.duplicate(true))
	game.choice_asked = snapshot.choice.choice_asked
	game.return_player = snapshot.choice.return_player
	game.rng.state = snapshot.rng


## What's where, ignoring the order cards sit in, so a bot can tell whether a move changed anything.
static func contents(game: Game) -> Dictionary:
	var result := {variables = game.variables.duplicate(true), marks = {}}
	for name in game.zones:
		var face_up_ids := game.zones[name].cards.map(func(card: Card) -> int: return card.get_instance_id() * (1 if card.face_up else -1))
		face_up_ids.sort()
		result[name] = face_up_ids
		for card in game.zones[name].cards:
			if card.marks:
				result.marks[card.get_instance_id()] = card.marks.duplicate()
	return result


## Everything that decides what happens next, as one number.
static func state_hash(game: Game) -> int:
	var state := {variables = _plain(game.variables), choice = _plain(take(game).choice), rng = game.rng.state, zones = {}, marks = {}}
	for name in game.zones:
		state.zones[name] = game.zones[name].cards.map(func(card: Card) -> int: return card.id if card.face_up else -1 - card.id)
		for card in game.zones[name].cards:
			if card.marks:
				state.marks[str(card.id)] = _plain(card.marks)
	return JSON.stringify(state).hash()


## Cards as their ids, so the state can be written out as JSON.
static func _plain(value: Variant) -> Variant:
	if value is Card:
		return "card %d" % value.id
	if value is Array:
		return value.map(_plain)
	if value is Dictionary:
		var plain := {}
		for key in value:
			plain[key] = _plain(value[key])
		return plain
	return value
