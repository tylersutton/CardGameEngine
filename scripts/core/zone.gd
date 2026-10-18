class_name Zone
extends RefCounted

signal changed(zone: Zone)

const FACE_DOWN := "down"
const FACE_UP := "up"
const FACE_PRIVATE := "private"
const FACE_OTHERS := "others"
## Older names for "private", still accepted.
const PRIVATE_ALIASES := ["owner", "human"]
const FACES := [FACE_UP, FACE_DOWN, FACE_PRIVATE, FACE_OTHERS]
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
var spread: int
var on_click: Array
var on_double_click: Array
var covered_by: Array = []
var cards: Array[Card] = []
var sort_columns: Array[String] = []


## seats: how many players there are, for per-player zones placed round the table from seat 1's "anchor".
func _init(zone_kind: String, definition: Dictionary, number := 0, seats := 1) -> void:
	kind = zone_kind
	owner = number if definition.get("per_player", false) else 0
	name = kind if number == 0 else "%s_%d" % [kind, number]
	var labels: Array = definition.get("labels", [])
	label = labels[number - 1] if number and labels.size() >= number else definition.get("label", "").replace(LABEL_NUMBER, str(number))
	has_outline = definition.get("outline", true)
	layout = definition.get("layout", "row")
	face = definition.get("face", FACE_UP)
	if face in PRIVATE_ALIASES:
		face = FACE_PRIVATE
	var anchor_values: Array = definition.anchors[number - 1] if number and definition.has("anchors") else definition.get("anchor", [0.5, 0.5])
	anchor = Vector2(anchor_values[0], anchor_values[1])
	if number and definition.has("grid"):
		var columns := int(definition.grid.columns)
		var spacing: Array = definition.grid.spacing
		anchor += Vector2((number - 1) % columns * spacing[0], (number - 1) / columns * spacing[1])
	width = definition.get("width", 0.0)
	var rotations: Array = definition.get("rotations", [])
	rotation = rotations[number - 1] if number and rotations.size() >= number else definition.get("rotation", 0.0)
	if definition.get("per_player", false) and number > 1 and not definition.has("anchors"):
		anchor = around_table(anchor, number, seats)
		rotation += seat_turn(number, seats)
	drags_stack = definition.get("drag", "") == DRAG_STACK
	spread = int(definition.get("spread", 0))
	on_click = definition.get("on_click", [])
	on_double_click = definition.get("on_double_click", [])
	var covering: Array = definition.get("covered_by", [])
	if number and covering.size() >= number:
		covered_by = covering[number - 1]


## Where seat 1's spot is for another seat: turned round the table's center, one equal step per seat
## (seat 2 on seat 1's left, as play goes clockwise).
static func around_table(seat_one_spot: Vector2, seat: int, seats: int) -> Vector2:
	var center := Vector2(0.5, 0.5)
	return center + (seat_one_spot - center).rotated(TAU * (seat - 1) / seats)


## How far cards turn at that seat: sideways at the sides, but upright across the table, so they stay readable.
static func seat_turn(seat: int, seats: int) -> float:
	var quarter_turns := roundi(4.0 * (seat - 1) / seats) % 4
	return 0.0 if quarter_turns == 2 else 90.0 * quarter_turns


func add(card: Card) -> void:
	card.face_up = face != FACE_DOWN
	card.marks.clear()
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


## Where the card would go if it were added, given the zone's sorting.
func sorted_index(card: Card) -> int:
	return cards.filter(func(other: Card) -> bool: return other != card and not _precedes(card, other)).size()


## Shuffles with the match's own random numbers, so a match replays the same from its seed.
func shuffle(rng: RandomNumberGenerator) -> void:
	for i in range(cards.size() - 1, 0, -1):
		var j := rng.randi_range(0, i)
		var swapped := cards[i]
		cards[i] = cards[j]
		cards[j] = swapped
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
