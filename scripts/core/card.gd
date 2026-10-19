class_name Card
extends RefCounted

const TITLE_COLUMN := "name"

var id := -1
var data: Dictionary
var face_up := true
var marks: Dictionary = {}
## The deck's columns whose every entry is a number (shared by the deck's cards).
var number_columns: Dictionary = {}

## The column naming the card in the log: "name", or the deck's first column if it has none.
var title_column := TITLE_COLUMN
var title: String:
	get: return data.get(title_column, "")


func _init(row: Dictionary) -> void:
	data = row


## card.suit, card.value: a column read directly. A column of numbers gives numbers (a blank is 0); a column that
## mixes in any text (rank: 2 ... 10, J, Q, K, A) gives text, so card.rank == 'A' and card.rank == '10' both work.
## Capitals don't matter (card.color reads a "Color" column), which also gets round Godot's own type names
## like Color, since card.Color can't be written in a step.
func _get(property: StringName) -> Variant:
	var column := _column_matching(property)
	if column.is_empty():
		return null
	var text: String = data[column]
	if column not in number_columns:
		return text
	return text.to_float() if "." in text else text.to_int()


## A column's text, whatever the capitals ("kind" reads a "Kind" column), or missing if the card has no such column.
func column(name: String, missing := "") -> String:
	var found := _column_matching(name)
	return data[found] if found else missing


func _column_matching(property: String) -> String:
	if property in data:
		return property
	for column: String in data:
		if column.to_lower() == property.to_lower():
			return column
	return ""


static func is_number(text: String) -> bool:
	return text.is_empty() or (text.is_valid_int() and str(text.to_int()) == text) or (text.is_valid_float() and str(text.to_float()) == text)
