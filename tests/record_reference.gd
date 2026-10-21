extends SceneTree
## Records what the C# engine (csharp/) must reproduce: Godot's random numbers for some seeds, how its sort
## orders ties, what each game loads (rules, zones, decks), and matches of every game (seed, inputs, and the log
## after each input). Each match is replayed here first, to make sure the
## recording itself repeats. Rerun after changing the engine or a game:
## godot --headless -s tests/record_reference.gd

const OUTPUT_FOLDER := "res://csharp/Core.Tests/Reference/"
const SEEDS := [1, 2]
const MOST_INPUTS := 300
const RANDOM_SEEDS := [0, 1, 11, 12345, 4294967295, 9876543210123]
const RANDOM_RANGES := [[0, 1], [0, 2], [0, 51], [1, 6], [-3, 3], [5, 2], [7, 7], [0, 1023], [0, 1000000]]
## Lengths either side of where Godot's sort switches from quicksort to insertion sort (16).
const SORT_LENGTHS := [0, 1, 2, 5, 15, 16, 17, 20, 33, 52, 100, 300]

var problems := []


func _init() -> void:
	var script_errors := ScriptErrors.watch()
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUTPUT_FOLDER + "matches"))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUTPUT_FOLDER + "loading"))
	_write(OUTPUT_FOLDER + "random_numbers.json", _random_numbers())
	_write(OUTPUT_FOLDER + "sorting.json", _sorting())
	for path in GameRules.game_paths():
		_write(OUTPUT_FOLDER + "loading/" + path.get_file().get_basename() + ".json", _loading(path))
		var matches := []
		for match_seed: int in SEEDS:
			var recorded := _record(path, match_seed)
			if recorded.is_empty():
				break
			if _logs_of(path, match_seed, recorded.inputs) != recorded.logs:
				problems.append("%s seed %d: replaying gave a different log" % [path.get_file(), match_seed])
			matches.append(recorded)
		if matches:
			_write(OUTPUT_FOLDER + "matches/" + path.get_file().get_basename() + ".json", matches)
			print("  %s: %s inputs" % [path.get_file(), ", ".join(matches.map(func(each: Dictionary) -> String: return str(each.inputs.size())))])
	problems.append_array(script_errors.messages)
	print("RECORD REFERENCE: ", "done" if problems.is_empty() else "FAILED\n  " + "\n  ".join(problems))
	quit(0 if problems.is_empty() else 1)


## Seeds and states as text, since JSON numbers can't hold every 64-bit number exactly.
func _random_numbers() -> Array:
	var sequences := []
	for random_seed: int in RANDOM_SEEDS:
		var rng := RandomNumberGenerator.new()
		rng.seed = random_seed
		var numbers := []
		for i in 5:
			numbers.append(rng.randi())
		var ranges := []
		for range_ends: Array in RANDOM_RANGES:
			ranges.append([range_ends[0], range_ends[1], rng.randi_range(range_ends[0], range_ends[1])])
		var shuffled := range(52)
		for i in range(shuffled.size() - 1, 0, -1):
			var j := rng.randi_range(0, i)
			var swapped: int = shuffled[i]
			shuffled[i] = shuffled[j]
			shuffled[j] = swapped
		sequences.append({seed = str(random_seed), numbers = numbers, ranges = ranges, shuffled = shuffled, state = str(rng.state)})
	return sequences


## Godot's sort isn't stable, so items that tie end up in an order only its own algorithm gives: lists of
## keys with many ties, and where each item (by its first position) ended up after sorting by key.
func _sorting() -> Array:
	var rng := RandomNumberGenerator.new()
	rng.seed = 7
	var cases := []
	for length: int in SORT_LENGTHS:
		for most_key: int in [3, length]:
			var keys := []
			for i in length:
				keys.append(rng.randi_range(0, most_key))
			var items := range(length).map(func(i: int) -> Array: return [keys[i], i])
			items.sort_custom(func(a: Array, b: Array) -> bool: return a[0] < b[0])
			cases.append({keys = keys, order = items.map(func(item: Array) -> int: return item[1])})
	return cases


## What the game file reads as (every GameRules field), the zones the game builds from it, and the cards of
## each deck it uses. Kept in the file's order, with whole numbers and decimals apart ("4" and "4.0").
func _loading(path: String) -> Dictionary:
	var rules := GameRules.load_json(path)
	var read := {}
	for field in ["title", "description", "zones", "actions", "triggers", "effects", "conditions", "condition_parameters",
			"condition_errors", "constants", "turn", "variables", "players", "seats", "player_names", "player_variables", "teams",
			"team_variables", "melds", "status", "show_variables", "bot", "card_face", "unknown_keys", "error"]:
		read[field] = rules.get(field)
	var zones := []
	for zone: Zone in Game.new(path, 1).zones.values():
		zones.append({name = zone.name, label = zone.label, has_outline = zone.has_outline, kind = zone.kind, owner = zone.owner,
			layout = zone.layout, face = zone.face, anchor = [zone.anchor.x, zone.anchor.y], width = zone.width,
			rotation = zone.rotation, drags_stack = zone.drags_stack, spread = zone.spread, covered_by = zone.covered_by})
	var decks := {}
	for deck_path in rules.files_used(path).slice(1):
		var cards := DeckLoader.load_csv(deck_path)
		decks[deck_path] = {title_column = cards[0].title_column if cards else "", number_columns = cards[0].number_columns.keys() if cards else [],
			cards = cards.map(func(card: Card) -> Dictionary: return card.data)}
	return {rules = read, zones = zones, decks = decks}


## Plays random legal moves (pressing a people-only button like Next hand when there are none), as the
## playthrough test does. Empty if the game doesn't load.
func _record(path: String, match_seed: int) -> Dictionary:
	var game := Game.new(path, match_seed)
	if game.errors:
		problems.append("%s doesn't load: %s" % [path.get_file(), game.errors[0]])
		return {}
	var lines := []
	game.game_log.logged.connect(func(text: String) -> void: lines.append(text))
	game.failed.connect(func(text: String) -> void: lines.append("[failed] " + text))
	game.start()
	var logs := [lines.duplicate()]
	var inputs := []
	var chooser := RandomNumberGenerator.new()
	chooser.seed = match_seed
	while inputs.size() < MOST_INPUTS and not game.over:
		var moves := game.plays.legal_moves()
		var move: Array = moves[chooser.randi_range(0, moves.size() - 1)] if moves else _people_only_button(game)
		if move.is_empty():
			break
		for input: Dictionary in move:
			lines.clear()
			if game.apply(input):
				inputs.append(input)
				logs.append(lines.duplicate())
	return {seed = match_seed, inputs = inputs, logs = logs, end = _end_state(game)}


func _people_only_button(game: Game) -> Array:
	for action_name: String in game.rules.actions:
		if game.rules.actions[action_name].get("human_only", false) and game.plays.can_perform(action_name):
			return [{type = "perform", action = action_name}]
	return []


func _logs_of(path: String, match_seed: int, inputs: Array) -> Array:
	var game := Game.new(path, match_seed)
	var lines := []
	game.game_log.logged.connect(func(text: String) -> void: lines.append(text))
	game.failed.connect(func(text: String) -> void: lines.append("[failed] " + text))
	game.start()
	var logs := [lines.duplicate()]
	for input: Dictionary in inputs:
		lines.clear()
		game.apply(input)
		logs.append(lines.duplicate())
	return logs


## Where every card ended up (face-down cards marked "▼"), the variables, and the result.
func _end_state(game: Game) -> Dictionary:
	var zones := {}
	for zone: Zone in game.zones.values():
		zones[zone.name] = zone.cards.map(func(card: Card) -> String: return card.title if card.face_up else "▼" + card.title)
	var variables := {}
	for name: String in game.variables:
		variables[name] = Game.format_value(game.variables[name])
	return {zones = zones, variables = variables, over = game.over, result = game.result_text() if game.over else ""}


func _write(path: String, data: Variant) -> void:
	var file := FileAccess.open(path, FileAccess.WRITE)
	# In their own order, and with every digit of a decimal.
	file.store_string(JSON.stringify(data, "\t", false, true))
