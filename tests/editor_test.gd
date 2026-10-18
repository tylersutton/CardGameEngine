extends SceneTree
## Checks the editor's GameDocument on every game: opening keeps the text exactly, edits change only what they
## should, and adding then removing things leaves the file as it was (comments and all).
## Run: godot --headless -s tests/editor_test.gd

var failures: Array[String] = []


func _init() -> void:
	for path in GameRules.game_paths():
		_check_round_trips(path)
	_check_editing()
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
	# Adding then removing a zone, a move and a trigger leaves the file as it was.
	document.set_value(["zones", "test_zone"], {"layout": "pile", "anchor": [0.5, 0.5]})
	check(document.get_value(["zones", "test_zone", "layout"]) == "pile", "%s: added zone not readable" % name)
	document.remove(["zones", "test_zone"])
	check(document.text == original, "%s: add + remove zone changed the text:\n%s" % [name, _difference(original, document.text)])
	if document.get_value(["moves"]) is Array:
		var count: int = document.get_value(["moves"]).size()
		document.insert(["moves"], count, {"from": "a", "to": "b"})
		check(document.get_value(["moves"]).size() == count + 1, "%s: move not added" % name)
		document.remove(["moves", count])
		check(document.text == original, "%s: add + remove move changed the text:\n%s" % [name, _difference(original, document.text)])
	document.set_value(["triggers", "test_trigger"], ["shuffle(deck)"])
	document.remove(["triggers", "test_trigger"])
	check(document.text == original, "%s: add + remove trigger changed the text:\n%s" % [name, _difference(original, document.text)])


func _check_editing() -> void:
	var document := GameDocument.new()
	document.path = "user://editor_test.jsonc"
	document.set_text('{\n\t// The table\n\t"zones": {\n\t\t"deck": { "label": "Deck", "anchor": [0.4, 0.5] }, // the stock\n\t\t"hand": {}\n\t},\n\t"moves": [\n\t\t{ "from": "deck", "to": "hand" }\n\t]\n}\n')
	var start := document.text
	document.set_value(["zones", "deck", "anchor"], [0.25, 0.75])
	check(document.text.contains('"anchor": [0.25, 0.75] }, // the stock'), "changing a value keeps its line and comment:\n" + document.text)
	document.set_value(["zones", "hand", "layout"], "fan")
	check(document.text.contains('"hand": { "layout": "fan" }'), "adding to an empty object:\n" + document.text)
	document.set_value(["title"], "Test")
	check(document.get_value(["title"]) == "Test" and document.text.contains('"title": "Test"'), "adding a top-level key:\n" + document.text)
	document.rename_key(["zones", "hand"], "cards")
	check(document.has(["zones", "cards", "layout"]), "renaming a key:\n" + document.text)
	check(document.note(["zones"]) == "The table", "reading a note: '%s'" % document.note(["zones"]))
	document.set_note(["zones"], "Where cards go")
	check(document.text.contains("\t// Where cards go\n\t\"zones\""), "changing a note:\n" + document.text)
	document.insert(["moves"], 0, {"from": "hand", "to": "deck"})
	check(document.get_value(["moves", 0, "from"]) == "hand" and document.get_value(["moves", 1, "from"]) == "deck", "inserting at the front:\n" + document.text)
	document.set_value(["zones", "deck", "label"], "Pioche ♠")
	check(document.text.contains('"Pioche ♠"'), "text with symbols stays readable:\n" + document.text)
	var long_value := {"a": "a fairly long piece of text to push this past the line width", "b": "and some more text to be sure it wraps", "c": [1, 2, 3]}
	document.set_value(["zones", "long"], long_value)
	check(document.text.contains('"long": {\n\t\t\t"a":'), "long values go one entry per line:\n" + document.text)
	check(GameDocument.format(document.get_value(["zones", "long"])) == GameDocument.format(long_value), "long value reads back")
	check(start != document.text, "edits happened")


## The first differing line of two texts, for failure messages.
static func _difference(expected: String, actual: String) -> String:
	var a := expected.split("\n")
	var b := actual.split("\n")
	for i in mini(a.size(), b.size()):
		if a[i] != b[i]:
			return "line %d:\n    was: %s\n    now: %s" % [i + 1, a[i], b[i]]
	return "lines %d vs %d" % [a.size(), b.size()]
