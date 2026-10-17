class_name CardView
extends Control

signal picked_up(view: CardView)
signal dragged(view: CardView)
signal clicked(view: CardView)
signal double_clicked(view: CardView)
signal dropped(view: CardView)

const SIZE := Vector2(100, 140)
const MOVE_TIME := 0.25
const HOVER_LIFT := 16.0
const CLICK_DISTANCE := 5.0
const MARGIN := 8.0
const CORNER_FONT_SIZE := 14
const TITLE_FONT_SIZE := 24
const BODY_FONT_SIZE := 12
const HIGHLIGHT_COLOR := Color(1, 0.85, 0.2)
const HIGHLIGHT_WIDTH := 4
const SELECTED_COLOR := Color(0.3, 0.75, 1)
const ALLOWED_COLOR := Color(0.35, 0.9, 0.45)
const REFUSED_COLOR := Color(0.95, 0.35, 0.3)

enum DropHint { NONE, ALLOWED, REFUSED }

static var face_style := _rounded_box(Color.WHITE, Color(0.75, 0.75, 0.75))
static var back_style := _rounded_box(Color(0.16, 0.24, 0.52), Color.WHITE)
static var highlight_style := _outline_box(HIGHLIGHT_COLOR)
static var selected_style := _outline_box(SELECTED_COLOR)
static var allowed_style := _outline_box(ALLOWED_COLOR)
static var refused_style := _outline_box(REFUSED_COLOR)

var card: Card
var face: Dictionary
var face_up := true:
	set(value):
		face_up = value
		queue_redraw()
var highlighted := false:
	set(value):
		highlighted = value
		queue_redraw()
var selected := false:
	set(value):
		selected = value
		queue_redraw()
var drop_hint := DropHint.NONE:
	set(value):
		drop_hint = value
		queue_redraw()
var lift := 0.0:
	set(value):
		lift = value
		queue_redraw()
var tween: Tween
var grabbed := false
var grab_start: Vector2
var grab_offset: Vector2


func _init(card_to_show: Card, card_face: Dictionary) -> void:
	card = card_to_show
	face = card_face
	size = SIZE
	pivot_offset = SIZE / 2
	mouse_entered.connect(_lift_to.bind(HOVER_LIFT))
	mouse_exited.connect(_lift_to.bind(0.0))


func move_to(target_position: Vector2, target_rotation: float) -> void:
	stop_moving()
	tween = create_tween().set_parallel().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(self, "position", target_position, MOVE_TIME)
	tween.tween_property(self, "rotation", target_rotation, MOVE_TIME)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.double_click:
			double_clicked.emit(self)
		elif event.pressed:
			_grab()
		elif grabbed:
			_release()
	elif event is InputEventMouseMotion and grabbed:
		global_position = get_global_mouse_position() - grab_offset
		dragged.emit(self)


func _grab() -> void:
	grabbed = true
	grab_start = get_global_mouse_position()
	grab_offset = grab_start - global_position
	stop_moving()
	get_parent().move_child(self, -1)
	picked_up.emit(self)


func stop_moving() -> void:
	if tween:
		tween.kill()


func _release() -> void:
	grabbed = false
	if get_global_mouse_position().distance_to(grab_start) < CLICK_DISTANCE:
		clicked.emit(self)
	else:
		dropped.emit(self)


func _lift_to(height: float) -> void:
	create_tween().tween_property(self, "lift", height, 0.1)


func _draw() -> void:
	draw_set_transform(Vector2(0, -lift))
	var rect := Rect2(Vector2.ZERO, SIZE)
	if face_up:
		_draw_face(rect)
	else:
		draw_style_box(back_style, rect)
	var outline := _outline_style()
	if outline:
		draw_style_box(outline, rect.grow(HIGHLIGHT_WIDTH / 2.0))


func _outline_style() -> StyleBoxFlat:
	match drop_hint:
		DropHint.ALLOWED:
			return allowed_style
		DropHint.REFUSED:
			return refused_style
	if selected:
		return selected_style
	return highlight_style if highlighted else null


func _draw_face(rect: Rect2) -> void:
	draw_style_box(face_style, rect)
	var font := get_theme_default_font()
	var ink := Color.from_string(_field("color"), Color.BLACK)
	var text_width := SIZE.x - MARGIN * 2
	draw_string(font, Vector2(MARGIN, MARGIN + CORNER_FONT_SIZE), _field("corner"), HORIZONTAL_ALIGNMENT_LEFT, text_width, CORNER_FONT_SIZE, ink)
	draw_string(font, Vector2(MARGIN, SIZE.y / 2), _field("title"), HORIZONTAL_ALIGNMENT_CENTER, text_width, TITLE_FONT_SIZE, ink)
	var line_y := SIZE.y / 2 + TITLE_FONT_SIZE
	for column in face.get("body", []):
		draw_string(font, Vector2(MARGIN, line_y), card.data.get(column, ""), HORIZONTAL_ALIGNMENT_CENTER, text_width, BODY_FONT_SIZE, ink)
		line_y += BODY_FONT_SIZE + 4


func _field(face_part: String) -> String:
	return card.data.get(face.get(face_part, ""), "")


static func _rounded_box(fill: Color, border: Color) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = fill
	box.border_color = border
	box.set_border_width_all(2)
	box.set_corner_radius_all(8)
	box.shadow_size = 3
	box.shadow_color = Color(0, 0, 0, 0.3)
	return box


static func _outline_box(color: Color) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.draw_center = false
	box.border_color = color
	box.set_border_width_all(HIGHLIGHT_WIDTH)
	box.set_corner_radius_all(10)
	return box
