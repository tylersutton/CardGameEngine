class_name Snapshot
## Copies of a game's state: whole snapshots for undo and bot try-outs, and a single number (the state hash)
## for checking that two copies of a match (e.g. online) are identical.


static func take(game: Game) -> Dictionary:
	var choice := {choosing = game.choosing, choice_action = game.choice_action, choices = game.choices.duplicate(), pick_count = game.pick_count,
		pick_exact = game.pick_exact, selected = game.selected.duplicate(), asked = game.asked.duplicate(true), choice_asked = game.choice_asked,
		return_player = game.return_player, waiting_steps = StepRunner.copy_frames(game.runner.frames), offered = game.offered.duplicate(),
		over = game.over}
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
	game.choice_action = snapshot.choice.choice_action
	game.choices = snapshot.choice.choices.duplicate()
	game.pick_count = snapshot.choice.pick_count
	game.pick_exact = snapshot.choice.pick_exact
	game.selected.assign(snapshot.choice.selected)
	game.asked.assign(snapshot.choice.asked.duplicate(true))
	game.choice_asked = snapshot.choice.choice_asked
	game.return_player = snapshot.choice.return_player
	game.runner.frames = StepRunner.copy_frames(snapshot.choice.waiting_steps)
	game.runner.waiting = not game.runner.frames.is_empty()
	game.offered = snapshot.choice.offered.duplicate()
	game.over = snapshot.choice.over
	game.rng.state = snapshot.rng


## What's where (cards, faces, marks, variables) as plain data. With every_detail, also the order cards sit in,
## any choice in progress and the random numbers' state; without, just enough to tell whether a move changed anything.
static func describe(game: Game, every_detail := true) -> Dictionary:
	var state := {variables = _plain(game.variables), zones = {}, marks = {}}
	if every_detail:
		state.merge({choice = _plain(take(game).choice), rng = game.rng.state})
	for name in game.zones:
		var ids: Array = game.zones[name].cards.map(func(card: Card) -> int: return card.id if card.face_up else -1 - card.id)
		if not every_detail:
			ids.sort()
		state.zones[name] = ids
		for card in game.zones[name].cards:
			if card.marks:
				state.marks[str(card.id)] = _plain(card.marks)
	return state


## Everything that decides what happens next, as one number.
static func state_hash(game: Game) -> int:
	return JSON.stringify(describe(game)).hash()


## Cards as their ids, so the state can be written out as JSON.
static func _plain(value: Variant) -> Variant:
	if value is Card:
		return "card %d" % value.id
	if value is Zone:
		return "zone " + value.name
	if value is Array:
		return value.map(_plain)
	if value is Dictionary:
		var plain := {}
		for key in value:
			plain[key] = _plain(value[key])
		return plain
	return value
