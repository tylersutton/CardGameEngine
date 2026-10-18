class_name Card
extends RefCounted

const TITLE_COLUMN := "name"

var id := -1
var data: Dictionary
var face_up := true
var marks: Dictionary = {}

var title: String:
	get: return data.get(TITLE_COLUMN, "")


func _init(row: Dictionary) -> void:
	data = row


## card.suit, card.value: a column read directly, as a number when it is one ("12" -> 12, but "+2" stays text).
func _get(column: StringName) -> Variant:
	if column not in data:
		return null
	var text: String = data[column]
	if text.is_valid_int() and str(text.to_int()) == text:
		return text.to_int()
	if text.is_valid_float() and str(text.to_float()) == text:
		return text.to_float()
	return text
