class_name ZoneDragger
extends Control
## Laid over the table in the editor's Edit mode: shows every zone's outline and name, and lets you pick a zone
## (zone_picked) or drag it somewhere else (zone_moved, with its new center as fractions of the table).

signal zone_picked(zone: Zone)
signal zone_moved(zone: Zone, spot: Vector2)

const OUTLINE := Color(1, 1, 1, 0.5)
const SELECTED := Color(1, 0.85, 0.3)
const NAME_COLOR := Color(1, 1, 1, 0.9)
const FONT_SIZE := 13
const DRAG_THRESHOLD := 4.0

var table: TableView
var selected_kind := ""
var dragging: Zone
var drag_offset := Vector2.ZERO
var drag_center := Vector2.ZERO
var press_at := Vector2.ZERO


func _init(table_to_edit: TableView) -> void:
	table = table_to_edit
	set_anchors_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP
	z_index = 20
	# Clicks go to the last child, whatever its z_index, so stay in front of card views as they're made.
	table.child_entered_tree.connect(func(_child: Node) -> void: move_to_front.call_deferred())


func _draw() -> void:
	if not table.game:
		return
	var font := get_theme_default_font()
	# The editor shrinks the table to fit; names stay readable at their usual size.
	var font_size := roundi(FONT_SIZE / get_global_transform().get_scale().x)
	for zone: Zone in table.game.zones.values():
		var rect := table.slot_rect(zone)
		if zone == dragging:
			rect.position = drag_center - rect.size / 2
		var color := SELECTED if zone.kind == selected_kind else OUTLINE
		draw_rect(rect, color, false, 3.0 if zone.kind == selected_kind else 1.5)
		draw_string(font, rect.position + Vector2(2, -4), zone.name, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, NAME_COLOR)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			dragging = _zone_at(event.position)
			press_at = event.position
			if dragging:
				var center := table.slot_rect(dragging).get_center()
				drag_offset = event.position - center
				drag_center = center
				zone_picked.emit(dragging)
		elif dragging:
			if event.position.distance_to(press_at) > DRAG_THRESHOLD:
				zone_moved.emit(dragging, drag_center / table.size)
			dragging = null
			queue_redraw()
	elif event is InputEventMouseMotion and dragging:
		drag_center = event.position - drag_offset
		queue_redraw()


## The smallest zone outline under the point (so a zone sitting inside a bigger one can still be picked).
func _zone_at(point: Vector2) -> Zone:
	var found: Zone
	var found_area := INF
	for zone: Zone in table.game.zones.values():
		var rect := table.slot_rect(zone)
		if rect.has_point(point) and rect.get_area() < found_area:
			found = zone
			found_area = rect.get_area()
	return found
