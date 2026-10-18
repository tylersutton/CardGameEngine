class_name Card
extends RefCounted

const TITLE_COLUMN := "name"

var id := -1
var data: Dictionary
var face_up := true
var marks: Dictionary = {}
## The deck's columns whose every entry is a number (shared by the deck's cards).
var number_columns: Dictionary = {}

var title: String:
	get: return data.get(TITLE_COLUMN, "")


func _init(row: Dictionary) -> void:
	data = row


## card.suit, card.value: a column read directly. A column of numbers gives numbers (a blank is 0); a column that
## mixes in any text (rank: 2 ... 10, J, Q, K, A) gives text, so card.rank == 'A' and card.rank == '10' both work.
func _get(column: StringName) -> Variant:
	if column not in data:
		return null
	var text: String = data[column]
	if column not in number_columns:
		return text
	return text.to_float() if "." in text else text.to_int()


static func is_number(text: String) -> bool:
	return text.is_empty() or (text.is_valid_int() and str(text.to_int()) == text) or (text.is_valid_float() and str(text.to_float()) == text)
