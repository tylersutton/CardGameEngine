class_name GameLog
extends RefCounted
## Collects what happened into readable lines: card moves and flips are grouped one line per kind
## ("deck → hand_1: 2♦, 7♥"), and identical lines in a row are shown once with a count ("(×17)").

signal logged(text: String)

var items: Dictionary = {}
var line_text := ""
var repeats := 0


func line(text: String) -> void:
	if text == line_text and items.is_empty():
		repeats += 1
		return
	flush()
	line_text = text
	repeats = 1


## An item of a grouped line, e.g. a card under "deck → hand_1". An empty name is a card the viewer can't see.
func item(kind: String, name: String) -> void:
	_flush_line()
	items.get_or_add(kind, []).append(name)


func flush() -> void:
	_flush_line()
	flush_items()


func flush_items() -> void:
	for kind: String in items:
		var names: Array = items[kind].filter(func(name: String) -> bool: return name != "")
		var hidden: int = items[kind].size() - names.size()
		if hidden:
			names.append("%d hidden card%s" % [hidden, "" if hidden == 1 else "s"])
		logged.emit("  %s: %s" % [kind, ", ".join(names)])
	items.clear()


## Drops anything not yet shown, e.g. after a bot's silent try-out.
func discard() -> void:
	items.clear()
	line_text = ""


func _flush_line() -> void:
	if line_text:
		logged.emit(line_text + (" (×%d)" % repeats if repeats > 1 else ""))
	line_text = ""
