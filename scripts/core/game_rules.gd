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
var title: String
var description: String
var error: String


static func load_json(path: String) -> GameRules:
	var rules := GameRules.new()
	if not FileAccess.file_exists(path):
		rules.error = "Game file not found: " + path
		return rules
	var json := JSON.new()
	if json.parse(FileAccess.get_file_as_string(path)) != OK:
		rules.error = "%s, line %d: %s" % [path.get_file(), json.get_error_line() + 1, json.get_error_message()]
		return rules
	var data: Dictionary = json.data
	rules.title = data.get("title", path.get_file().get_basename().capitalize())
	rules.description = data.get("description", "")
	rules.zones = data.get("zones", {})
	var actions: Dictionary = data.get("actions", {})
	for action_name in actions:
		var action: Variant = actions[action_name]
		rules.actions[action_name] = {"steps": action} if action is Array else action
	rules.moves = data.get("moves", [])
	rules.triggers = data.get("triggers", {})
	rules.effects = data.get("effects", {})
	rules.conditions = data.get("conditions", {})
	rules.variables = data.get("variables", {})
	rules.players = int(data.get("players", 1))
	rules.seats = data.get("seats", [])
	rules.player_names = data.get("player_names", [])
	rules.player_variables = data.get("player_variables", {})
	rules.teams = data.get("teams", [])
	rules.team_variables = data.get("team_variables", {})
	rules.melds = data.get("melds", {})
	rules.status = data.get("status", {})
	rules.show_variables = data.get("show_variables", [])
	rules.bot = data.get("bot", {})
	rules.card_face = data.get("card_face", {})
	return rules
