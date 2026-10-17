class_name Zone
extends RefCounted

signal changed(zone: Zone)

const FACE_DOWN := "down"
const FACE_OWNER := "owner"
const FACE_HUMAN := "human"
const DRAG_STACK := "stack"
const LABEL_NUMBER := "#"

var name: String
var label: String
var has_outline: bool
var kind: String
var owner: int
var layout: String
var face: String
var anchor: Vector2
var width: float
var rotation: float
var drags_stack: bool
var on_click: Array
var cards: Array[Card] = []
var sort_columns: Array[String] = []


func _init(zone_kind: String, definition: Dictionary, number := 0) -> void:
	kind = zone_kind
	owner = number if definition.get("per_player", false) else 0
	name = kind if number == 0 else "%s_%d" % [kind, number]
	label = definition.get("label", "").replace(LABEL_NUMBER, str(number))
	has_outline = definition.get("outline", true)
	layout = definition.get("layout", "row")
	face = definition.get("face", "up")
	var anchor_values: Array = definition.anchors[number - 1] if number and definition.has("anchors") else definition.get("anchor", [0.5, 0.5])
	anchor = Vector2(anchor_values[0], anchor_values[1])
	width = definition.get("width", 0.0)
	var rotations: Array = definition.get("rotations", [])
	rotation = rotations[number - 1] if number and rotations.size() >= number else definition.get("rotation", 0.0)
	drags_stack = definition.get("drag", "") == DRAG_STACK
	on_click = definition.get("on_click", [])


func add(card: Card) -> void:
	card.face_up = face != FACE_DOWN
	cards.append(card)
	_sort()
	changed.emit(self)


func add_all(new_cards: Array[Card]) -> void:
	for card in new_cards:
		card.face_up = face != FACE_DOWN
	cards.append_array(new_cards)
	_sort()
	changed.emit(self)


func remove(card: Card) -> void:
	cards.erase(card)
	changed.emit(self)


func reorder(card: Card, index: int) -> void:
	sort_columns.clear()
	cards.erase(card)
	cards.insert(index, card)
	changed.emit(self)


func restore(saved_cards: Array[Card], saved_sort_columns: Array[String]) -> void:
	cards.assign(saved_cards)
	sort_columns.assign(saved_sort_columns)
	changed.emit(self)


func shuffle() -> void:
	cards.shuffle()
	changed.emit(self)


func keep_sorted_by(columns: Array[String]) -> void:
	sort_columns = columns
	_sort()
	changed.emit(self)


func is_empty() -> bool:
	return cards.is_empty()


func _sort() -> void:
	if sort_columns:
		cards.sort_custom(_precedes)


func _precedes(a: Card, b: Card) -> bool:
	for column in sort_columns:
		var a_value: String = a.data.get(column, "")
		var b_value: String = b.data.get(column, "")
		if a_value == b_value:
			continue
		if a_value.is_valid_float() and b_value.is_valid_float():
			return a_value.to_float() < b_value.to_float()
		return a_value < b_value
	return false
