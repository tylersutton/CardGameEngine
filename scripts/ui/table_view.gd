class_name TableView
extends Control

const FELT_COLOR := Color(0.12, 0.38, 0.22)
const LABEL_COLOR := Color(1, 1, 1, 0.7)
const ZONE_LABEL_COLOR := Color(1, 1, 1, 0.45)
const SLOT_BORDER := Color(1, 1, 1, 0.25)
const CURRENT_SLOT_BORDER := Color(1, 0.85, 0.4, 0.8)
const LABEL_FONT_SIZE := 14
const LABEL_GAP := 22.0
const BOTTOM_MARGIN := 8.0
const ZOOM_GAP := 12.0
const LABEL_OVERHANG := 20.0
const SEAT_PADDING := Vector2(10, 4)
const SEAT_TEXT_COLOR := Color.WHITE
const CURRENT_SEAT_TEXT_COLOR := Color(0.1, 0.1, 0.1)

static var slot_style := _slot_box(SLOT_BORDER)
static var current_slot_style := _slot_box(CURRENT_SLOT_BORDER)
static var seat_style := _seat_box(Color(0, 0, 0, 0.45))
static var current_seat_style := _seat_box(CURRENT_SLOT_BORDER)

var game: Game
## Where inputs go: the game itself, or the online session (set by Main).
var submit: Callable
var views: Dictionary[Card, CardView] = {}
var show_zone_names := false
var preview_zone: Zone
var zoom_view: CardView
var hover_zone: Zone
var hover_allowed := false
var carried_views: Array[CardView] = []
var carried_offsets: Array[Vector2] = []


func _ready() -> void:
	resized.connect(_layout_all)


func show_game(game_to_show: Game) -> void:
	for view in views.values():
		view.queue_free()
	views.clear()
	game = game_to_show
	_make_zoom_view()
	for zone: Zone in game.zones.values():
		zone.changed.connect(_layout_zone)
		zone.changed.connect(queue_redraw.unbind(1))
	game.variables_changed.connect(queue_redraw)
	game.variables_changed.connect(_refresh_views)
	game.state_changed.connect(_refresh_views)
	_layout_all()
	queue_redraw()


func _layout_all() -> void:
	if game:
		for zone: Zone in game.zones.values():
			_layout_zone(zone)


## With an incoming card, the other cards make room for it at that index (the incoming card itself is being dragged).
func _layout_zone(zone: Zone, incoming: Card = null, index := 0) -> void:
	var center := zone.anchor * size
	var turn := deg_to_rad(zone.rotation)
	var groups := _groups(zone)
	if incoming:
		var arranged := zone.cards.filter(func(card: Card) -> bool: return card != incoming)
		arranged.insert(index, incoming)
		groups = [arranged]
	var group_centers := ZoneLayout.group_centers(groups.map(func(group: Array) -> int: return group.size()))
	for group_index in groups.size():
		var group: Array = groups[group_index]
		var column_offsets: Array = ZoneLayout.column_offsets(group.map(game.shows_face), _max_spread(zone)) if zone.layout == "column" else []
		for i in group.size():
			if group[i] == incoming:
				continue
			var view := _view_for(group[i], center)
			view.face_up = game.shows_face(group[i])
			move_child(view, -1)
			var placement := ZoneLayout.place(zone.layout, i, group.size(), _max_spread(zone), zone.spread)
			if zone.layout == "column":
				placement = Transform2D(0.0, Vector2(0, column_offsets[i]))
			var offset := (Vector2(group_centers[group_index], 0) + placement.origin).rotated(turn)
			view.move_to(center + offset - CardView.SIZE / 2, placement.get_rotation() + turn)


func _groups(zone: Zone) -> Array:
	if zone.layout != "melds":
		return [zone.cards]
	var deadwood := game.meld_rules.deadwood(zone.cards)
	return game.meld_rules.best(zone.cards) + ([deadwood] if deadwood else [])


func _refresh_views() -> void:
	for card in views:
		views[card].face_up = game.shows_face(card)
		views[card].highlighted = card in game.choices and game.is_local_turn()
		views[card].selected = card in game.selected
		views[card].movable = game.may_handle(card)
		views[card].marks_text = " ".join(card.marks.values().map(Game.format_value))


func _view_for(card: Card, spawn_center: Vector2) -> CardView:
	if card not in views:
		var view := CardView.new(card, game.rules.card_face)
		view.position = spawn_center - CardView.SIZE / 2
		view.picked_up.connect(_on_card_picked_up)
		view.dragged.connect(_on_card_dragged)
		view.clicked.connect(_on_card_clicked)
		view.double_clicked.connect(_on_card_double_clicked)
		view.hovered.connect(_on_card_hovered)
		view.unhovered.connect(func(_view: CardView) -> void: _hide_zoom())
		view.dropped.connect(_on_card_dropped)
		add_child(view)
		views[card] = view
	return views[card]


## With "zoom" in card_face, hovering a face-up card shows an enlarged copy beside it.
func _make_zoom_view() -> void:
	if zoom_view:
		zoom_view.queue_free()
		zoom_view = null
	if not game.rules.card_face.has("zoom") or game.zones.is_empty():
		return
	zoom_view = CardView.new(null, game.rules.card_face)
	zoom_view.pivot_offset = Vector2.ZERO
	zoom_view.mouse_filter = Control.MOUSE_FILTER_IGNORE
	zoom_view.z_index = 10
	zoom_view.visible = false
	add_child(zoom_view)


func _on_card_hovered(view: CardView) -> void:
	if not zoom_view or not view.face_up or view.grabbed:
		return
	var zoom: float = game.rules.card_face.zoom
	var zoom_size := CardView.SIZE * zoom
	var spot := view.position + Vector2(CardView.SIZE.x + ZOOM_GAP, (CardView.SIZE.y - zoom_size.y) / 2)
	if spot.x + zoom_size.x > size.x:
		spot.x = view.position.x - zoom_size.x - ZOOM_GAP
	zoom_view.card = view.card
	zoom_view.scale = Vector2(zoom, zoom)
	zoom_view.position = spot.clamp(Vector2.ZERO, size - zoom_size)
	zoom_view.visible = true
	zoom_view.queue_redraw()


func _hide_zoom() -> void:
	if zoom_view:
		zoom_view.visible = false


func _on_card_picked_up(view: CardView) -> void:
	_hide_zoom()
	carried_views.clear()
	carried_offsets.clear()
	for card in game.carried(view.card).slice(1):
		var carried_view := views[card]
		carried_view.stop_moving()
		move_child(carried_view, -1)
		carried_views.append(carried_view)
		carried_offsets.append(carried_view.position - view.position)


func _on_card_dragged(view: CardView) -> void:
	for i in carried_views.size():
		carried_views[i].position = view.position + carried_offsets[i]
	var source := game.zone_of(view.card)
	var target := _nearest_zone(get_local_mouse_position(), _dragged_views(view))
	_show_drop_hint(view, null if target == source else target)
	if not _makes_room(target) or target != source and not hover_allowed:
		target = null
	if preview_zone and preview_zone != target:
		_close_gap(view.card)
	preview_zone = target
	if target:
		_layout_zone(target, view.card, _drop_index(target, view.card, get_local_mouse_position()))
		for dragged_view in _dragged_views(view):
			move_child(dragged_view, -1)


## Outlines the zone the card would land in, and the card itself, green if the move is allowed or red if not.
func _show_drop_hint(view: CardView, target: Zone) -> void:
	if target != hover_zone:
		hover_zone = target
		hover_allowed = target != null and game.can_move(view.card, target)
		queue_redraw()
	if not hover_zone:
		view.drop_hint = CardView.DropHint.NONE
	else:
		view.drop_hint = CardView.DropHint.ALLOWED if hover_allowed else CardView.DropHint.REFUSED


## Back to the normal layout, except that a card still being dragged stays under the cursor.
func _close_gap(dragged_card: Card) -> void:
	if dragged_card in preview_zone.cards:
		_layout_zone(preview_zone, dragged_card, preview_zone.cards.find(dragged_card))
	else:
		_layout_zone(preview_zone)


func _makes_room(zone: Zone) -> bool:
	return zone.layout in ["row", "fan"]


func _drop_index(zone: Zone, card: Card, point: Vector2) -> int:
	if zone.sort_columns and card not in zone.cards:
		return zone.sorted_index(card)
	var slots := zone.cards.size() if card in zone.cards else zone.cards.size() + 1
	return _nearest_slot(zone, point, slots)


func _dragged_views(view: CardView) -> Array[CardView]:
	var dragged: Array[CardView] = [view]
	dragged.append_array(carried_views)
	return dragged


func _on_card_clicked(view: CardView) -> void:
	carried_views.clear()
	_show_drop_hint(view, null)
	if preview_zone:
		_layout_zone(preview_zone)
		preview_zone = null
	var zone := game.zone_of(view.card)
	submit.call({type = "click", card = view.card.id})
	_layout_zone(zone)


func _on_card_double_clicked(view: CardView) -> void:
	var zone := game.zone_of(view.card)
	submit.call({type = "double_click", card = view.card.id})
	_layout_zone(zone)


func _on_card_dropped(view: CardView) -> void:
	var source := game.zone_of(view.card)
	var drop_point := get_local_mouse_position()
	var destination := _nearest_zone(drop_point, _dragged_views(view))
	var index := _drop_index(destination, view.card, drop_point) if _makes_room(destination) else -1
	carried_views.clear()
	preview_zone = null
	_show_drop_hint(view, null)
	if destination == source and _makes_room(source):
		if not submit.call({type = "reorder", card = view.card.id, index = index}):
			_layout_zone(source)
	elif destination == source or not submit.call({type = "drag", card = view.card.id, to = destination.name, index = index}):
		_layout_zone(source)
		_layout_zone(destination)


func _gui_input(event: InputEvent) -> void:
	if game and event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		for zone: Zone in game.zones.values():
			if zone.is_empty() and _slot(zone).has_point(event.position):
				submit.call({type = "click_empty", zone = zone.name})


func _nearest_slot(zone: Zone, point: Vector2, slots: int) -> int:
	var offset := (point - zone.anchor * size).rotated(-deg_to_rad(zone.rotation)).x
	var nearest := 0
	var nearest_distance := INF
	for i in slots:
		var distance := absf(ZoneLayout.place(zone.layout, i, slots, _max_spread(zone)).origin.x - offset)
		if distance < nearest_distance:
			nearest = i
			nearest_distance = distance
	return nearest


func _max_spread(zone: Zone) -> float:
	if zone.layout == "column":
		return size.y * (1 - zone.anchor.y) - CardView.SIZE.y / 2 - BOTTOM_MARGIN
	return zone.width * (size.y if _is_sideways(zone) else size.x) if zone.width else ZoneLayout.MAX_WIDTH


func _is_sideways(zone: Zone) -> bool:
	return is_equal_approx(absf(sin(deg_to_rad(zone.rotation))), 1.0)


func _nearest_zone(point: Vector2, ignored_views: Array[CardView]) -> Zone:
	var nearest: Zone
	var nearest_distance := INF
	for zone: Zone in game.zones.values():
		var centers: Array[Vector2] = [zone.anchor * size]
		for card in zone.cards:
			if views[card] not in ignored_views:
				centers.append(views[card].position + CardView.SIZE / 2)
		for center in centers:
			if point.distance_to(center) < nearest_distance:
				nearest = zone
				nearest_distance = point.distance_to(center)
	return nearest


func _slot(zone: Zone) -> Rect2:
	var slot_size := Vector2(CardView.SIZE.y, CardView.SIZE.x) if _is_sideways(zone) else CardView.SIZE
	return Rect2(zone.anchor * size - slot_size / 2, slot_size)


func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, size), FELT_COLOR)
	if not game:
		return
	var font := get_theme_default_font()
	for zone: Zone in game.zones.values():
		if not zone.has_outline or zone.layout == "melds" and zone.is_empty():
			continue
		var slot := _slot(zone)
		var is_current := game.rules.players > 1 and zone.owner == game.current_player()
		draw_style_box(current_slot_style if is_current else slot_style, slot)
		if zone.label:
			var baseline := Vector2(slot.position.x, slot.get_center().y + LABEL_FONT_SIZE / 3.0)
			draw_string(font, baseline, zone.label, HORIZONTAL_ALIGNMENT_CENTER, slot.size.x, LABEL_FONT_SIZE, ZONE_LABEL_COLOR)
		if show_zone_names:
			var name_position := slot.position - Vector2(LABEL_OVERHANG, LABEL_GAP)
			var name_text := "%s (%d)" % [zone.name, zone.cards.size()]
			draw_string(font, name_position, name_text, HORIZONTAL_ALIGNMENT_CENTER, slot.size.x + LABEL_OVERHANG * 2, LABEL_FONT_SIZE, LABEL_COLOR)
	if hover_zone:
		draw_style_box(CardView.allowed_style if hover_allowed else CardView.refused_style, _slot(hover_zone).grow(CardView.HIGHLIGHT_WIDTH / 2.0))
	_draw_seats(font)


func _draw_seats(font: Font) -> void:
	for player in range(1, game.rules.seats.size() + 1):
		var is_current := player == game.current_player()
		var text := game.player_name(player)
		var text_size := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE)
		var seat := Vector2(game.rules.seats[player - 1][0], game.rules.seats[player - 1][1]) * size
		var tag := Rect2(seat - text_size / 2 - SEAT_PADDING, text_size + SEAT_PADDING * 2)
		draw_style_box(current_seat_style if is_current else seat_style, tag)
		var baseline := Vector2(tag.position.x + SEAT_PADDING.x, tag.position.y + SEAT_PADDING.y + font.get_ascent(LABEL_FONT_SIZE))
		draw_string(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE, CURRENT_SEAT_TEXT_COLOR if is_current else SEAT_TEXT_COLOR)


static func _seat_box(color: Color) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = color
	box.set_corner_radius_all(10)
	return box


static func _slot_box(border_color: Color) -> StyleBoxFlat:
	var box := StyleBoxFlat.new()
	box.bg_color = Color(0, 0, 0, 0.15)
	box.border_color = border_color
	box.set_border_width_all(2)
	box.set_corner_radius_all(8)
	return box
