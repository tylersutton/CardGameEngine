class_name GameRules
extends RefCounted

var zones: Dictionary
var actions: Dictionary
var triggers: Dictionary
var effects: Dictionary
var conditions: Dictionary
## The steps of a turn, run again and again for whoever's turn it is (empty: no turns run by themselves).
var turn: Array
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
	"team_variables", "show_variables", "status", "conditions", "effects", "actions", "turn", "triggers", "melds", "bot", "card_face"]
const GAMES_FOLDER := "res://games/"
## Game files are .jsonc (JSON with comments); plain .json works too.
const GAME_EXTENSIONS := ["jsonc", "json"]
const DEFAULT_CARD_FACE := {"corner": "name", "title": "name"}


## Every game file in games/.
static func game_paths() -> Array[String]:
	var paths: Array[String] = []
	for file in DirAccess.get_files_at(GAMES_FOLDER):
		if file.get_extension() in GAME_EXTENSIONS:
			paths.append(GAMES_FOLDER + file)
	return paths


static func load_json(path: String, files := {}) -> GameRules:
	var rules := GameRules.new()
	if path not in files and not FileAccess.file_exists(path):
		rules.error = "Game file not found: " + path
		return rules
	var json := JSON.new()
	if json.parse(plain_json(files[path] if path in files else FileAccess.get_file_as_string(path))) != OK:
		rules.error = "%s, line %d: %s" % [path.get_file(), json.get_error_line() + 1, json.get_error_message()]
		return rules
	var data: Dictionary = json.data
	rules.unknown_keys.assign(data.keys().filter(func(key: String) -> bool: return key not in KEYS))
	rules.title = data.get("title", path.get_file().get_basename().capitalize())
	rules.description = data.get("description", "")
	rules.zones = data.get("zones", {})
	var actions: Dictionary = data.get("actions", {})
	for action_name in actions:
		var action: Variant = actions[action_name]
		rules.actions[action_name] = action if action is Dictionary else {"steps": action}
		if rules.actions[action_name].has("steps"):
			rules.actions[action_name].steps = _as_steps(rules.actions[action_name].steps)
	var turn: Variant = _as_steps(data.get("turn", []))
	if turn is Array:
		rules.turn = turn
	else:
		rules.unknown_keys.append("turn")
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


## Game files are JSONC: JSON plus // and /* */ comments and trailing commas ([1, 2,]). This turns the text into
## plain JSON for Godot's parser. Quoted text is left alone (so "res://..." is safe), and line breaks are kept so
## error line numbers still match the file.
static func plain_json(text: String) -> String:
	var kept := ""
	var in_text := false
	var i := 0
	while i < text.length():
		var character := text[i]
		if in_text:
			if character == "\\":
				kept += text.substr(i, 2)
				i += 2
				continue
			in_text = character != "\""
		elif character == "\"":
			in_text = true
		elif text.substr(i, 2) == "//":
			var line_end := text.find("\n", i)
			i = text.length() if line_end == -1 else line_end
			continue
		elif text.substr(i, 2) == "/*":
			var comment_end := text.find("*/", i + 2)
			var comment := text.substr(i, (text.length() if comment_end == -1 else comment_end + 2) - i)
			kept += "\n".repeat(comment.count("\n"))
			i += comment.length()
			continue
		elif character == "," and _closes_next(text, i + 1):
			i += 1
			continue
		kept += character
		i += 1
	return kept


## Whether only spaces and comments stand between here and a closing ] or }, making a comma before it trailing.
static func _closes_next(text: String, from: int) -> bool:
	var i := from
	while i < text.length():
		if text[i] in " \t\r\n":
			i += 1
		elif text.substr(i, 2) == "//":
			var line_end := text.find("\n", i)
			i = text.length() if line_end == -1 else line_end
		elif text.substr(i, 2) == "/*":
			var comment_end := text.find("*/", i + 2)
			i = text.length() if comment_end == -1 else comment_end + 2
		else:
			return text[i] in "]}"
	return false


## A game file's contents as data (or null if it isn't valid), e.g. for tests that adjust a game before loading it.
static func read_file(path: String) -> Variant:
	return JSON.parse_string(plain_json(FileAccess.get_file_as_string(path)))


## Every file a game reads: the game file and its decks (for reloading when one changes).
func files_used(path: String) -> Array[String]:
	var paths: Array[String] = [path]
	for zone: Variant in zones.values():
		if zone is Dictionary and zone.get("cards") is String:
			paths.append(zone.cards)
	return paths


## Wherever a list of steps goes, a single step will do: "then": "end_turn()".
static func _as_steps(steps: Variant) -> Variant:
	if steps is String or steps is Dictionary:
		return _as_steps([steps])
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
