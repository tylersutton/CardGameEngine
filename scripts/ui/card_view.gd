class_name CardView
extends Control

signal picked_up(view: CardView)
signal dragged(view: CardView)
signal clicked(view: CardView)
signal double_clicked(view: CardView)
signal dropped(view: CardView)
signal hovered(view: CardView)
signal unhovered(view: CardView)

const SIZE := Vector2(100, 140)
const MOVE_TIME := 0.25
const HOVER_LIFT := 16.0
const CLICK_DISTANCE := 5.0
const MARGIN := 8.0
const CORNER_FONT_SIZE := 14
const TITLE_FONT_SIZE := 24
const BODY_FONT_SIZE := 12
const TEXT_FONT_SIZE := 10
const MIN_TEXT_FONT_SIZE := 6
const MIN_TITLE_FONT_SIZE := 12
const HIGHLIGHT_COLOR := Color(1, 0.85, 0.2)
const HIGHLIGHT_WIDTH := 4
const SELECTED_COLOR := Color(0.3, 0.75, 1)
const ALLOWED_COLOR := Color(0.35, 0.9, 0.45)
const REFUSED_COLOR := Color(0.95, 0.35, 0.3)
const MARKS_HEIGHT := 18.0
const MARKS_FONT_SIZE := 13
const MARKS_BACKGROUND := Color(0.1, 0.1, 0.1, 0.8)

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
var marks_text := "":
	set(value):
		marks_text = value
		queue_redraw()
var lift := 0.0:
	set(value):
		lift = value
		queue_redraw()
var tween: Tween
## Off for cards this screen's player can't move (another player's, online): they can still be clicked.
var movable := true
var grabbed := false
## The press was the second of a double-click; it counts as one when the button comes up, like any click.
var pressed_twice := false
var grab_start: Vector2
var grab_offset: Vector2


func _init(card_to_show: Card, card_face: Dictionary) -> void:
	card = card_to_show
	face = card_face
	size = SIZE
	pivot_offset = SIZE / 2
	mouse_entered.connect(_lift_to.bind(HOVER_LIFT))
	mouse_exited.connect(_lift_to.bind(0.0))
	mouse_entered.connect(func() -> void: hovered.emit(self))
	mouse_exited.connect(func() -> void: unhovered.emit(self))


func move_to(target_position: Vector2, target_rotation: float) -> void:
	stop_moving()
	tween = create_tween().set_parallel().set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(self, "position", target_position, MOVE_TIME)
	tween.tween_property(self, "rotation", target_rotation, MOVE_TIME)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			pressed_twice = event.double_click
			_grab()
		elif grabbed:
			_release()
	elif event is InputEventMouseMotion and grabbed and movable:
		global_position = get_global_mouse_position() - grab_offset
		dragged.emit(self)


func _grab() -> void:
	grabbed = true
	grab_start = get_global_mouse_position()
	grab_offset = grab_start - global_position
	if movable:
		stop_moving()
		get_parent().move_child(self, -1)
		picked_up.emit(self)


func stop_moving() -> void:
	if tween:
		tween.kill()


func _release() -> void:
	grabbed = false
	if get_global_mouse_position().distance_to(grab_start) < CLICK_DISTANCE:
		(double_clicked if pressed_twice else clicked).emit(self)
	elif movable:
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
	if marks_text:
		_draw_marks(rect)
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
	var rules_text := _field("text")
	var title := _field("title")
	var title_y := SIZE.y * 0.32 if rules_text else SIZE.y / 2
	draw_string(font, Vector2(MARGIN, title_y), title, HORIZONTAL_ALIGNMENT_CENTER, text_width, _fitting_size(font, title, text_width), ink)
	var line_y := title_y + (BODY_FONT_SIZE + 6 if rules_text else TITLE_FONT_SIZE)
	for column in face.get("body", []):
		draw_string(font, Vector2(MARGIN, line_y), card.data.get(column, ""), HORIZONTAL_ALIGNMENT_CENTER, text_width, BODY_FONT_SIZE, ink)
		line_y += BODY_FONT_SIZE + 4
	if rules_text:
		var text_size := _fitting_text_size(font, rules_text, text_width, SIZE.y - MARGIN - line_y)
		draw_multiline_string(font, Vector2(MARGIN, line_y + 2), rules_text, HORIZONTAL_ALIGNMENT_CENTER, text_width, text_size, -1, ink)


## Marks show on both sides, so a player can see what they know about a card they can't see.
func _draw_marks(rect: Rect2) -> void:
	var font := get_theme_default_font()
	var band := Rect2(rect.position.x + 4, rect.end.y - MARKS_HEIGHT - 6, rect.size.x - 8, MARKS_HEIGHT)
	var font_size := _fitting_size(font, marks_text, band.size.x - 4, MARKS_FONT_SIZE, MIN_TEXT_FONT_SIZE)
	draw_rect(band, MARKS_BACKGROUND)
	draw_string(font, Vector2(band.position.x, band.end.y - 5), marks_text, HORIZONTAL_ALIGNMENT_CENTER, band.size.x, font_size, Color.WHITE)


static func _fitting_text_size(font: Font, text: String, width: float, height: float) -> int:
	var font_size := TEXT_FONT_SIZE
	while font_size > MIN_TEXT_FONT_SIZE and font.get_multiline_string_size(text, HORIZONTAL_ALIGNMENT_CENTER, width, font_size).y > height:
		font_size -= 1
	return font_size


static func _fitting_size(font: Font, text: String, width: float, largest := TITLE_FONT_SIZE, smallest := MIN_TITLE_FONT_SIZE) -> int:
	var font_size := largest
	while font_size > smallest and font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x > width:
		font_size -= 1
	return font_size


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
