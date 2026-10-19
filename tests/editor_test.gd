extends SceneTree
## Checks the editor's GameDocument on every game: opening keeps the text exactly, edits change only what they
## should, and adding then removing things leaves the file as it was (comments and all).
## Run: godot --headless -s tests/editor_test.gd

var failures: Array[String] = []


func _init() -> void:
	for path in GameRules.game_paths():
		_check_round_trips(path)
		_check_rebuilding(path)
	_check_editing()
	_check_recipes()
	print("EDITOR: ", "all passed" if failures.is_empty() else "FAILED\n  " + "\n  ".join(failures))
	quit(0 if failures.is_empty() else 1)


func check(ok: bool, what: String) -> void:
	if not ok:
		failures.append(what)


func _check_round_trips(path: String) -> void:
	var original := FileAccess.get_file_as_string(path)
	var document := GameDocument.open(path)
	var name := path.get_file()
	check(document.parse_error == "", "%s: %s" % [name, document.parse_error])
	check(document.text == original, "%s: opening changed the text" % name)
	check(JSON.stringify(document.data) == JSON.stringify(GameRules.read_file(path)), "%s: reads differently from the engine" % name)
	# Every value set to itself leaves the text alone.
	for zone in document.get_value(["zones"], {}):
		document.set_value(["zones", zone], document.get_value(["zones", zone]))
	check(document.text == original, "%s: setting values to themselves changed the text" % name)
	# Adding then removing a zone, a turn step and a trigger leaves the file as it was.
	document.set_value(["zones", "test_zone"], {"layout": "pile", "anchor": [0.5, 0.5]})
	check(document.get_value(["zones", "test_zone", "layout"]) == "pile", "%s: added zone not readable" % name)
	document.remove(["zones", "test_zone"])
	check(document.text == original, "%s: add + remove zone changed the text:\n%s" % [name, _difference(original, document.text)])
	if document.get_value(["turn"]) is Array:
		var count: int = document.get_value(["turn"]).size()
		document.insert(["turn"], count, "end_turn()")
		check(document.get_value(["turn"]).size() == count + 1, "%s: turn step not added" % name)
		document.remove(["turn", count])
		check(document.text == original, "%s: add + remove turn step changed the text:\n%s" % [name, _difference(original, document.text)])
	document.set_value(["triggers", "test_trigger"], ["shuffle(deck)"])
	document.remove(["triggers", "test_trigger"])
	check(document.text == original, "%s: add + remove trigger changed the text:\n%s" % [name, _difference(original, document.text)])


## Builds the game again from an empty file, one setting at a time as the editor's forms do, and checks the
## result holds the same game and loads cleanly.
func _check_rebuilding(path: String) -> void:
	var original: Dictionary = GameRules.read_file(path)
	var document := GameDocument.new()
	document.path = "user://rebuilt_" + path.get_file()
	document.set_text("{
}
")
	_set_leaves(document, [], original)
	var name := path.get_file()
	check(GameDocument.format(document.data) == GameDocument.format(original), "%s: rebuilt differently" % name)
	var game := Game.new(path, 1, {path: document.text})
	check(game.errors.is_empty(), "%s: rebuilt game has problems: %s" % [name, game.errors])


## Sets every value that isn't an object, depth first, so objects and lists are made along the way.
func _set_leaves(document: GameDocument, at: Array, value: Variant) -> void:
	if value is Dictionary and value:
		for key: String in value:
			_set_leaves(document, at + [key], value[key])
	else:
		document.set_value(at, value)


func _check_editing() -> void:
	var document := GameDocument.new()
	document.path = "user://editor_test.jsonc"
	document.set_text('{\n\t// The table\n\t"zones": {\n\t\t"deck": {\n\t\t\t"label": "Deck", // the stock\n\t\t\t"anchor": [\n\t\t\t\t0.4,\n\t\t\t\t0.5\n\t\t\t]\n\t\t},\n\t\t"hand": {}\n\t},\n\t"moves": [\n\t\t{\n\t\t\t"from": "deck",\n\t\t\t"to": "hand"\n\t\t}\n\t]\n}')
	var start := document.text
	document.set_value(["zones", "deck", "anchor"], [0.25, 0.75])
	check(document.text.contains('"label": "Deck", // the stock\n\t\t\t"anchor": [\n\t\t\t\t0.25,\n\t\t\t\t0.75\n\t\t\t]\n'), "changing a value keeps the comment before it:\n" + document.text)
	document.set_value(["zones", "hand", "layout"], "fan")
	check(document.text.contains('"hand": {\n\t\t\t"layout": "fan"\n\t\t}'), "adding to an empty object:\n" + document.text)
	document.remove(["zones", "hand", "layout"])
	check(document.text.contains('"hand": {}'), "emptying an object:\n" + document.text)
	document.set_value(["zones", "hand", "layout"], "fan")
	document.set_value(["title"], "Test")
	check(document.get_value(["title"]) == "Test" and document.text.contains('"title": "Test"'), "adding a top-level key:\n" + document.text)
	document.rename_key(["zones", "hand"], "cards")
	check(document.has(["zones", "cards", "layout"]), "renaming a key:\n" + document.text)
	check(document.note(["zones"]) == "The table", "reading a note: '%s'" % document.note(["zones"]))
	document.set_note(["zones"], "Where cards go")
	check(document.text.contains("\t// Where cards go\n\t\"zones\""), "changing a note:\n" + document.text)
	document.insert(["moves"], 0, {"from": "hand", "to": "deck"})
	check(document.get_value(["moves", 0, "from"]) == "hand" and document.get_value(["moves", 1, "from"]) == "deck", "inserting at the front:\n" + document.text)
	document.set_value(["moves", 1, "phase"], "draw")
	check(document.text.contains('"to": "hand",\n\t\t\t"phase": "draw"\n\t\t}'), "adding to an object inside a list:\n" + document.text)
	document.set_value(["zones", "deck", "label"], "Pioche ♠")
	check(document.text.contains('"Pioche ♠"'), "text with symbols stays readable:\n" + document.text)
	var nested := {"a": "text", "b": [], "c": [1, {"d": true}]}
	document.set_value(["zones", "nested"], nested)
	check(document.text.contains('"nested": {\n\t\t\t"a": "text",\n\t\t\t"b": [],\n\t\t\t"c": [\n\t\t\t\t1,\n\t\t\t\t{\n\t\t\t\t\t"d": true\n\t\t\t\t}\n\t\t\t]\n\t\t}'),
		"values are written one entry per line, as VS Code formats them:\n" + document.text)
	check(GameDocument.format(document.get_value(["zones", "nested"])) == GameDocument.format(nested), "nested value reads back")
	check(start != document.text, "edits happened")


## Every recipe added in turn to an empty game makes a game that loads cleanly; adding them again changes nothing.
func _check_recipes() -> void:
	var document := GameDocument.new()
	document.path = "res://games/recipe_test.jsonc"
	document.set_text("{\n}\n")
	for recipe: String in Recipes.paths():
		Recipes.add_to(document, recipe)
		check(document.parse_error == "", "%s: %s" % [recipe.get_file(), document.parse_error])
		check(not Recipes.description(recipe).is_empty(), "%s: no description at the top" % recipe.get_file())
	var all_added := document.text
	for recipe: String in Recipes.paths():
		Recipes.add_to(document, recipe)
	check(document.text == all_added, "adding the recipes twice changed the game:\n" + _difference(all_added, document.text))
	var game := Game.new(document.path, 1, {document.path: document.text})
	check(game.errors.is_empty(), "the recipes together have problems: %s\n%s" % [game.errors, document.text])


## The first differing line of two texts, for failure messages.
static func _difference(expected: String, actual: String) -> String:
	var a := expected.split("\n")
	var b := actual.split("\n")
	for i in mini(a.size(), b.size()):
		if a[i] != b[i]:
			return "line %d:\n    was: %s\n    now: %s" % [i + 1, a[i], b[i]]
	return "lines %d vs %d" % [a.size(), b.size()]
