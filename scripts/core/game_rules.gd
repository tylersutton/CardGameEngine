class_name GameRules
extends RefCounted

var zones: Dictionary
var actions: Dictionary
var triggers: Dictionary
var effects: Dictionary
var conditions: Dictionary
var moves: Array
var variables: Dictionary
var players: int
var seats: Array
var player_names: Array
var player_variables: Dictionary
var teams: Array
var team_variables: Dictionary
var melds: Dictionary
var status: Dictionary
var show_variables: Array
var bot: Dictionary
var card_face: Dictionary
var unknown_keys: Array[String] = []
var title: String
var description: String
var error: String


const KEYS := ["title", "description", "players", "player_names", "seats", "teams", "zones", "variables", "player_variables",
	"team_variables", "show_variables", "status", "conditions", "effects", "moves", "actions", "triggers", "melds", "bot", "card_face"]
const GAMES_FOLDER := "res://games/"
const DEFAULT_CARD_FACE := {"corner": "name", "title": "name"}


## Every game file in games/.
static func game_paths() -> Array[String]:
	var paths: Array[String] = []
	for file in DirAccess.get_files_at(GAMES_FOLDER):
		if file.get_extension() == "json":
			paths.append(GAMES_FOLDER + file)
	return paths


static func load_json(path: String, files := {}) -> GameRules:
	var rules := GameRules.new()
	if path not in files and not FileAccess.file_exists(path):
		rules.error = "Game file not found: " + path
		return rules
	var json := JSON.new()
	if json.parse(_without_comments(files[path] if path in files else FileAccess.get_file_as_string(path))) != OK:
		rules.error = "%s, line %d: %s" % [path.get_file(), json.get_error_line() + 1, json.get_error_message()]
		return rules
	var data: Dictionary = json.data
	rules.unknown_keys.assign(data.keys().filter(func(key: String) -> bool: return key not in KEYS))
	rules.title = data.get("title", path.get_file().get_basename().capitalize())
	rules.description = data.get("description", "")
	rules.zones = data.get("zones", {})
	for zone: Variant in rules.zones.values():
		for key in ["on_click", "on_double_click"]:
			if zone is Dictionary and zone.has(key):
				zone[key] = _as_steps(zone[key])
	var actions: Dictionary = data.get("actions", {})
	for action_name in actions:
		var action: Variant = actions[action_name]
		rules.actions[action_name] = action if action is Dictionary else {"steps": action}
		if rules.actions[action_name].has("steps"):
			rules.actions[action_name].steps = _as_steps(rules.actions[action_name].steps)
	rules.moves = data.get("moves", [])
	for rule: Variant in rules.moves:
		if rule is Dictionary and rule.has("then"):
			rule.then = _as_steps(rule.then)
	rules.triggers = _steps_by_name(data.get("triggers", {}))
	# "effects" is the older name for triggers that cards name in their on_<zone> column.
	rules.effects = _steps_by_name(data.get("effects", {}))
	for name in rules.effects:
		if name not in rules.triggers:
			rules.triggers[name] = rules.effects[name]
	rules.conditions = data.get("conditions", {})
	rules.variables = _whole_numbers(data.get("variables", {}))
	rules.players = int(data.get("players", 1))
	rules.seats = data.get("seats", [])
	rules.player_names = data.get("player_names", [])
	rules.player_variables = _whole_numbers(data.get("player_variables", {}))
	rules.teams = data.get("teams", [])
	rules.team_variables = _whole_numbers(data.get("team_variables", {}))
	rules.melds = data.get("melds", {})
	rules.status = data.get("status", {})
	rules.show_variables = data.get("show_variables", [])
	rules.bot = data.get("bot", {})
	rules.card_face = data.get("card_face", DEFAULT_CARD_FACE)
	return rules


## Game files may have comments: "//" to the end of the line, except inside quoted text (so "res://..." is safe).
## Line breaks are kept, so error line numbers still match the file.
static func _without_comments(text: String) -> String:
	var kept := ""
	var in_text := false
	var i := 0
	while i < text.length():
		var character := text[i]
		if in_text and character == "\\":
			kept += text.substr(i, 2)
			i += 2
			continue
		if character == "\"":
			in_text = not in_text
		elif not in_text and text.substr(i, 2) == "//":
			var line_end := text.find("
", i)
			i = text.length() if line_end == -1 else line_end
			continue
		kept += character
		i += 1
	return kept


## Every file a game reads: the game file and its decks (for reloading when one changes).
func files_used(path: String) -> Array[String]:
	var paths: Array[String] = [path]
	for zone: Variant in zones.values():
		if zone is Dictionary and zone.get("cards") is String:
			paths.append(zone.cards)
	return paths


## Wherever a list of steps goes, a single step will do: "then": "end_turn()".
static func _as_steps(steps: Variant) -> Variant:
	if steps is String:
		return [steps]
	if steps is not Array:
		return steps
	for step: Variant in steps:
		if step is Dictionary:
			for key in ["then", "else", "do"]:
				if step.has(key):
					step[key] = _as_steps(step[key])
	return steps


static func _steps_by_name(lists: Dictionary) -> Dictionary:
	for name in lists:
		lists[name] = _as_steps(lists[name])
	return lists


## JSON numbers all arrive as decimals; starting values like 0 or 4 become whole numbers, so % and list positions work.
static func _whole_numbers(values: Dictionary) -> Dictionary:
	for name in values:
		if values[name] is float and values[name] == floorf(values[name]):
			values[name] = int(values[name])
	return values
