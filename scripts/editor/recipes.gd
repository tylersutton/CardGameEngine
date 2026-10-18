class_name Recipes
## Ready-made pieces of a game (templates/recipes/*.jsonc), added into the game being edited. What the game
## already has is kept: new zones, variables, buttons and settings are added, new moves and trigger steps go
## after the existing ones, and anything else the game already sets is left as it is.

const FOLDER := "res://templates/recipes/"


static func paths() -> Array:
	return Array(DirAccess.get_files_at(FOLDER)).filter(func(file: String) -> bool: return file.get_extension() == "jsonc") \
		.map(func(file: String) -> String: return FOLDER + file)


## "draw_then_discard.jsonc" -> "Draw then discard".
static func title(path: String) -> String:
	var words := path.get_file().get_basename().replace("_", " ")
	return words.left(1).to_upper() + words.substr(1)


## The // lines at the top of the recipe, which say what it does.
static func description(path: String) -> String:
	var lines := []
	for line in FileAccess.get_file_as_string(path).split("\n"):
		if not line.strip_edges().begins_with("//"):
			break
		lines.append(line.strip_edges().trim_prefix("//").strip_edges())
	return " ".join(lines)


static func add_to(document: GameDocument, path: String) -> void:
	var recipe: Dictionary = GameRules.read_file(path)
	for key: String in recipe:
		_merge(document, [key], recipe[key])


static func _merge(document: GameDocument, path: Array, value: Variant) -> void:
	if not document.has(path):
		document.set_value(path, value)
		return
	var existing: Variant = document.get_value(path)
	if existing is Dictionary and value is Dictionary:
		for key: String in value:
			_merge(document, path + [key], value[key])
	elif path == ["moves"] or (path.size() == 2 and path[0] == "triggers"):
		if existing is String:
			document.set_value(path, [existing])
		for item: Variant in value if value is Array else [value]:
			var list: Array = document.get_value(path)
			if item not in list:
				document.insert(path, list.size(), item)
