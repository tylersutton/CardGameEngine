class_name BlockHelp
## What each building block does, read from the building-block tables in docs/reference.md, so the editor can
## show it beside steps without saying it a second time. (An exported build without docs/ simply has no help.)

const REFERENCE := "res://docs/reference.md"

## [{category, title, text, names}], in the reference's order. title is the table's first cell, e.g.
## "mark(cards, 'key', value), unmark(cards, 'key')"; names are the building blocks it covers.
static var entries: Array = _read()
## Building block name -> what it does.
static var by_name: Dictionary = _index()


static func _read() -> Array:
	var found := []
	var category := ""
	var in_blocks := false
	var link := RegEx.create_from_string("\\[([^\\]]+)\\]\\([^)]*\\)")
	var name := RegEx.create_from_string("([A-Za-z_]\\w*)\\(")
	for line in FileAccess.get_file_as_string(REFERENCE).split("\n"):
		if line.begins_with("## "):
			in_blocks = line.strip_edges() == "## Building blocks"
		elif in_blocks and line.begins_with("**"):
			category = line.strip_edges().trim_prefix("**").trim_suffix("**")
		elif in_blocks and line.begins_with("| `"):
			var cells := line.split("|")
			var title := cells[1].strip_edges().replace("`", "")
			var text := link.sub(cells[2].strip_edges(), "$1", true).replace("`", "").replace("**", "")
			var names := name.search_all(title).map(func(found_name: RegExMatch) -> String: return found_name.get_string(1))
			found.append({category = category, title = title, text = text, names = names})
	return found


static func _index() -> Dictionary:
	var index := {}
	for entry: Dictionary in entries:
		for block: String in entry.names:
			index[block] = entry.text
	return index
